using System;
using System.Threading;
using System.Threading.Tasks;
using QFramework;
using ZFramework.Unity;

namespace Demo.App {
    /// <summary>业务主线：先看 StartAsync、LoginAsync、OpenGameAsync、LogoutAsync，再按需要阅读下面的步骤。</summary>
    public sealed class AppLogic {
        private readonly IAppBackend m_Backend;
        private readonly ResourceComponent m_Resource;
        private readonly ZFrameworkGamePackages m_Packages;
        private readonly float m_ResourceTimeoutSeconds;
        private Task m_Shutdown;

        public IArchitecture Architecture { get; private set; }
        public AppFlow Flow { get; }
        public GamePackageProgress PackageProgress => m_Packages.Progress;
        public bool IsStopping { get; private set; }

        public AppLogic(ResourceComponent resource, IAppBackend backend, float resourceTimeoutSeconds = 60) {
            Architecture = DemoApp.Interface;
            m_Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            m_Resource = resource;
            m_Packages = new ZFrameworkGamePackages(resource);
            m_ResourceTimeoutSeconds = resourceTimeoutSeconds;
            // 框架在相应时机调用这两个命名方法，不需要在这里编写状态机。
            Flow = new AppFlow(Architecture, CreateGameLauncher, CloseAccountAsync);
        }

        // 先看这五个入口：所有按钮和启动 Procedure 都通过这里执行业务。
        public Task StartAsync() => Flow.StartAsync(PrepareAppAsync);

        public Task LoginAsync(string account, CancellationToken cancellation = default) =>
            Flow.LoginAsync((user, token) => ConfigureAccountAsync(user, account, token), PrepareAccountAsync,
                cancellation);

        public Task OpenGameAsync(string gameId, GameLaunchData data = null,
            CancellationToken cancellation = default) =>
            Flow.OpenGameAsync(gameId, data, cancellation);

        public Task ReturnToLobbyAsync() => Flow.ReturnToLobbyAsync();
        public Task LogoutAsync() => Flow.LogoutAsync();

        // 启动步骤：只读取 App 配置并等待基础组件；游戏包等用户选择后再准备。
        private async Task PrepareAppAsync(CancellationToken cancellation) {
            var settings = await m_Backend.LoadSettingsAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            Architecture.GetModel<ServerSettingsModel>().settings = settings;
            await m_Packages.WaitForReadyAsync(m_ResourceTimeoutSeconds, cancellation);
        }

        // 登录步骤：认证成功后注册当前账号的数据与服务。
        private async Task ConfigureAccountAsync(Session user, string account, CancellationToken cancellation) {
            var data = await m_Backend.LoginAsync(account, cancellation);
            cancellation.ThrowIfCancellationRequested();
            // 钱包属于这次登录的账号，不放进常驻 App，也不放进某个游戏。
            user.RegisterModel(new AccountModel(data));
            user.RegisterModel(new WalletModel(data.coins));
            user.RegisterSystem(new AccountSystem());
        }

        // 服务的 OnInit 已执行，此处才请求账号资料、连接大厅或等待必要数据。
        private Task PrepareAccountAsync(Session user, CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            return user.GetSystem<AccountSystem>().ConnectAsync(cancellation);
        }

        // 登录完成后，游戏会话以账号会话为父级，所以能取得当前账号的钱包。
        private GameLauncher CreateGameLauncher(Session user) {
            var games = Architecture.GetModel<ServerSettingsModel>().settings.games;
            var loader = new ZFrameworkGamePackageLoader(m_Resource, packages: m_Packages);
            return new GameLauncher(user, loader, games);
        }

        private async Task CloseAccountAsync(Session user) {
            var connection = user.GetSystem<AccountSystem>();

            if (connection != null) {
                await connection.DisconnectAsync();
            }

            await m_Backend.CloseAccountAsync(user.GetModel<AccountModel>());
        }

        // App 整体关闭才释放这些资源；普通注销由 Flow 保留 App 基础服务。
        public Task ShutdownAsync() {
            IsStopping = true;

            if (m_Shutdown == null || m_Shutdown.IsFaulted) {
                m_Shutdown = ShutdownCoreAsync();
            }

            return m_Shutdown;
        }

        private async Task ShutdownCoreAsync() {
            await Flow.ShutdownAsync();
            await m_Packages.ShutdownAsync();
            Architecture?.Deinit();
            Architecture = null;
        }
    }
}