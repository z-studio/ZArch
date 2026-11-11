using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    /// <summary>
    /// 管理一次游戏进入与退出，最多运行一个游戏。
    /// 阅读顺序：OpenAsync → OpenNewGameAsync → CloseCurrentAsync；最后再看 ShutdownAsync 的取消与等待。
    /// </summary>
    public sealed class GameLauncher {
        // 借用父架构和加载器。关闭游戏不会销毁账号，也不会关闭整个资源系统。
        private readonly IArchitecture m_Parent;
        private readonly IGamePackageLoader m_Loader;
        private readonly Dictionary<string, GameInfo> m_Games = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim m_Gate = new(1, 1);

        private readonly CancellationTokenSource m_Lifetime = new();

        // 这三个对象属于本次进入：先退出游戏对象，再释放会话，最后归还资源引用。
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

                // 留下目录快照，避免外部修改配置对象时改变正在使用的入口信息。
                m_Games.Add(game.id,
                    new GameInfo { id = game.id, packageName = game.packageName, entryAddress = game.entryAddress });
            }
        }

        public async Task OpenAsync(string id, CancellationToken cancellation = default) {
            await OpenAsync(id, null, cancellation);
        }

        /// <summary>先完整关闭旧游戏，再加载并进入新游戏。进入失败会回滚本次创建的对象。</summary>
        public async Task OpenAsync(string id, object arguments, CancellationToken cancellation = default) {
            if (!m_Games.TryGetValue(id, out var info)) {
                throw new KeyNotFoundException($"Unknown game '{id}'.");
            }

            BeginOperation();

            try {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, m_Lifetime.Token);
                linked.Token.ThrowIfCancellationRequested();

                // 关闭旧游戏是独立步骤。若它失败，保留旧对象供重试，不开始新游戏。
                await CloseCurrentAsync(CancellationToken.None);
                await OpenNewGameAsync(id, info, arguments, linked.Token);
            } finally {
                m_Gate.Release();
            }
        }

        /// <summary>普通返回大厅，只关闭游戏；账号仍由父级 AppFlow 持有。</summary>
        public async Task CloseAsync(CancellationToken cancellation = default) {
            BeginOperation();

            try {
                await CloseCurrentAsync(cancellation);
            } finally {
                m_Gate.Release();
            }
        }

        /// <summary>注销前调用：先取消正在加载的游戏，等当前操作退出，再完成清理。失败可重试。</summary>
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
                // 取消回调出错也继续清理，不能留下仍占用账号和资源的游戏。
                cancelError = error;
            }

            // 取消只是发出停止请求；加载器返回前仍可能持有资源，必须等它完成或回滚。
            await m_Gate.WaitAsync();

            try {
                // 此时已经决定关闭，清理不用已取消的令牌，否则会刚开始就中断。
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

        // 新游戏的正常路径：加载资源 → 配置会话 → 创建对象 → 等业务进入完成。
        private async Task OpenNewGameAsync(string id, GameInfo info, object arguments,
            CancellationToken cancellation) {
            State = GameState.Loading;
            CurrentGameId = id;

            try {
                m_Package = await m_Loader.LoadAsync(info, cancellation)
                            ?? throw new InvalidOperationException("Loader returned no package.");
                cancellation.ThrowIfCancellationRequested();
                CreateGameSession(id, arguments);
                m_Game = m_Package.CreateGame() ?? throw new InvalidOperationException("Package returned no game.");
                await m_Game.EnterAsync(CurrentSession, cancellation);
                cancellation.ThrowIfCancellationRequested();
                State = GameState.Running;
            } catch (Exception error) {
                await RollbackOpenAsync(error);
                throw;
            }
        }

        private void CreateGameSession(string id, object arguments) {
            // 先注册完所有对象，再初始化；System.OnInit 才能找到本游戏的完整依赖。
            CurrentSession = new Session(m_Parent, id);
            CurrentSession.RegisterUtility(new GameArguments(arguments));
            m_Package.Configure(CurrentSession);
            CurrentSession.Initialize();
        }

        private async Task RollbackOpenAsync(Exception openError) {
            try {
                // 进入可能只做了一半，退出路径必须能处理部分对象，并完整归还资源。
                await CloseCurrentAsync(CancellationToken.None);
            } catch (Exception cleanupError) {
                // 同时保留进入错误和清理错误，不能让后一个错误遮住最初原因。
                throw new AggregateException(openError, cleanupError);
            }
        }

        // 退出的正常路径：停止任务 → 退出表现对象 → 释放业务会话 → 归还资源。
        private async Task CloseCurrentAsync(CancellationToken cancellation) {
            State = GameState.Closing;

            try {
                CurrentSession?.CancelLifetime();
                await ExitGameAsync(cancellation);
                var disposeError = DisposeGameSession();
                await UnloadGamePackageAsync(disposeError);
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

        private async Task ExitGameAsync(CancellationToken cancellation) {
            if (m_Game != null) {
                // 退出期间仍可能访问游戏和账号数据，所以业务会话必须留到它完成之后。
                await m_Game.ExitAsync(cancellation);
                m_Game = null;
            }
            // 失败时不清空 m_Game，下一次 Close/Shutdown 继续从未完成的步骤重试。
        }

        private Exception DisposeGameSession() {
            Exception disposeError = null;

            if (CurrentSession != null) {
                try {
                    CurrentSession.Dispose();
                } catch (Exception error) {
                    // Session 会尝试清理所有本地对象；即使报告错误，也继续归还资源引用。
                    disposeError = error;
                }

                CurrentSession = null;
            }

            return disposeError;
        }

        private async Task UnloadGamePackageAsync(Exception disposeError) {
            try {
                if (m_Package != null) {
                    // Model/System 已经释放，现在才归还它们可能持有的配置和其他资源。
                    await m_Package.UnloadAsync();
                    m_Package = null;
                }
            } catch (Exception unloadError) {
                // 失败时保留 m_Package；重试不会重复执行已经成功的游戏退出步骤。
                if (disposeError != null) {
                    throw new AggregateException(disposeError, unloadError);
                }

                throw;
            }
        }

        private void BeginOperation() {
            if (m_ShutdownRequested) {
                throw new ObjectDisposedException(nameof(GameLauncher));
            }

            // 普通操作不能重叠；Shutdown 是唯一会先取消、再等待当前操作的关闭入口。
            if (!m_Gate.Wait(0)) {
                throw new InvalidOperationException("A game transition is already in progress.");
            }
        }
    }
}