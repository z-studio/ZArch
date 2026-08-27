using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch.GameModules {
    public sealed class GameLauncher : IGameLauncher, IDeinitializable {
        private readonly IGameScopeFactory m_ScopeFactory;
        private readonly IGameContentLoader m_ContentLoader;
        private readonly Dictionary<string, IGameModule> m_Modules = new(StringComparer.Ordinal);
        private bool m_IsDisposed;

        public GameSession Current { get; private set; }
        public bool IsTransitioning { get; private set; }
        public IReadOnlyCollection<IGameModule> Modules { get; }

        public GameLauncher(
            IGameScopeFactory scopeFactory,
            IGameContentLoader contentLoader,
            IEnumerable<IGameModule> modules
        ) {
            m_ScopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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

        public async Task<GameSession> EnterAsync(
            string gameId,
            GameLaunchContext context = null,
            CancellationToken cancellationToken = default
        ) {
            EnsureUsable();

            if (!TryGetModule(gameId, out var module)) {
                throw new KeyNotFoundException($"Game module '{gameId}' is not registered.");
            }

            BeginTransition();
            context ??= GameLaunchContext.Empty;
            GameSession entering = null;

            try {
                cancellationToken.ThrowIfCancellationRequested();

                var scope = await m_ScopeFactory
                                  .CreateAsync(module, context, cancellationToken)
                                  .ConfigureAwait(true);

                entering = new GameSession(module, context, scope);

                entering.Content = await m_ContentLoader
                                         .LoadAsync(module, scope, context, cancellationToken)
                                         .ConfigureAwait(true);

                if (entering.Content == null) {
                    throw new InvalidOperationException($"Content loader returned null for game module '{module.Id}'.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                entering.State = EGameSessionState.Active;

                var previous = Current;

                if (previous != null) {
                    var cleanupException = await CleanupAsync(previous, EGameSessionState.Disposed).ConfigureAwait(true);

                    if (cleanupException != null) {
                        previous.Scope.Architecture.ReportException(cleanupException);
                    }
                }

                Current = entering;
                return entering;
            } catch {
                if (entering != null) {
                    var cleanupException = await CleanupAsync(entering, EGameSessionState.Faulted).ConfigureAwait(true);

                    if (cleanupException != null) {
                        entering.Scope.Architecture.ReportException(cleanupException);
                    }
                }

                throw;
            } finally {
                IsTransitioning = false;
            }
        }

        public async Task ExitAsync() {
            EnsureUsable();
            BeginTransition();

            try {
                var session = Current;

                if (session == null) {
                    return;
                }

                Current = null;

                var cleanupException = await CleanupAsync(session, EGameSessionState.Disposed).ConfigureAwait(true);

                if (cleanupException != null) {
                    ExceptionDispatchInfo.Capture(cleanupException).Throw();
                }
            } finally {
                IsTransitioning = false;
            }
        }

        private async Task<Exception> CleanupAsync(GameSession session, EGameSessionState finalState) {
            if (session.State is EGameSessionState.Disposed or EGameSessionState.Faulted) {
                return null;
            }

            session.State = EGameSessionState.Exiting;
            Exception cleanupException = null;

            if (session.Content != null) {
                try {
                    await m_ContentLoader
                          .UnloadAsync(session.Content, CancellationToken.None)
                          .ConfigureAwait(true);
                } catch (Exception exception) {
                    cleanupException = exception;
                }
            }

            try {
                session.Scope.Dispose();
            } catch (Exception exception) {
                cleanupException = cleanupException == null
                    ? exception
                    : new AggregateException(cleanupException, exception);
            }

            session.State = finalState;
            return cleanupException;
        }

        private void BeginTransition() {
            if (IsTransitioning) {
                throw new InvalidOperationException("A game transition is already in progress.");
            }

            IsTransitioning = true;
        }

        private void EnsureUsable() {
            if (m_IsDisposed) {
                throw new ObjectDisposedException(nameof(GameLauncher));
            }
        }

        public void Deinitialize() {
            if (m_IsDisposed) {
                return;
            }

            m_IsDisposed = true;
            IsTransitioning = false;

            if (Current != null) {
                Current.Scope.Dispose();
                Current.State = EGameSessionState.Disposed;
                Current = null;
            }
        }
    }
}