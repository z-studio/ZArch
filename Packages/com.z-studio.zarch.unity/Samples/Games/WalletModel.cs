namespace ZArch.Unity.Samples {
    // 属于应用：切换游戏不重置。
    public sealed class WalletModel : AbstractModel {
        public BindableProperty<int> Coins { get; } = new(100);
    }
}