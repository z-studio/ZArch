namespace ZArch.Unity.Samples {
    public sealed class InventoryModel : AbstractModel {
        public BindableProperty<int> Apples { get; } = new(0);
    }
}
