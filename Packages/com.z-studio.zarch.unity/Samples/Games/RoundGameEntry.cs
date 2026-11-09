namespace ZArch.Unity.Samples {
    public sealed class RoundGameEntry : GameEntry {
        // 仅直接运行游戏场景时创建临时玩家数据；从大厅进入则使用大厅的 WalletModel。
        protected override void ConfigureStandaloneServices(ArchitectureScope services) =>
            services.Register(new WalletModel());
    }
}