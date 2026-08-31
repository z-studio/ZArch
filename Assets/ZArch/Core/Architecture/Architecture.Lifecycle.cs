using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

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
            IsStarted = true;

            try {
                OnStart();

                if (!IsStarted || m_IsTerminated) {
                    throw new InvalidOperationException(
                        $"{GetType().Name} was shutdown while OnStart was running."
                    );
                }
            } catch (Exception startupException) {
                IsStarted = false;

                try {
                    Shutdown();
                } catch (Exception cleanupException) {
                    throw new AggregateException(
                        $"Starting {GetType().Name} failed, and rolling it back also failed.",
                        startupException,
                        cleanupException
                    );
                }

                ExceptionDispatchInfo.Capture(startupException).Throw();
            }
        }

        public void ReportUnhandledException(Exception exception) {
            if (exception == null) {
                throw new ArgumentNullException(nameof(exception));
            }

            var handlers = UnhandledExceptionHandler;

            if (handlers == null) {
                ExceptionDispatchInfo.Capture(exception).Throw();
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

            var cleanupExceptions = new List<Exception>();
            m_IsShuttingDown = true;
            IsStarted = false;
            var callOnShutdown = m_HasStartedLifecycle;
            m_HasStartedLifecycle = false;

            try {
                var roots = m_RootScopes.ToArray();

                for (var i = roots.Length - 1; i >= 0; i--) {
                    try {
                        roots[i]?.Dispose();
                    } catch (Exception exception) {
                        cleanupExceptions.Add(exception);
                    }
                }

                var pending = m_PendingScopes.ToArray();

                for (var i = pending.Length - 1; i >= 0; i--) {
                    try {
                        pending[i]?.Dispose();
                    } catch (Exception exception) {
                        cleanupExceptions.Add(exception);
                    }
                }

                if (callOnShutdown) {
                    try {
                        OnShutdown();
                    } catch (Exception exception) {
                        cleanupExceptions.Add(exception);
                    }
                }
            } finally {
                m_RootScopes.Clear();
                m_AllScopes.Clear();
                m_PendingScopes.Clear();
                m_Events.Clear();
                ScopeConfiguring = null;
                m_IsShuttingDown = false;
                m_IsTerminated = true;
            }

            ReportCleanupExceptions(cleanupExceptions);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default) {
            if (m_ShutdownTask != null) {
                return m_ShutdownTask;
            }

            if (m_IsTerminated) {
                return Task.CompletedTask;
            }

            m_ShutdownTask = ShutdownCoreAsync(cancellationToken);
            return m_ShutdownTask;
        }

        private async Task ShutdownCoreAsync(CancellationToken cancellationToken) {
            if (m_IsShuttingDown || m_IsTerminated) {
                return;
            }

            var cleanupExceptions = new List<Exception>();
            m_IsShuttingDown = true;
            IsStarted = false;
            var callOnShutdown = m_HasStartedLifecycle;
            m_HasStartedLifecycle = false;

            try {
                var roots = m_RootScopes.ToArray();

                for (var i = roots.Length - 1; i >= 0; i--) {
                    try {
                        if (roots[i] != null) {
                            await roots[i].DisposeAsync(cancellationToken).ConfigureAwait(true);
                        }
                    } catch (Exception exception) {
                        cleanupExceptions.Add(exception);
                    }
                }

                var pending = m_PendingScopes.ToArray();

                for (var i = pending.Length - 1; i >= 0; i--) {
                    try {
                        if (pending[i] != null) {
                            await pending[i].DisposeAsync(cancellationToken).ConfigureAwait(true);
                        }
                    } catch (Exception exception) {
                        cleanupExceptions.Add(exception);
                    }
                }

                if (callOnShutdown) {
                    try {
                        OnShutdown();
                    } catch (Exception exception) {
                        cleanupExceptions.Add(exception);
                    }
                }
            } finally {
                m_RootScopes.Clear();
                m_AllScopes.Clear();
                m_PendingScopes.Clear();
                m_Events.Clear();
                ScopeConfiguring = null;
                m_IsShuttingDown = false;
                m_IsTerminated = true;
            }

            ReportCleanupExceptions(cleanupExceptions);
        }

        private void ReportCleanupExceptions(List<Exception> cleanupExceptions) {
            if (cleanupExceptions.Count == 0) {
                return;
            }

            ReportUnhandledException(
                cleanupExceptions.Count == 1
                    ? cleanupExceptions[0]
                    : new AggregateException("Architecture cleanup failed.", cleanupExceptions)
            );
        }

        public void Dispose() => Shutdown();
    }
}
