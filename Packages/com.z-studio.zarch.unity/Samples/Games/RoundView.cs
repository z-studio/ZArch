using UnityEngine;

namespace ZArch.Unity.Samples {
    public sealed class RoundView : ArchitectureView {
        [SerializeField]
        private UnityEngine.UI.Text m_Rounds;

        [SerializeField]
        private UnityEngine.UI.Text m_Coins;

        protected override void OnReady() {
            Track(GetModel<RoundModel>().Rounds.SubscribeAndInvoke(ShowRounds));
            Track(GetModel<WalletModel>().Coins.SubscribeAndInvoke(ShowCoins));
        }

        private void ShowRounds(int rounds) => m_Rounds.text = $"Rounds: {rounds}";
        private void ShowCoins(int coins) => m_Coins.text = $"Coins: {coins}";

        public void PlayRound() {
            if (IsReady) {
                this.SendCommand(new PlayRoundCommand());
            }
        }
    }
}