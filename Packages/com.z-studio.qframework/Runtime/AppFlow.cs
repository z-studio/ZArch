using System;
using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    public enum AppState {
        Created,
        Starting,
        Login,
        Authenticating,
        Lobby,
        EnteringGame,
        Game,
        LeavingGame,
        GameCloseFailed,
        LoggingOut,
        LogoutFailed,
        Closed
    }

    /// <summary>App 常驻；账号会话只在登录期间存在，游戏会话是账号会话的子级。</summary>
    public sealed class AppFlow {
        private readonly IArchitecture m_App;
        private readonly Func<Session, GameLauncher> m_CreateGames;
        private readonly Func<Session, Task> m_CloseAccount;
        private readonly SemaphoreSlim m_Gate = new(1, 1);
        private CancellationTokenSource m_Operation;
        private Session m_User;
        private bool m_Authenticated;
        private bool m_AccountClosed;
        private bool m_Started;
        private bool m_CloseRequested;
        private bool m_ShutdownRequested;
        private Task m_CloseTask;

        public AppState State { get; private set; }
        public Session UserSession => m_Authenticated ? m_User : null;
        public GameLauncher Games { get; private set; }

        public AppFlow(IArchitecture app, Func<Session, GameLauncher> createGames,
            Func<Session, Task> closeAccount = null) {
            m_App = app ?? throw new ArgumentNullException(nameof(app));
            m_CreateGames = createGames ?? throw new ArgumentNullException(nameof(createGames));
            m_CloseAccount = closeAccount ?? (_ => Task.CompletedTask);
        }

        /// <summary>加载服务器配置、准备基础服务；成功后显示登录界面。失败可重新调用。</summary>
        public async Task StartAsync(Func<CancellationToken, Task> prepare, CancellationToken cancellation = default) {
            if (prepare == null) {
                throw new ArgumentNullException(nameof(prepare));
            }

            Begin(cancellation, AppState.Created);
            State = AppState.Starting;

            try {
                await prepare(m_Operation.Token);
                m_Operation.Token.ThrowIfCancellationRequested();
                m_Started = true;

                if (!m_CloseRequested) {
                    State = AppState.Login;
                }
            } catch {
                if (!m_CloseRequested) {
                    State = AppState.Created;
                }

                throw;
            } finally {
                End();
            }
        }

        /// <summary>回调完成服务器认证，并向候选会话注册账号 Model/System。回调中不要刷新大厅。</summary>
        public async Task LoginAsync(Func<Session, CancellationToken, Task> authenticateAndConfigure,
            CancellationToken cancellation = default) {
            if (authenticateAndConfigure == null) {
                throw new ArgumentNullException(nameof(authenticateAndConfigure));
            }

            Begin(cancellation, AppState.Login);
            State = AppState.Authenticating;
            m_User = new Session(m_App, "Account");
            m_AccountClosed = false;

            try {
                using var pendingLogin = m_Operation.Token.Register(m_User.CancelLifetime);
                await authenticateAndConfigure(m_User, m_Operation.Token);

                // 即使服务器忽略取消并迟到返回，也不能发布已经注销的账号。
                m_Operation.Token.ThrowIfCancellationRequested();
                m_User.Initialize();
                Games = m_CreateGames(m_User) ?? throw new InvalidOperationException("No game launcher was created.");
                m_Authenticated = true;

                if (!m_CloseRequested) {
                    State = AppState.Lobby;
                }
            } catch (Exception error) {
                try {
                    await ReleaseAccountAsync();
                } catch (Exception cleanup) {
                    State = AppState.LogoutFailed;
                    throw new AggregateException(error, cleanup);
                }

                if (!m_CloseRequested) {
                    State = AppState.Login;
                }

                throw;
            } finally {
                End();
            }
        }

        public async Task OpenGameAsync(string id, CancellationToken cancellation = default) {
            Begin(cancellation, AppState.Lobby, AppState.Game);
            State = AppState.EnteringGame;

            try {
                await Games.OpenAsync(id, m_Operation.Token);

                if (!m_CloseRequested) {
                    State = AppState.Game;
                }
            } catch {
                if (!m_CloseRequested) {
                    SetGameState();
                }

                throw;
            } finally {
                End();
            }
        }

        public async Task ReturnToLobbyAsync(CancellationToken cancellation = default) {
            Begin(cancellation, AppState.Game, AppState.GameCloseFailed);
            State = AppState.LeavingGame;

            try {
                await Games.CloseAsync(m_Operation.Token);

                if (!m_CloseRequested) {
                    State = AppState.Lobby;
                }
            } catch {
                if (!m_CloseRequested) {
                    SetGameState();
                }

                throw;
            } finally {
                End();
            }
        }

        /// <summary>注销可以打断认证/进入游戏；完成清理后才能再次登录。清理失败可重试。</summary>
        public Task LogoutAsync() {
            if (!m_Started) {
                throw new InvalidOperationException("App has not started.");
            }

            if (m_ShutdownRequested) {
                throw new ObjectDisposedException(nameof(AppFlow));
            }

            if (State == AppState.Login && m_User == null) {
                return Task.CompletedTask;
            }

            return RequestClose();
        }

        /// <summary>只关闭本流程持有的会话，不销毁调用方拥有的 App 架构和基础组件。</summary>
        public Task ShutdownAsync() {
            if (State == AppState.Closed) {
                return Task.CompletedTask;
            }

            m_ShutdownRequested = true;
            return RequestClose();
        }

        private Task RequestClose() {
            if (m_CloseTask is { IsCompleted: false }) {
                return m_CloseTask;
            }

            // 先发布同一关闭任务；取消回调可能同步重入 LogoutAsync。
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_CloseTask = completion.Task;
            m_CloseRequested = true;
            State = AppState.LoggingOut;
            Exception cancelError = null;

            void Cancel(Action action) {
                try {
                    action();
                } catch (Exception error) {
                    cancelError = cancelError == null ? error : new AggregateException(cancelError, error);
                }
            }

            Cancel(() => m_User?.CancelLifetime());
            Cancel(() => m_Operation?.Cancel());
            _ = CompleteCloseAsync(completion, cancelError);
            return m_CloseTask;
        }

        private async Task CompleteCloseAsync(TaskCompletionSource<bool> completion, Exception cancelError) {
            try {
                await CloseAsync(cancelError);
                m_CloseRequested = false;
                completion.SetResult(true);
            } catch (Exception error) {
                m_CloseRequested = false;
                completion.SetException(error);
            }
        }

        private async Task CloseAsync(Exception cancelError) {
            await m_Gate.WaitAsync();

            try {
                await ReleaseAccountAsync();
                State = m_ShutdownRequested ? AppState.Closed : AppState.Login;

                if (cancelError != null) {
                    throw cancelError;
                }
            } catch {
                State = AppState.LogoutFailed;
                throw;
            } finally {
                m_Gate.Release();
            }
        }

        private async Task ReleaseAccountAsync() {
            // 游戏退出回调仍然可以访问账号数据。必须先完整关闭游戏，再关闭账号。
            if (Games != null) {
                await Games.ShutdownAsync();
                Games = null;
            }

            if (m_User == null) {
                return;
            }

            if (m_User.IsDisposed) {
                m_User = null;
                m_Authenticated = false;
                return;
            }

            m_User.CancelLifetime();

            if (!m_AccountClosed) {
                await m_CloseAccount(m_User);
                m_AccountClosed = true;
            }

            try {
                m_User.Dispose();
            } finally {
                if (m_User.IsDisposed) {
                    m_User = null;
                    m_Authenticated = false;
                }
            }
        }

        private void SetGameState() {
            State = Games.State == GameState.CloseFailed
                ? AppState.GameCloseFailed
                : Games.State == GameState.Running
                    ? AppState.Game
                    : AppState.Lobby;
        }

        private void Begin(CancellationToken cancellation, params AppState[] allowed) {
            if (m_ShutdownRequested) {
                throw new ObjectDisposedException(nameof(AppFlow));
            }

            if (m_CloseRequested || Array.IndexOf(allowed, State) < 0) {
                throw new InvalidOperationException($"Cannot perform this operation while App is {State}.");
            }

            cancellation.ThrowIfCancellationRequested();

            if (!m_Gate.Wait(0)) {
                throw new InvalidOperationException("An App transition is already running.");
            }

            m_Operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        }

        private void End() {
            m_Operation.Dispose();
            m_Operation = null;
            m_Gate.Release();
        }
    }
}