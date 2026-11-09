namespace ZArch.Unity.Samples {
    // 入口只负责共享服务与游戏配置，界面放在 LobbyView。
    public sealed class LobbyEntry : GameLauncher {
        protected override void ConfigureServices(ArchitectureScope services) => services.Register(new WalletModel());
    }
}