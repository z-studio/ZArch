using System;

namespace ZArch {
    public partial class Architecture {
        public void Start() {
            if (m_IsTerminated) {
                throw new InvalidOperationException(
                    $"{GetType().Name} has already shutdown and cannot be restarted. Create a new instance instead."
                );
            }

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

        private void EnsureStarted() {
            if (!IsStarted || m_IsShuttingDown) {
                throw new InvalidOperationException("Call Architecture.Start first.");
            }
        }

        public void Shutdown() {
            if (m_IsShuttingDown || m_IsTerminated) {
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

                var pending = m_PendingScopes.ToArray();

                for (var i = pending.Length - 1; i >= 0; i--) {
                    pending[i]?.Dispose();
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
                m_PendingScopes.Clear();
                m_EventSystem.Clear();
                ScopeConfiguring = null;
                m_IsShuttingDown = false;
                m_IsTerminated = true;
            }
        }

        public void Dispose() => Shutdown();
    }
}
