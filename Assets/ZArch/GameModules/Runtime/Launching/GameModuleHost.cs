using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch.GameModules {
    public sealed class GameModuleHost : IGameModuleHost, IAsyncDeinitializable, IDeinitializable {
        private readonly GameScopeFactory m_ScopeFactory;
        private readonly IGameContentLoader m_ContentLoader;
        private readonly Dictionary<string, IGameModule> m_Modules = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource m_LifetimeCts = new();
        private TaskCompletionSource<bool> m_TransitionCompletion;
        private Task m_ShutdownTask;
        private GameModuleSession m_PendingCleanup;
        private bool m_IsShuttingDown;
        private bool m_IsDisposed;

        public GameModuleSession Current { get; private set; }
        public bool IsTransitioning { get; private set; }
        private bool HasPendingCleanup => m_PendingCleanup != null;
        public IReadOnlyCollection<IGameModule> Modules { get; }

        public GameModuleHost(
            ArchitectureScope parentScope,
            IGameContentLoader contentLoader,
            IEnumerable<IGameModule> modules
        ) {
            m_ScopeFactory = new GameScopeFactory(
                parentScope ?? throw new ArgumentNullException(nameof(parentScope))
            );
            m_ContentLoader = contentLoader ?? throw new ArgumentNullException(nameof(contentLoader));

            if (modules == null) {
                throw new ArgumentNullException(nameof(modules));
            }

            foreach (var module in modules) {
                if (module == null) {
                    throw new ArgumentException("Game modules contain null.", nameof(modules));
                }

                if (string.IsNullOrWhiteSpace(module.Id)) {
                    throw new ArgumentException("A game module has an empty ID.", nameof(modules));
                }

                if (!m_Modules.TryAdd(module.Id, module)) {
                    throw new ArgumentException($"Duplicate game module ID '{module.Id}'.", nameof(modules));
                }
            }

            Modules = m_Modules.Values;
        }

        public bool TryGetModule(string gameId, out IGameModule module) {
            if (string.IsNullOrWhiteSpace(gameId)) {
                module = null;
                return false;
            }

            return m_Modules.TryGetValue(gameId, out module);
        }

        public async Task<GameModuleSession> EnterAsync(
            string gameId,
            GameEnterContext context = null,
            CancellationToken cancellationToken = default
        ) {
            EnsureUsable();

            if (HasPendingCleanup) {
                throw new InvalidOperationException(
                    $"Game module '{m_PendingCleanup.Module.Id}' still has content pending cleanup. "
                    + "Call ExitAsync to retry cleanup before entering another game."
                );
            }

            if (!TryGetModule(gameId, out var module)) {
                throw new KeyNotFoundException($"Game module '{gameId}' is not registered.");
            }

            if (Current != null) {
                throw new InvalidOperationException(
                    $"Game module '{Current.Module.Id}' is already active. Call ExitAsync before entering another game."
                );
            }

            BeginTransition();
            context ??= GameEnterContext.Empty;
            GameModuleSession entering = null;
            CancellationTokenSource linkedCts = null;

            try {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    m_LifetimeCts.Token
                );

                var transitionToken = linkedCts.Token;
                transitionToken.ThrowIfCancellationRequested();

                var creation = await m_ScopeFactory.CreateAsync(module, context, transitionToken).ConfigureAwait(true);
                var scope = creation.Scope;

                entering = new GameModuleSession(module, context, scope, creation.Runtime);

                entering.Content = await m_ContentLoader.LoadAsync(module, scope, context, transitionToken)
                                                        .ConfigureAwait(true);

                if (entering.Content == null) {
                    throw new InvalidOperationException($"Content loader returned null for game module '{module.Id}'.");
                }

                entering.IsRuntimeExited = false;
                var enterTask = entering.Runtime.EnterAsync(transitionToken)
                                ?? throw new InvalidOperationException(
                                    $"{entering.Runtime.GetType().FullName}.EnterAsync returned null."
                                );
                await enterTask.ConfigureAwait(true);

                transitionToken.ThrowIfCancellationRequested();
                Current = entering;
                return entering;
            } catch (Exception enterException) {
                if (entering != null) {
                    var cleanupException = await CleanupAndTrackAsync(entering).ConfigureAwait(true);

                    if (cleanupException != null) {
                        throw new AggregateException(
                            $"Entering game module '{module.Id}' failed, and rolling back the session also failed.",
                            enterException,
                            cleanupException
                        );
                    }
                }

                throw;
            } finally {
                linkedCts?.Dispose();
                EndTransition();
            }
        }

        public async Task ExitAsync() {
            EnsureUsable();
            BeginTransition();

            try {
                var session = Current ?? m_PendingCleanup;

                if (session == null) {
                    return;
                }

                Current = null;

                var cleanupException = await CleanupAndTrackAsync(session).ConfigureAwait(true);

                if (cleanupException != null) {
                    ExceptionDispatchInfo.Capture(cleanupException).Throw();
                }
            } finally {
                EndTransition();
            }
        }

        public Task ShutdownAsync() {
            if (m_ShutdownTask != null) {
                return m_ShutdownTask;
            }

            m_ShutdownTask = ShutdownCoreAsync();
            return m_ShutdownTask;
        }

        public Task DeinitializeAsync(CancellationToken cancellationToken) => ShutdownAsync();

        private async Task ShutdownCoreAsync() {
            if (m_IsDisposed) {
                return;
            }

            m_IsShuttingDown = true;
            m_LifetimeCts.Cancel();

            try {
                var transition = m_TransitionCompletion?.Task;

                if (transition != null) {
                    await transition.ConfigureAwait(true);
                }

                var session = Current ?? m_PendingCleanup;
                Current = null;

                if (session == null) {
                    return;
                }

                var cleanupException = await CleanupAndTrackAsync(session).ConfigureAwait(true);

                if (cleanupException != null) {
                    ExceptionDispatchInfo.Capture(cleanupException).Throw();
                }
            } finally {
                m_IsDisposed = true;
                IsTransitioning = false;
                m_LifetimeCts.Dispose();
            }
        }

        private async Task<Exception> CleanupAndTrackAsync(GameModuleSession session) {
            m_PendingCleanup = session;
            var cleanupException = await CleanupAsync(session).ConfigureAwait(true);

            if (session.IsCleanedUp && ReferenceEquals(m_PendingCleanup, session)) {
                m_PendingCleanup = null;
            }

            return cleanupException;
        }

        private async Task<Exception> CleanupAsync(GameModuleSession session) {
            if (session.IsCleanedUp) {
                return null;
            }

            Exception cleanupException = null;

            if (!session.IsRuntimeExited) {
                try {
                    var exitTask = session.Runtime.ExitAsync()
                                   ?? throw new InvalidOperationException(
                                       $"{session.Runtime.GetType().FullName}.ExitAsync returned null."
                                   );
                    await exitTask.ConfigureAwait(true);
                    session.IsRuntimeExited = true;
                } catch (Exception exception) {
                    // Keep content and scope alive so runtime exit can be retried safely.
                    return exception;
                }
            }

            if (!session.IsContentUnloaded) {
                if (session.Content == null) {
                    session.IsContentUnloaded = true;
                } else {
                    try {
                        await m_ContentLoader.UnloadAsync(session.Content).ConfigureAwait(true);
                        session.Content = null;
                        session.IsContentUnloaded = true;
                    } catch (Exception exception) {
                        cleanupException = exception;
                    }
                }
            }

            if (!session.IsScopeDisposed) {
                try {
                    await session.Scope.DisposeAsync(CancellationToken.None).ConfigureAwait(true);
                } catch (Exception exception) {
                    cleanupException = cleanupException == null
                        ? exception
                        : new AggregateException(cleanupException, exception);
                } finally {
                    session.IsScopeDisposed = session.Scope.IsDisposed;
                }
            }

            return cleanupException;
        }

        private void BeginTransition() {
            if (IsTransitioning) {
                throw new InvalidOperationException("A game transition is already in progress.");
            }

            IsTransitioning = true;
            m_TransitionCompletion = new TaskCompletionSource<bool>();
        }

        private void EndTransition() {
            IsTransitioning = false;
            var completion = m_TransitionCompletion;
            m_TransitionCompletion = null;
            completion?.TrySetResult(true);
        }

        private void EnsureUsable() {
            if (m_IsDisposed || m_IsShuttingDown) {
                throw new ObjectDisposedException(nameof(GameModuleHost));
            }
        }

        public void Deinitialize() {
            if (m_IsDisposed) {
                return;
            }

            if (Current != null || HasPendingCleanup || IsTransitioning) {
                throw new InvalidOperationException(
                    "GameModuleHost has active or pending asynchronous content. "
                    + "Await ShutdownAsync or Architecture.ShutdownAsync before synchronous disposal."
                );
            }

            m_IsShuttingDown = true;
            m_IsDisposed = true;
            IsTransitioning = false;
            m_LifetimeCts.Cancel();
            m_LifetimeCts.Dispose();

        }
    }
}
