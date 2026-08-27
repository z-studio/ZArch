using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    public sealed class ArchitectureScope : IServiceResolver, IDisposable {
        private readonly List<ArchitectureScope> m_Children = new();
        private readonly ReadOnlyCollection<ArchitectureScope> m_ChildrenView;
        private readonly Dictionary<Type, ServiceRegistration> m_Registrations = new();
        private readonly List<ServiceRegistration> m_RegistrationOrder = new();
        private readonly List<object> m_OwnedInstances = new();
        private readonly HashSet<object> m_OwnedSet = new(ReferenceEqualityComparer.sInstance);
        private readonly List<object> m_InitializedInstances = new();
        private readonly HashSet<object> m_InitializedSet = new(ReferenceEqualityComparer.sInstance);
        private readonly TypeEventSystem m_Events = new();
        private readonly List<IUnregister> m_EventUnregisters = new();
        private readonly CancellationTokenSource m_LifetimeCts = new();
        private int m_NextRegistrationOrder;

        public string Name { get; }
        public object Tag { get; }
        public ArchitectureScope Parent { get; }
        public Architecture Architecture { get; }
        public EScopeState State { get; private set; } = EScopeState.Created;
        public bool IsDisposed => State == EScopeState.Disposed;
        public bool IsActivated => State == EScopeState.Active;
        public bool IsActivating => State is EScopeState.Configuring or EScopeState.Initializing;

        public string BoundSceneName { get; set; }

        public IReadOnlyList<ArchitectureScope> Children => m_ChildrenView;
        internal IReadOnlyList<object> OwnedInstances => m_OwnedInstances;
        internal IReadOnlyList<ArchitectureScope> ChildScopes => m_Children;
        internal CancellationToken LifetimeToken => m_LifetimeCts.Token;

        internal ArchitectureScope(Architecture architecture, string name, ArchitectureScope parent, object tag) {
            Architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
            m_ChildrenView = m_Children.AsReadOnly();

            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Scope name is empty.", nameof(name))
                : name;

            Parent = parent;
            Tag = tag;
        }

        internal void AddChild(ArchitectureScope child) {
            if (child == null) {
                throw new ArgumentNullException(nameof(child));
            }

            if (!m_Children.Contains(child)) {
                m_Children.Add(child);
            }
        }

        public void Register<TService>(TService instance, bool owned = true, int initializationOrder = 0)
            where TService : class {
            if (instance == null) {
                throw new ArgumentNullException(nameof(instance));
            }

            RegisterDescriptor(
                typeof(TService),
                _ => instance,
                EServiceLifetime.Scoped,
                owned,
                initializationOrder,
                instance,
                true
            );
        }

        public void Register<TService, TImplementation>(bool owned = true, int initializationOrder = 0)
            where TService : class
            where TImplementation : class, TService, new() {
            RegisterFactory<TService>(_ => new TImplementation(), EServiceLifetime.Scoped, owned, initializationOrder);
        }

        public void RegisterFactory<TService>(
            Func<IServiceResolver, TService> factory,
            EServiceLifetime lifetime = EServiceLifetime.Scoped,
            bool owned = true,
            int initializationOrder = 0
        ) where TService : class {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterDescriptor(typeof(TService), factory, lifetime, owned, initializationOrder, null, true);
        }

        public void RegisterAlias<TAlias, TService>()
            where TAlias : class
            where TService : class, TAlias {
            RegisterDescriptor(
                typeof(TAlias),
                resolver => resolver.Resolve<TService>(),
                EServiceLifetime.Scoped,
                false,
                0,
                null,
                false
            );
        }

        private void RegisterDescriptor(
            Type serviceType,
            Func<IServiceResolver, object> factory,
            EServiceLifetime lifetime,
            bool owned,
            int initializationOrder,
            object instance,
            bool attachContext
        ) {
            EnsureConfigurable();

            if (lifetime is not EServiceLifetime.Scoped and not EServiceLifetime.Transient) {
                throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unknown service lifetime.");
            }

            if (m_Registrations.ContainsKey(serviceType)) {
                throw new InvalidOperationException(
                    $"Type {serviceType.FullName} is already registered in scope '{Name}'."
                );
            }

            var registration = new ServiceRegistration {
                ServiceType = serviceType,
                Factory = factory,
                Lifetime = lifetime,
                Owned = owned,
                InitializationOrder = initializationOrder,
                RegistrationOrder = m_NextRegistrationOrder++,
                Instance = instance,
                AttachContext = attachContext
            };

            m_Registrations.Add(serviceType, registration);
            m_RegistrationOrder.Add(registration);

            if (instance != null) {
                if (attachContext) {
                    AttachContext(instance);
                }

                if (owned) {
                    AddOwned(instance);
                }
            }
        }

        public object Resolve(Type serviceType) {
            if (serviceType == null) {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (TryResolve(serviceType, out var instance)) {
                return instance;
            }

            throw new KeyNotFoundException(
                $"Service {serviceType.FullName} is not registered from scope '{Name}' to the root."
            );
        }

        public bool TryResolve(Type serviceType, out object instance) {
            if (serviceType == null) {
                throw new ArgumentNullException(nameof(serviceType));
            }

            EnsureResolvable();

            if (m_Registrations.TryGetValue(serviceType, out var registration)) {
                instance = GetOrCreate(registration);
                return true;
            }

            if (Parent != null) {
                return Parent.TryResolve(serviceType, out instance);
            }

            instance = null;
            return false;
        }

        public T Resolve<T>() where T : class => (T)Resolve(typeof(T));

        public bool TryResolve<T>(out T instance) where T : class {
            if (TryResolve(typeof(T), out var raw)) {
                instance = raw as T;
                return instance != null;
            }

            instance = null;
            return false;
        }

        private object GetOrCreate(ServiceRegistration registration) {
            if (registration.Lifetime == EServiceLifetime.Scoped && registration.Instance != null) {
                return registration.Instance;
            }

            if (registration.IsCreating) {
                throw new InvalidOperationException(
                    $"Circular dependency detected while creating {registration.ServiceType.FullName} in scope '{Name}'."
                );
            }

            registration.IsCreating = true;

            try {
                var created = registration.Factory(this)
                              ?? throw new InvalidOperationException(
                                  $"Factory for {registration.ServiceType.FullName} returned null in scope '{Name}'."
                              );

                if (!registration.ServiceType.IsInstanceOfType(created)) {
                    throw new InvalidOperationException(
                        $"Factory for {registration.ServiceType.FullName} returned incompatible type {created.GetType().FullName}."
                    );
                }

                if (registration.Lifetime == EServiceLifetime.Transient
                    && registration.Owned
                    && HasLifecycle(created)) {
                    var exception = new InvalidOperationException(
                        $"Transient service {registration.ServiceType.FullName} has a managed lifecycle. "
                        + "Use EServiceLifetime.Scoped so initialization is deterministic."
                    );

                    if (created is IDisposable disposable) {
                        try {
                            disposable.Dispose();
                        } catch (Exception disposeException) {
                            throw new AggregateException(exception, disposeException);
                        }
                    }

                    throw exception;
                }

                if (registration.AttachContext) {
                    AttachContext(created);
                }

                if (registration.Owned) {
                    AddOwned(created);
                }

                if (registration.Lifetime == EServiceLifetime.Scoped) {
                    registration.Instance = created;
                }

                return created;
            } finally {
                registration.IsCreating = false;
            }
        }

        public IUnregister RegisterEvent<T>(Action<T> onEvent) {
            EnsureNotDisposed();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            var unregister = m_Events.Register(onEvent);
            m_EventUnregisters.Add(unregister);
            return unregister;
        }

        public void Publish<T>(T message, EEventPropagation propagation = EEventPropagation.Local) {
            EnsureNotDisposed();

            if (propagation is not EEventPropagation.Local and not EEventPropagation.Parents) {
                throw new ArgumentOutOfRangeException(nameof(propagation), propagation, "Unknown propagation mode.");
            }

            List<Exception> exceptions = null;

            for (var scope = this; scope != null;
                 scope = propagation == EEventPropagation.Parents ? scope.Parent : null) {
                try {
                    scope.m_Events.Send(message);
                } catch (Exception exception) {
                    exceptions ??= new List<Exception>();

                    if (exception is AggregateException aggregate) {
                        exceptions.AddRange(aggregate.Flatten().InnerExceptions);
                    } else {
                        exceptions.Add(exception);
                    }
                }
            }

            if (exceptions != null) {
                throw new AggregateException(exceptions);
            }
        }

        public ArchitectureScope CreateChild(string name, Action<ArchitectureScope> setup, object tag = null) =>
            Architecture.CreateChildScope(this, name, setup, tag);

        public Task<ArchitectureScope> CreateChildAsync(
            string name,
            Func<ArchitectureScope, Task> setup,
            object tag = null
        ) =>
            Architecture.CreateChildScopeAsync(this, name, setup, tag);

        public Task<ArchitectureScope> CreateChildAsync(
            string name,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            object tag = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        ) =>
            Architecture.CreateChildScopeAsync(this, name, setup, tag, timeout, cancellationToken);

        internal void BeginConfiguration() {
            if (State != EScopeState.Created) {
                throw new InvalidOperationException($"Scope '{Name}' cannot be configured from state {State}.");
            }

            State = EScopeState.Configuring;
        }

        internal void Activate() {
            if (State != EScopeState.Configuring) {
                throw new InvalidOperationException($"Scope '{Name}' cannot activate from state {State}.");
            }

            State = EScopeState.Initializing;

            try {
                MaterializeScopedServices();
                ValidateSynchronousInitialization();

                foreach (var registration in OrderedRegistrations()) {
                    InitializeSynchronously(registration);
                }

                State = EScopeState.Active;
            } catch {
                State = EScopeState.Faulted;
                throw;
            }
        }

        internal async Task ActivateAsync(CancellationToken cancellationToken) {
            if (State != EScopeState.Configuring) {
                throw new InvalidOperationException($"Scope '{Name}' cannot activate from state {State}.");
            }

            State = EScopeState.Initializing;

            try {
                MaterializeScopedServices();

                foreach (var registration in OrderedRegistrations()) {
                    cancellationToken.ThrowIfCancellationRequested();
                    await InitializeAsynchronously(registration, cancellationToken).ConfigureAwait(true);
                    EnsureInitializing(cancellationToken);
                }

                EnsureInitializing(cancellationToken);
                State = EScopeState.Active;
            } catch {
                if (State is not EScopeState.Disposing and not EScopeState.Disposed) {
                    State = EScopeState.Faulted;
                }

                throw;
            }
        }

        private void MaterializeScopedServices() {
            foreach (var registration in m_RegistrationOrder) {
                if (registration.Lifetime == EServiceLifetime.Scoped) {
                    GetOrCreate(registration);
                }
            }
        }

        private List<ServiceRegistration> OrderedRegistrations() {
            var result = new List<ServiceRegistration>(m_RegistrationOrder);

            result.Sort((a, b) => {
                    var byOrder = a.InitializationOrder.CompareTo(b.InitializationOrder);
                    return byOrder != 0 ? byOrder : a.RegistrationOrder.CompareTo(b.RegistrationOrder);
                }
            );

            return result;
        }

        private void ValidateSynchronousInitialization() {
            foreach (var registration in m_RegistrationOrder) {
                var instance = registration.Instance;

                if (registration.Owned && instance is IAsyncInitializable) {
                    throw new InvalidOperationException(
                        $"{registration.ServiceType.FullName} requires asynchronous initialization. Use an async scope API."
                    );
                }
            }
        }

        private void InitializeSynchronously(ServiceRegistration registration) {
            var instance = registration.Instance;

            if (!registration.Owned || instance == null || m_InitializedSet.Contains(instance)) {
                return;
            }

            if (instance is IInitializable initializable) {
                initializable.Initialize();
            } else if (instance is not IDeinitializable) {
                return;
            }

            MarkInitialized(instance);
        }

        private async Task InitializeAsynchronously(ServiceRegistration registration, CancellationToken token) {
            var instance = registration.Instance;

            if (!registration.Owned || instance == null || m_InitializedSet.Contains(instance)) {
                return;
            }

            switch (instance) {
                case IAsyncInitializable asyncInitializable:
                    var initializeTask = asyncInitializable.InitializeAsync(token)
                                         ?? throw new InvalidOperationException(
                                             $"{registration.ServiceType.FullName}.InitializeAsync returned null."
                                         );

                    await initializeTask.ConfigureAwait(true);
                    EnsureInitializing(token);
                    break;
                case IInitializable initializable:
                    initializable.Initialize();
                    break;

                default: {
                    if (instance is not IDeinitializable) {
                        return;
                    }

                    break;
                }
            }

            MarkInitialized(instance);
        }

        private void EnsureInitializing(CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            if (State != EScopeState.Initializing) {
                throw new InvalidOperationException($"Scope '{Name}' stopped initializing from state {State}.");
            }
        }

        private void MarkInitialized(object instance) {
            if (!m_InitializedSet.Add(instance)) {
                return;
            }

            m_InitializedInstances.Add(instance);

            foreach (var registration in m_RegistrationOrder) {
                if (ReferenceEquals(registration.Instance, instance)) {
                    registration.IsInitialized = true;
                }
            }
        }

        private void AttachContext(object instance) {
            if (instance is ICanSetScope setScope) {
                setScope.SetScope(this);
            }
        }

        private void AddOwned(object instance) {
            if (!m_OwnedSet.Add(instance)) {
                return;
            }

            m_OwnedInstances.Add(instance);
        }

        private static bool HasLifecycle(object instance) {
            return instance is IInitializable or IAsyncInitializable or IDeinitializable;
        }

        public IReadOnlyList<ServiceDebugInfo> GetServiceDebugInfo() {
            var result = new List<ServiceDebugInfo>(m_RegistrationOrder.Count);

            foreach (var registration in m_RegistrationOrder) {
                result.Add(
                    new ServiceDebugInfo {
                        ServiceType = registration.ServiceType,
                        ImplementationType = registration.Instance?.GetType(),
                        Lifetime = registration.Lifetime,
                        IsCreated = registration.Instance != null,
                        IsInitialized = registration.IsInitialized,
                        IsOwned = registration.Owned
                    }
                );
            }

            return result;
        }

        private void EnsureConfigurable() {
            if (State != EScopeState.Created && State != EScopeState.Configuring) {
                throw new InvalidOperationException($"Scope '{Name}' is immutable in state {State}.");
            }
        }

        private void EnsureResolvable() {
            if (State is EScopeState.Disposing or EScopeState.Disposed or EScopeState.Faulted) {
                throw new ObjectDisposedException($"Scope '{Name}' is in state {State}.");
            }
        }

        private void EnsureNotDisposed() {
            if (State is EScopeState.Disposing or EScopeState.Disposed or EScopeState.Faulted) {
                throw new ObjectDisposedException($"Scope '{Name}'");
            }
        }

        public void Dispose() {
            if (State is EScopeState.Disposed or EScopeState.Disposing) {
                return;
            }

            State = EScopeState.Disposing;
            TryCleanup(m_LifetimeCts.Cancel);

            for (var i = m_Children.Count - 1; i >= 0; i--) {
                TryCleanup(m_Children[i].Dispose);
            }

            m_Children.Clear();

            for (var i = m_EventUnregisters.Count - 1; i >= 0; i--) {
                var unregister = m_EventUnregisters[i];
                TryCleanup(unregister.Unregister);
            }

            m_EventUnregisters.Clear();
            m_Events.Clear();

            for (var i = m_InitializedInstances.Count - 1; i >= 0; i--) {
                var instance = m_InitializedInstances[i];

                try {
                    if (instance is IDeinitializable deinitializable) {
                        deinitializable.Deinitialize();
                    }
                } catch (Exception exception) {
                    Architecture.ReportException(exception);
                }
            }

            m_InitializedInstances.Clear();
            m_InitializedSet.Clear();

            for (var i = m_OwnedInstances.Count - 1; i >= 0; i--) {
                if (m_OwnedInstances[i] is IDisposable disposable) {
                    TryCleanup(disposable.Dispose);
                }
            }

            m_OwnedInstances.Clear();
            m_OwnedSet.Clear();
            m_Registrations.Clear();
            m_RegistrationOrder.Clear();
            BoundSceneName = null;
            State = EScopeState.Disposed;
            TryCleanup(m_LifetimeCts.Dispose);
            Parent?.m_Children.Remove(this);
            Architecture.OnScopeDisposed(this);
        }

        private void TryCleanup(Action cleanup) {
            try {
                cleanup?.Invoke();
            } catch (Exception exception) {
                Architecture.ReportException(exception);
            }
        }
    }
}