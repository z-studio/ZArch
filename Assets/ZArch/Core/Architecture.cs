using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    public interface IArchitecture {
        bool IsStarted { get; }
        IReadOnlyList<ArchitectureScope> RootScopes { get; }

        ArchitectureScope CreateRootScope(string name, Action<ArchitectureScope> setup, object tag = null);

        Task<ArchitectureScope> CreateRootScopeAsync(
            string name,
            Func<ArchitectureScope, Task> setup,
            object tag = null
        );

        Task<ArchitectureScope> CreateRootScopeAsync(
            string name,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            object tag = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        );

        void SendEvent<T>() where T : new();
        void SendEvent<T>(T message);
        IUnregister RegisterEvent<T>(Action<T> onEvent);
        void UnregisterEvent<T>(Action<T> onEvent);
    }

    public abstract class Architecture : IArchitecture, IDisposable {
        private readonly List<ArchitectureScope> m_RootScopes = new();
        private readonly List<ArchitectureScope> m_AllScopes = new();
        private readonly ReadOnlyCollection<ArchitectureScope> m_RootScopesView;
        private readonly ReadOnlyCollection<ArchitectureScope> m_AllScopesView;
        private readonly TypeEventSystem m_EventSystem = new();
        private bool m_IsShuttingDown;
        private bool m_HasStartedLifecycle;

        public bool IsStarted { get; private set; }
        public Action<Exception> ExceptionHandler { get; set; }
        public IReadOnlyList<ArchitectureScope> RootScopes => m_RootScopesView;
        public IReadOnlyList<ArchitectureScope> Scopes => m_AllScopesView;
        public event Action<ArchitectureScope> ScopeConfiguring;

        protected Architecture() {
            m_RootScopesView = m_RootScopes.AsReadOnly();
            m_AllScopesView = m_AllScopes.AsReadOnly();
        }

        public void Start() {
            if (m_IsShuttingDown) {
                throw new InvalidOperationException($"{GetType().Name} is shutting down.");
            }

            if (IsStarted) {
                throw new InvalidOperationException($"{GetType().Name} is already started.");
            }

            m_HasStartedLifecycle = true;

            try {
                OnStart();
                IsStarted = true;
            } catch {
                IsStarted = false;
                Shutdown();
                throw;
            }
        }

        protected virtual void OnStart() { }
        protected virtual void OnShutdown() { }

        public ArchitectureScope CreateRootScope(string name, Action<ArchitectureScope> setup, object tag = null) {
            EnsureStarted();

            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            var scope = CreateUnconfiguredScope(name, null, tag);
            ActivateScope(scope, setup);
            return scope;
        }

        public Task<ArchitectureScope> CreateRootScopeAsync(
            string name,
            Func<ArchitectureScope, Task> setup,
            object tag = null
        ) {
            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            return CreateRootScopeAsync(
                name,
                (scope, _) => setup(scope),
                tag,
                null,
                CancellationToken.None
            );
        }

        public Task<ArchitectureScope> CreateRootScopeAsync(
            string name,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            object tag = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        ) {
            EnsureStarted();

            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            ValidateTimeout(timeout);
            var scope = CreateUnconfiguredScope(name, null, tag);
            return ActivateScopeAsync(scope, setup, timeout, cancellationToken);
        }

        internal ArchitectureScope CreateChildScope(
            ArchitectureScope parent,
            string name,
            Action<ArchitectureScope> setup,
            object tag
        ) {
            ValidateParent(parent);

            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            var scope = CreateUnconfiguredScope(name, parent, tag);
            ActivateScope(scope, setup);
            return scope;
        }

        internal Task<ArchitectureScope> CreateChildScopeAsync(
            ArchitectureScope parent,
            string name,
            Func<ArchitectureScope, Task> setup,
            object tag
        ) {
            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            return CreateChildScopeAsync(
                parent,
                name,
                (scope, _) => setup(scope),
                tag,
                null,
                CancellationToken.None
            );
        }

        internal Task<ArchitectureScope> CreateChildScopeAsync(
            ArchitectureScope parent,
            string name,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            object tag,
            TimeSpan? timeout,
            CancellationToken cancellationToken
        ) {
            ValidateParent(parent);

            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            ValidateTimeout(timeout);
            var scope = CreateUnconfiguredScope(name, parent, tag);
            return ActivateScopeAsync(scope, setup, timeout, cancellationToken);
        }

        private ArchitectureScope CreateUnconfiguredScope(string name, ArchitectureScope parent, object tag) {
            EnsureStarted();
            var scope = new ArchitectureScope(this, name, parent, tag);
            m_AllScopes.Add(scope);

            if (parent == null) {
                m_RootScopes.Add(scope);
            } else {
                parent.AddChild(scope);
            }

            return scope;
        }

        private void ActivateScope(ArchitectureScope scope, Action<ArchitectureScope> setup) {
            scope.BeginConfiguration();

            try {
                setup(scope);
                ScopeConfiguring?.Invoke(scope);
                scope.Activate();
            } catch {
                scope.Dispose();
                throw;
            }
        }

        private async Task<ArchitectureScope> ActivateScopeAsync(
            ArchitectureScope scope,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            TimeSpan? timeout,
            CancellationToken cancellationToken
        ) {
            scope.BeginConfiguration();

            CancellationTokenSource timeoutCts = null;
            CancellationTokenSource linkedCts = null;

            try {
                timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
                linkedCts = timeoutCts == null
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                linkedCts.Token.ThrowIfCancellationRequested();
                var setupTask = setup(scope, linkedCts.Token)
                                ?? throw new InvalidOperationException(
                                    $"Async setup for scope '{scope.Name}' returned null."
                                );
                await setupTask.ConfigureAwait(true);
                linkedCts.Token.ThrowIfCancellationRequested();
                ScopeConfiguring?.Invoke(scope);
                await scope.ActivateAsync(linkedCts.Token).ConfigureAwait(true);
                return scope;
            } catch {
                scope.Dispose();
                throw;
            } finally {
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
            }
        }

        internal void OnScopeDisposed(ArchitectureScope scope) {
            m_AllScopes.Remove(scope);
            m_RootScopes.Remove(scope);
        }

        public void ReportException(Exception exception) {
            if (exception == null) {
                throw new ArgumentNullException(nameof(exception));
            }

            var handlers = ExceptionHandler;

            if (handlers == null) {
                return;
            }

            foreach (Action<Exception> handler in handlers.GetInvocationList()) {
                try {
                    handler(exception);
                } catch {
                    // Exception reporting must never interrupt scope cleanup.
                }
            }
        }

        public void SendEvent<T>() where T : new() {
            EnsureStarted();
            m_EventSystem.Send<T>();
        }

        public void SendEvent<T>(T message) {
            EnsureStarted();
            m_EventSystem.Send(message);
        }

        public IUnregister RegisterEvent<T>(Action<T> onEvent) {
            EnsureStarted();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return m_EventSystem.Register(onEvent);
        }

        public void UnregisterEvent<T>(Action<T> onEvent) {
            EnsureStarted();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_EventSystem.Unregister(onEvent);
        }

        private void ValidateParent(ArchitectureScope parent) {
            EnsureStarted();

            if (parent == null) {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!ReferenceEquals(parent.Architecture, this)) {
                throw new InvalidOperationException("A child scope must belong to the same Architecture.");
            }

            if (parent.State is EScopeState.Disposing or EScopeState.Disposed) {
                throw new ObjectDisposedException(parent.Name);
            }

            if (parent.State == EScopeState.Faulted) {
                throw new InvalidOperationException($"Scope '{parent.Name}' is faulted.");
            }
        }

        private void EnsureStarted() {
            if (!IsStarted || m_IsShuttingDown) {
                throw new InvalidOperationException("Call Architecture.Start first.");
            }
        }

        private static void ValidateTimeout(TimeSpan? timeout) {
            if (!timeout.HasValue) {
                return;
            }

            using var validationCts = new CancellationTokenSource(timeout.Value);
        }

        public void Shutdown() {
            if (m_IsShuttingDown) {
                return;
            }

            m_IsShuttingDown = true;
            IsStarted = false;
            var callOnShutdown = m_HasStartedLifecycle;
            m_HasStartedLifecycle = false;

            try {
                var roots = m_RootScopes.ToArray();

                for (var i = roots.Length - 1; i >= 0; i--) {
                    roots[i]?.Dispose();
                }

                if (callOnShutdown) {
                    try {
                        OnShutdown();
                    } catch (Exception exception) {
                        ReportException(exception);
                    }
                }
            } finally {
                m_RootScopes.Clear();
                m_AllScopes.Clear();
                m_EventSystem.Clear();
                ScopeConfiguring = null;
                m_IsShuttingDown = false;
            }
        }

        public void Dispose() => Shutdown();
    }
}
