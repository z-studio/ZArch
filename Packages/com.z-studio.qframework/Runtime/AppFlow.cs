using System;
using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    /// <summary>AppFlow 的工作进度；Procedure 依据稳定的 Login/Lobby/Game 状态切换显示。</summary>
    public enum AppState {
        /// <summary>尚未启动，或启动失败后等待重试。</summary>
        Created,

        /// <summary>读取 App 配置、准备基础服务。</summary>
        Starting,

        /// <summary>App 已就绪，等待登录，没有已发布的账号会话。</summary>
        Login,

        /// <summary>认证、注册、初始化并准备账号数据；尚不能进入大厅。</summary>
        Authenticating,

        /// <summary>账号已就绪，可以从大厅选择游戏。</summary>
        Lobby,

        /// <summary>等待游戏切换完成，尚不能把界面当作已经进入游戏。</summary>
        EnteringGame,

        /// <summary>游戏进入完成。</summary>
        Game,

        /// <summary>普通返回大厅，正在等待游戏退出。</summary>
        LeavingGame,

        /// <summary>游戏关闭未完成，可以返回大厅或注销重试。</summary>
        GameCloseFailed,

        /// <summary>注销或关闭 AppFlow，正在等待当前操作和账号清理。</summary>
        LoggingOut,

        /// <summary>账号相关清理未完成，重试原来的 LogoutAsync / ShutdownAsync。</summary>
        LogoutFailed,

        /// <summary>AppFlow 已关闭；根架构和基础组件仍由调用方负责释放。</summary>
        Closed
    }

    /// <summary>
    /// 组合启动、登录、游戏切换和注销。借用 App 根架构，拥有当前账号和它的 GameLauncher。
    /// 阅读顺序：StartAsync → LoginAsync → OpenGameAsync → LogoutAsync；关闭细节从 RequestClose 往下读。
    /// </summary>
    public sealed class AppFlow {
        // App 与业务回调由项目提供；此类不连接具体服务器，也不关闭整个 ZFramework。
        private readonly IArchitecture m_App;
        private readonly Func<Session, GameLauncher> m_CreateGames;
        private readonly Func<Session, Task> m_CloseAccount;
        private readonly SemaphoreSlim m_Gate = new(1, 1);
        private CancellationTokenSource m_Operation;

        private Session m_User;

        // 认证和账号准备全部成功才公开 m_User，防止界面提前使用半初始化的数据。
        private bool m_Authenticated;

        // 异步账号清理成功后记账；重试只继续尚未完成的释放步骤。
        private bool m_AccountClosed;
        private bool m_Started;
        private bool m_CloseRequested;
        private bool m_ShutdownRequested;
        private Task m_CloseTask;

        public AppState State { get; private set; }
        public IArchitecture AppArchitecture => m_App;
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

            BeginOperation(cancellation, AppState.Created);
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
                EndOperation();
            }
        }

        /// <summary>回调完成服务器认证，并向候选会话注册账号 Model/System。回调中不要刷新大厅。</summary>
        public async Task LoginAsync(Func<Session, CancellationToken, Task> authenticateAndConfigure,
            CancellationToken cancellation = default) {
            await LoginAsync(authenticateAndConfigure, null, cancellation);
        }

        /// <summary>先认证并注册服务，再初始化 Session，最后等待账号数据/连接就绪；全部成功才进入大厅。</summary>
        public async Task LoginAsync(Func<Session, CancellationToken, Task> authenticateAndConfigure,
            Func<Session, CancellationToken, Task> prepareAccount, CancellationToken cancellation = default) {
            if (authenticateAndConfigure == null) {
                throw new ArgumentNullException(nameof(authenticateAndConfigure));
            }

            BeginOperation(cancellation, AppState.Login);
            State = AppState.Authenticating;
            m_User = new Session(m_App, "Account");
            m_AccountClosed = false;

            try {
                using var pendingLogin = m_Operation.Token.Register(m_User.CancelLifetime);
                await PrepareLoginSessionAsync(authenticateAndConfigure, prepareAccount, m_Operation.Token);
                Games = m_CreateGames(m_User) ?? throw new InvalidOperationException("No game launcher was created.");
                m_Authenticated = true;

                if (!m_CloseRequested) {
                    State = AppState.Lobby;
                }
            } catch (Exception error) {
                await RollbackLoginAsync(error);

                if (!m_CloseRequested) {
                    State = AppState.Login;
                }

                throw;
            } finally {
                EndOperation();
            }
        }

        public async Task OpenGameAsync(string id, CancellationToken cancellation = default) {
            await OpenGameAsync(id, null, cancellation);
        }

        public async Task OpenGameAsync(string id, object arguments, CancellationToken cancellation = default) {
            BeginOperation(cancellation, AppState.Lobby, AppState.Game);
            State = AppState.EnteringGame;

            try {
                await Games.OpenAsync(id, arguments, m_Operation.Token);

                if (!m_CloseRequested) {
                    State = AppState.Game;
                }
            } catch {
                if (!m_CloseRequested) {
                    RestoreGameState();
                }

                throw;
            } finally {
                EndOperation();
            }
        }

        public async Task ReturnToLobbyAsync(CancellationToken cancellation = default) {
            BeginOperation(cancellation, AppState.Game, AppState.GameCloseFailed);
            State = AppState.LeavingGame;

            try {
                await Games.CloseAsync(m_Operation.Token);

                if (!m_CloseRequested) {
                    State = AppState.Lobby;
                }
            } catch {
                if (!m_CloseRequested) {
                    RestoreGameState();
                }

                throw;
            } finally {
                EndOperation();
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

        // 登录正常路径：认证并注册 → 初始化业务对象 → 等账号连接/数据就绪。
        private async Task PrepareLoginSessionAsync(Func<Session, CancellationToken, Task> authenticateAndConfigure,
            Func<Session, CancellationToken, Task> prepareAccount, CancellationToken cancellation) {
            await authenticateAndConfigure(m_User, cancellation);
            
            // 后端可能忽略取消并迟到返回，不能因此把已经注销的账号再次变成有效登录。
            cancellation.ThrowIfCancellationRequested();
            m_User.Initialize();

            if (prepareAccount != null) {
                await prepareAccount(m_User, cancellation);
                cancellation.ThrowIfCancellationRequested();
            }
        }

        private async Task RollbackLoginAsync(Exception loginError) {
            try {
                await ReleaseAccountAsync();
            } catch (Exception cleanupError) {
                // 清理未完成时禁止再次登录，避免新旧账号同时占用连接或资源。
                State = AppState.LogoutFailed;
                throw new AggregateException(loginError, cleanupError);
            }
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
            var cancelError = CancelActiveWork();
            _ = CompleteCloseAsync(completion, cancelError);
            return m_CloseTask;
        }

        private Exception CancelActiveWork() {
            Exception cancelError = null;

            void Cancel(Action action) {
                try {
                    action();
                } catch (Exception error) {
                    cancelError = cancelError == null ? error : new AggregateException(cancelError, error);
                }
            }

            // 两个取消都要尝试；一个回调出错不能阻止另一个任务收到停止信号。
            Cancel(() => m_User?.CancelLifetime());
            Cancel(() => m_Operation?.Cancel());
            return cancelError;
        }

        private async Task CompleteCloseAsync(TaskCompletionSource<bool> completion, Exception cancelError) {
            try {
                await CloseAccountAfterOperationAsync(cancelError);
                m_CloseRequested = false;
                completion.SetResult(true);
            } catch (Exception error) {
                m_CloseRequested = false;
                completion.SetException(error);
            }
        }

        private async Task CloseAccountAfterOperationAsync(Exception cancelError) {
            // 取消只是请求停止。等登录/进游戏流程真正结束，才能安全释放其正在使用的账号数据。
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
                // 连接关闭可能要等待；成功后再标记，失败时重试仍有原账号可用于收尾。
                await m_CloseAccount(m_User);
                m_AccountClosed = true;
            }

            DisposeAccountSession();
        }

        private void DisposeAccountSession() {
            try {
                m_User.Dispose();
            } finally {
                if (m_User.IsDisposed) {
                    m_User = null;
                    m_Authenticated = false;
                }
            }
        }

        private void RestoreGameState() {
            State = Games.State switch {
                // 失败可能发生在关闭旧游戏或加载新游戏时，按实际保留的对象恢复可重试状态。
                GameState.CloseFailed => AppState.GameCloseFailed,
                GameState.Running => AppState.Game,
                _ => AppState.Lobby
            };
        }

        private void BeginOperation(CancellationToken cancellation, params AppState[] allowed) {
            if (m_ShutdownRequested) {
                throw new ObjectDisposedException(nameof(AppFlow));
            }

            if (m_CloseRequested || Array.IndexOf(allowed, State) < 0) {
                throw new InvalidOperationException($"Cannot perform this operation while App is {State}.");
            }

            cancellation.ThrowIfCancellationRequested();

            // 普通按钮操作立即拒绝重叠；注销由 RequestClose 取消并等待，无需用户排队重试。
            if (!m_Gate.Wait(0)) {
                throw new InvalidOperationException("An App transition is already running.");
            }

            m_Operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        }

        private void EndOperation() {
            m_Operation.Dispose();
            m_Operation = null;
            m_Gate.Release();
        }
    }
}