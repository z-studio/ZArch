namespace ZArch.Unity.Samples {
    public sealed class AddAppleCommand : AbstractCommand {
        protected override void OnExecute() {
            this.GetSystem<InventorySystem>().AddApple();
        }
    }
}
