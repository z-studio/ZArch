namespace ZArch.Unity.Samples {
    // 属于每次游戏会话：重新进入会重置。
    public sealed class RoundModel : AbstractModel {
        public BindableProperty<int> Rounds { get; } = new(0);
    }
}