namespace ZArch.Unity.Samples {
    public sealed class IncreaseCountCommand : AbstractCommand {
        protected override void OnExecute() {
            this.GetModel<CounterModel>().Count.Value++;
        }
    }
}
