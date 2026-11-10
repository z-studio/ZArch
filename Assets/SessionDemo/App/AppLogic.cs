using System;
using System.Threading;
using System.Threading.Tasks;
using QFramework;
using ZFramework.Unity;

namespace Demo.App {
    /// <summary>先读这个文件：它只描述启动、登录和账号数据，不处理界面显示。</summary>
    public sealed class AppLogic {
        private readonly IAppBackend m_Backend;
        private readonly ResourceComponent m_Resource;
        private readonly AppResources m_Resources;
        private Task m_Shutdown;

        public IArchitecture Architecture { get; private set; }
        public AppFlow Flow { get; }
        public GamePackageProgress PackageProgress => m_Resources.Packages.Progress;
        public bool IsStopping { get; private set; }

        public AppLogic(ResourceComponent resource, IAppBackend backend, float resourceTimeoutSeconds = 60) {
            Architecture = DemoApp.Interface;
            m_Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            m_Resource = resource;
            m_Resources = new AppResources(resource, resourceTimeoutSeconds);
            // 框架在相应时机调用这两个命名方法，不需要在这里编写状态机。
            Flow = new AppFlow(Architecture, CreateGameLauncher, CloseAccountAsync);
        }

        // 1. 启动：读取服务器配置，等待基础资源组件就绪。完成后框架停在登录界面。
        public Task StartAsync() => Flow.StartAsync(PrepareAppAsync);
        private async Task PrepareAppAsync(CancellationToken cancellation) {
            var settings = await m_Backend.LoadSettingsAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            Architecture.GetModel<ServerSettingsModel>().settings = settings;
            await m_Resources.PrepareAsync(cancellation);
        }

        // 2. 登录：认证和配置会话由 Flow 包裹，注销时才能统一取消、清理。
        public Task LoginAsync(string account, CancellationToken cancellation = default) {
            return Flow.LoginAsync((user, token) => ConfigureAccountAsync(user, account, token), PrepareAccountAsync, cancellation);
        }
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

        // 3. 登录完成后，游戏会话以账号会话为父级，所以能取得当前账号的钱包。
        private GameLauncher CreateGameLauncher(Session user) {
            var games = Architecture.GetModel<ServerSettingsModel>().settings.games;
            var loader = new ZFrameworkGamePackageLoader(m_Resource, packages: m_Resources.Packages);
            return new GameLauncher(user, loader, games);
        }
        private async Task CloseAccountAsync(Session user) {
            var connection = user.GetSystem<AccountSystem>();
            if (connection != null) await connection.DisconnectAsync();
            await m_Backend.CloseAccountAsync(user.GetModel<AccountModel>());
        }

        // App 整体关闭才释放这些资源；普通注销由 Flow 保留 App 基础服务。
        public Task ShutdownAsync() {
            IsStopping = true;
            if (m_Shutdown == null || m_Shutdown.IsFaulted) m_Shutdown = ShutdownCoreAsync();
            return m_Shutdown;
        }
        private async Task ShutdownCoreAsync() {
            await Flow.ShutdownAsync();
            await m_Resources.ShutdownAsync();
            Architecture?.Deinit();
            Architecture = null;
        }
    }
}
