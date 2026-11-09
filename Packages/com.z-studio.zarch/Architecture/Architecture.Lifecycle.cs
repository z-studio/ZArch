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
                    $"{nameof(Architecture)} has already shutdown and cannot be restarted. Create a new instance instead.");
            }

            if (m_IsShuttingDown) {
                throw new InvalidOperationException($"{nameof(Architecture)} is shutting down.");
            }

            if (IsStarted) {
                throw new InvalidOperationException($"{nameof(Architecture)} is already started.");
            }

            IsStarted = true;
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
            if (!IsStarted) {
                throw new InvalidOperationException("Call Architecture.Start first.");
            }
        }

        public void Shutdown() {
            if (m_IsShuttingDown || m_IsTerminated) {
                return;
            }

            m_IsShuttingDown = true;

            try {
                foreach (var root in m_RootScopes.ToArray()) {
                    root.PrepareShutdown();
                }
            } catch {
                m_IsShuttingDown = false;
                throw;
            }

            var cleanupExceptions = new List<Exception>();
            m_IsShuttingDown = true;
            IsStarted = false;

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
            if (m_ShutdownTask is { IsFaulted: false, IsCanceled: false }) {
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

            m_IsShuttingDown = true;

            try {
                foreach (var root in m_RootScopes.ToArray()) {
                    await root.PrepareShutdownAsync();
                }
            } catch {
                m_IsShuttingDown = false;
                throw;
            }

            var cleanupExceptions = new List<Exception>();
            m_IsShuttingDown = true;
            IsStarted = false;

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

            ReportUnhandledException(cleanupExceptions.Count == 1
                ? cleanupExceptions[0]
                : new AggregateException("Architecture cleanup failed.", cleanupExceptions));
        }

        public void Dispose() => Shutdown();
    }
}