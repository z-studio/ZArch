using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    /// <summary>一次运行一个游戏。App 只依赖这些接口，不引用任何具体游戏类型。</summary>
    public sealed class GameLauncher {
        private readonly IArchitecture m_Parent;
        private readonly IGamePackageLoader m_Loader;
        private readonly Dictionary<string, GameInfo> m_Games = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim m_Gate = new(1, 1);
        private readonly CancellationTokenSource m_Lifetime = new();
        private IGamePackage m_Package;
        private IGame m_Game;
        private bool m_ShutdownRequested;

        public GameState State { get; private set; }
        public string CurrentGameId { get; private set; }
        public Session CurrentSession { get; private set; }

        public GameLauncher(IArchitecture parent, IGamePackageLoader loader, IEnumerable<GameInfo> games) {
            m_Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            m_Loader = loader ?? throw new ArgumentNullException(nameof(loader));

            if (games == null) {
                throw new ArgumentNullException(nameof(games));
            }

            foreach (var game in games) {
                if (game == null || string.IsNullOrWhiteSpace(game.id)
                                 || string.IsNullOrWhiteSpace(game.entryAddress)) {
                    throw new ArgumentException("Each game needs a unique Id and EntryAddress.", nameof(games));
                }

                m_Games.Add(game.id,
                    new GameInfo { id = game.id, entryAddress = game.entryAddress });
            }
        }

        public async Task OpenAsync(string id, CancellationToken cancellation = default) {
            if (!m_Games.TryGetValue(id, out var info)) {
                throw new KeyNotFoundException($"Unknown game '{id}'.");
            }

            EnterOperation();

            try {
                if (State == GameState.Running && CurrentGameId == id) {
                    return;
                }

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, m_Lifetime.Token);
                linked.Token.ThrowIfCancellationRequested();
                await CloseCurrentAsync(CancellationToken.None);
                State = GameState.Loading;
                CurrentGameId = id;

                try {
                    m_Package = await m_Loader.LoadAsync(info, linked.Token)
                                ?? throw new InvalidOperationException("Loader returned no package.");
                    linked.Token.ThrowIfCancellationRequested();
                    CurrentSession = new Session(m_Parent, id);
                    m_Package.Configure(CurrentSession);
                    CurrentSession.Initialize();
                    m_Game = m_Package.CreateGame() ?? throw new InvalidOperationException("Package returned no game.");
                    await m_Game.EnterAsync(CurrentSession, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    State = GameState.Running;
                } catch (Exception error) {
                    try {
                        await CloseCurrentAsync(CancellationToken.None);
                    } catch (Exception cleanup) {
                        throw new AggregateException(error, cleanup);
                    }

                    throw;
                }
            } finally {
                m_Gate.Release();
            }
        }

        public async Task CloseAsync(CancellationToken cancellation = default) {
            EnterOperation();

            try {
                await CloseCurrentAsync(cancellation);
            } finally {
                m_Gate.Release();
            }
        }

        // 释放所属账号会话前必须先 await 此方法。
        // 若关闭失败，保留资源及服务，允许再次调用重试。
        public async Task ShutdownAsync() {
            if (State == GameState.Closed) {
                return;
            }

            m_ShutdownRequested = true;
            Exception cancelError = null;

            try {
                if (!m_Lifetime.IsCancellationRequested) {
                    m_Lifetime.Cancel();
                }
            } catch (Exception error) {
                cancelError = error;
            }

            await m_Gate.WaitAsync();

            try {
                await CloseCurrentAsync(CancellationToken.None);
                State = GameState.Closed;
                m_Lifetime.Dispose();

                if (cancelError != null) {
                    throw cancelError;
                }
            } finally {
                m_Gate.Release();
            }
        }

        private void EnterOperation() {
            if (m_ShutdownRequested) {
                throw new ObjectDisposedException(nameof(GameLauncher));
            }

            if (!m_Gate.Wait(0)) {
                throw new InvalidOperationException("A game transition is already in progress.");
            }
        }

        private async Task CloseCurrentAsync(CancellationToken cancellation) {
            State = GameState.Closing;

            try {
                CurrentSession?.CancelLifetime();

                if (m_Game != null) {
                    await m_Game.ExitAsync(cancellation);
                    m_Game = null;
                }

                Exception disposeError = null;

                if (CurrentSession != null) {
                    try {
                        CurrentSession.Dispose();
                    } catch (Exception error) {
                        disposeError = error;
                    }

                    CurrentSession = null;
                }

                try {
                    if (m_Package != null) {
                        await m_Package.UnloadAsync();
                        m_Package = null;
                    }
                } catch (Exception unloadError) {
                    if (disposeError != null) {
                        throw new AggregateException(disposeError, unloadError);
                    }

                    throw;
                }

                CurrentGameId = null;
                State = GameState.Idle;

                if (disposeError != null) {
                    throw disposeError;
                }
            } catch {
                State = GameState.CloseFailed;
                throw;
            }
        }
    }
}