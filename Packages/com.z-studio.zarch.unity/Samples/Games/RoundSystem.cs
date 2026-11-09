namespace ZArch.Unity.Samples {
    public sealed class RoundSystem : AbstractSystem {
        public void PlayRound() {
            this.GetModel<RoundModel>().Rounds.Value++;
            this.GetModel<WalletModel>().Coins.Value += 10;
        }
    }
}