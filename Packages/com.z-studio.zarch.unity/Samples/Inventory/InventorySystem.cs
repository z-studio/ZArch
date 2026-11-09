namespace ZArch.Unity.Samples {
    public sealed class InventorySystem : AbstractSystem {
        public void AddApple() => this.GetModel<InventoryModel>().Apples.Value++;
    }
}