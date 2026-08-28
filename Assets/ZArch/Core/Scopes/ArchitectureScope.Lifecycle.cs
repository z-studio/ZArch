using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    public sealed partial class ArchitectureScope {
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
            } else if (instance is not IDeinitializable && instance is not IAsyncDeinitializable) {
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
                    if (instance is not IDeinitializable && instance is not IAsyncDeinitializable) {
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
            return instance is IInitializable
                or IAsyncInitializable
                or IDeinitializable
                or IAsyncDeinitializable;
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

            if (State is not EScopeState.Initializing and not EScopeState.Active) {
                throw new InvalidOperationException(
                    $"Scope '{Name}' cannot resolve services while it is in state {State}. "
                    + "Register a factory and resolve after configuration completes."
                );
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

            var cleanupExceptions = new List<Exception>();
            State = EScopeState.Disposing;
            TryCleanup(m_LifetimeCts.Cancel, cleanupExceptions);

            for (var i = m_Children.Count - 1; i >= 0; i--) {
                TryCleanup(m_Children[i].Dispose, cleanupExceptions);
            }

            m_Children.Clear();

            var eventUnregisters = new List<TrackedEventUnregister>(m_EventUnregisters);
            m_EventUnregisters.Clear();

            foreach (var unregister in eventUnregisters) {
                TryCleanup(unregister.Unregister, cleanupExceptions);
            }

            m_Events.Clear();

            for (var i = m_InitializedInstances.Count - 1; i >= 0; i--) {
                var instance = m_InitializedInstances[i];

                try {
                    if (instance is IDeinitializable deinitializable) {
                        deinitializable.Deinitialize();
                    } else if (instance is IAsyncDeinitializable) {
                        throw new InvalidOperationException(
                            $"{instance.GetType().FullName} requires asynchronous cleanup. "
                            + "Use ArchitectureScope.DisposeAsync or Architecture.ShutdownAsync."
                        );
                    }
                } catch (Exception exception) {
                    cleanupExceptions.Add(exception);
                }
            }

            m_InitializedInstances.Clear();
            m_InitializedSet.Clear();

            for (var i = m_OwnedInstances.Count - 1; i >= 0; i--) {
                if (m_OwnedInstances[i] is IDisposable disposable) {
                    TryCleanup(disposable.Dispose, cleanupExceptions);
                }
            }

            m_OwnedInstances.Clear();
            m_OwnedSet.Clear();
            m_Registrations.Clear();
            m_RegistrationOrder.Clear();
            State = EScopeState.Disposed;
            TryCleanup(m_LifetimeCts.Dispose, cleanupExceptions);
            Parent?.m_Children.Remove(this);
            Architecture.OnScopeDisposed(this);
            ReportCleanupExceptions(cleanupExceptions);
        }

        public Task DisposeAsync(CancellationToken cancellationToken = default) {
            if (State == EScopeState.Disposed) {
                return Task.CompletedTask;
            }

            if (m_DisposeTask != null) {
                return m_DisposeTask;
            }

            if (State == EScopeState.Disposing) {
                return Task.CompletedTask;
            }

            m_DisposeTask = DisposeAsyncCore(cancellationToken);
            return m_DisposeTask;
        }

        private async Task DisposeAsyncCore(CancellationToken cancellationToken) {
            var cleanupExceptions = new List<Exception>();
            State = EScopeState.Disposing;
            TryCleanup(m_LifetimeCts.Cancel, cleanupExceptions);

            for (var i = m_Children.Count - 1; i >= 0; i--) {
                try {
                    await m_Children[i].DisposeAsync(cancellationToken).ConfigureAwait(true);
                } catch (Exception exception) {
                    cleanupExceptions.Add(exception);
                }
            }

            m_Children.Clear();

            var eventUnregisters = new List<TrackedEventUnregister>(m_EventUnregisters);
            m_EventUnregisters.Clear();

            foreach (var unregister in eventUnregisters) {
                TryCleanup(unregister.Unregister, cleanupExceptions);
            }

            m_Events.Clear();

            for (var i = m_InitializedInstances.Count - 1; i >= 0; i--) {
                var instance = m_InitializedInstances[i];

                try {
                    if (instance is IAsyncDeinitializable asyncDeinitializable) {
                        var task = asyncDeinitializable.DeinitializeAsync(cancellationToken)
                                   ?? throw new InvalidOperationException(
                                       $"{instance.GetType().FullName}.DeinitializeAsync returned null."
                                   );
                        await task.ConfigureAwait(true);
                    } else if (instance is IDeinitializable deinitializable) {
                        deinitializable.Deinitialize();
                    }
                } catch (Exception exception) {
                    cleanupExceptions.Add(exception);
                }
            }

            m_InitializedInstances.Clear();
            m_InitializedSet.Clear();

            for (var i = m_OwnedInstances.Count - 1; i >= 0; i--) {
                if (m_OwnedInstances[i] is IDisposable disposable) {
                    TryCleanup(disposable.Dispose, cleanupExceptions);
                }
            }

            m_OwnedInstances.Clear();
            m_OwnedSet.Clear();
            m_Registrations.Clear();
            m_RegistrationOrder.Clear();
            State = EScopeState.Disposed;
            TryCleanup(m_LifetimeCts.Dispose, cleanupExceptions);
            Parent?.m_Children.Remove(this);
            Architecture.OnScopeDisposed(this);
            ReportCleanupExceptions(cleanupExceptions);
        }

        private static void TryCleanup(Action cleanup, List<Exception> cleanupExceptions) {
            try {
                cleanup?.Invoke();
            } catch (Exception exception) {
                cleanupExceptions.Add(exception);
            }
        }

        private void ReportCleanupExceptions(List<Exception> cleanupExceptions) {
            if (cleanupExceptions.Count == 0) {
                return;
            }

            Architecture.ReportUnhandledException(
                cleanupExceptions.Count == 1
                    ? cleanupExceptions[0]
                    : new AggregateException($"Scope '{Name}' cleanup failed.", cleanupExceptions)
            );
        }
    }
}
