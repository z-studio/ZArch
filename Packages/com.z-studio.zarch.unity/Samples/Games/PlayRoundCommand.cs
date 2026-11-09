namespace ZArch.Unity.Samples {
    public sealed class PlayRoundCommand : AbstractCommand {
        protected override void OnExecute() {
            this.GetSystem<RoundSystem>().PlayRound();
        }
    }
}
