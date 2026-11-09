namespace ZArch.Unity.Samples {
    public sealed class CounterModel : AbstractModel {
        public BindableProperty<int> Count { get; } = new(0);
    }
}