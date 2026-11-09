using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    /// <summary>单一游戏状态机。会话先退出再释放服务，失败保留资源，Close/Shutdown 可重试。</summary>
    public sealed class GameSwitcher {
        private sealed class Session {
            public IGameDefinition game;
            public ArchitectureScope scope;
            public IGameContent content;
            public bool enterStarted;
            public bool exited;
            public bool unloaded;
        }

        private readonly ArchitectureScope m_Parent;
        private readonly IGameContentLoader m_Loader;
        private readonly Dictionary<string, IGameDefinition> m_Games = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource m_Lifetime = new();
        private Session m_Session;
        private bool m_ShutdownRequested;
        private TaskCompletionSource<bool> m_Transition;
        private Task m_Shutdown;

        public EGameState State { get; private set; } = EGameState.Idle;

        public bool IsBusy =>
            State == EGameState.ShuttingDown || (m_Transition != null && !m_Transition.Task.IsCompleted);

        public string CurrentId => State == EGameState.Running ? m_Session?.game.Id : null;

        public GameSwitcher(ArchitectureScope parent, IGameContentLoader loader, IEnumerable<IGameDefinition> games) {
            m_Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            m_Loader = loader ?? throw new ArgumentNullException(nameof(loader));

            if (games == null) {
                throw new ArgumentNullException(nameof(games));
            }

            foreach (var game in games) {
                if (game == null || string.IsNullOrWhiteSpace(game.Id)) {
                    throw new ArgumentException("Games contains a missing definition or empty ID.", nameof(games));
                }

                if (!m_Games.TryAdd(game.Id, game)) {
                    throw new ArgumentException($"Duplicate game ID '{game.Id}'.", nameof(games));
                }

                m_Loader.Validate(game);
            }

            // 父环境直接关闭时，也会先结束本会话；不要求调用者记住额外的退出顺序。
            parent.RegisterShutdown(ShutdownAsync, ShutdownSynchronously);
        }

        public async Task OpenAsync(string id, object arguments = null, CancellationToken cancellationToken = default) {
            EnsureAvailable();

            if (string.IsNullOrWhiteSpace(id) || !m_Games.TryGetValue(id, out var game)) {
                throw new KeyNotFoundException($"Game '{id}' is not configured.");
            }

            m_Loader.Validate(game);
            cancellationToken.ThrowIfCancellationRequested();

            if (State == EGameState.CloseFailed) {
                throw new InvalidOperationException(
                    "CloseAsync must finish cleaning up the previous game before opening another.");
            }

            BeginTransition(EGameState.Opening);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_Lifetime.Token);

            try {
                await CleanupSessionAsync();
                linked.Token.ThrowIfCancellationRequested();
                var session = new Session { game = game };
                m_Session = session;

                try {
                    session.scope = await m_Parent.CreateChildAsync("Game:" + id, (scope, token) => {
                        token.ThrowIfCancellationRequested();
                        game.Configure(scope);
                        return Task.CompletedTask;
                    }, tag: game, cancellationToken: linked.Token);

                    session.content = await m_Loader.LoadAsync(game, session.scope, linked.Token)
                                      ?? throw new InvalidOperationException("Game loader returned null content.");
                    linked.Token.ThrowIfCancellationRequested();
                    session.enterStarted = true;
                    await session.content.EnterAsync(arguments, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    State = EGameState.Running;
                } catch (Exception enterError) {
                    if (enterError is GameContentLoadException partial) {
                        session.content = partial.Content;
                    }

                    try {
                        await CleanupSessionAsync();
                    } catch (Exception cleanupError) {
                        throw new AggregateException(enterError, cleanupError);
                    }

                    throw;
                }
            } finally {
                if (State != EGameState.Running) {
                    State = m_Session == null ? EGameState.Idle : EGameState.CloseFailed;
                }

                EndTransition();
            }
        }

        public async Task CloseAsync() {
            EnsureAvailable();
            BeginTransition(EGameState.Closing);

            try {
                await CleanupSessionAsync();
            } finally {
                State = m_Session == null ? EGameState.Idle : EGameState.CloseFailed;
                EndTransition();
            }
        }

        public Task ShutdownAsync() {
            if (State == EGameState.Closed) {
                return Task.CompletedTask;
            }

            if (m_Shutdown != null && !m_Shutdown.IsCompleted) {
                return m_Shutdown;
            }

            m_ShutdownRequested = true;
            m_Lifetime.Cancel();
            return m_Shutdown = ShutdownCoreAsync();
        }

        private async Task ShutdownCoreAsync() {
            try {
                if (IsBusy) {
                    await m_Transition.Task;
                }

                State = EGameState.ShuttingDown;
                await CleanupSessionAsync();
                State = EGameState.Closed;
                m_Lifetime.Dispose();
            } catch {
                State = EGameState.ShutdownFailed;
                throw;
            }
        }

        private void ShutdownSynchronously() {
            if (State == EGameState.Closed) {
                return;
            }

            if (IsBusy || m_Session != null) {
                throw new InvalidOperationException("An active game requires await Architecture.ShutdownAsync().");
            }

            m_ShutdownRequested = true;
            State = EGameState.Closed;
            m_Lifetime.Cancel();
            m_Lifetime.Dispose();
        }

        private async Task CleanupSessionAsync() {
            var session = m_Session;
            if (session == null) {
                return;
            }

            if (!session.exited) {
                if (session.enterStarted && session.content != null) {
                    await session.content.ExitAsync();
                }

                session.exited = true;
            }

            if (!session.unloaded) {
                if (session.content != null) {
                    await session.content.UnloadAsync();
                }

                session.unloaded = true;
            }

            if (session.scope != null) {
                await session.scope.DisposeAsync();
            }

            m_Session = null;
        }

        private void EnsureAvailable() {
            if (m_ShutdownRequested) {
                throw new ObjectDisposedException(nameof(GameSwitcher));
            }

            if (IsBusy) {
                throw new InvalidOperationException("A game transition is already in progress.");
            }
        }

        private void BeginTransition(EGameState state) {
            m_Transition = new TaskCompletionSource<bool>();
            State = state;
        }

        private void EndTransition() => m_Transition.TrySetResult(true);
    }
}