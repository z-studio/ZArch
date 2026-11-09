using UnityEngine;

namespace ZArch.Unity.Samples {
    public sealed class LobbyView : ArchitectureView {
        [SerializeField]
        private LobbyEntry m_Launcher;

        [SerializeField]
        private UnityEngine.UI.Text m_Status;

        [SerializeField]
        private UnityEngine.UI.Text m_Coins;

        protected override void OnReady() {
            Track(GetModel<WalletModel>().Coins.SubscribeAndInvoke(ShowCoins));
        }

        private void ShowCoins(int value) => m_Coins.text = $"Shared coins: {value}";
        public void OpenGameA() => OpenGame("game-a");
        public void OpenGameB() => OpenGame("game-b");

        private async void OpenGame(string id) {
            if (!IsReady || m_Launcher.Games.IsBusy) {
                return;
            }

            try {
                m_Status.text = "Loading...";
                await m_Launcher.OpenAsync(id);
                m_Status.text = $"Current: {m_Launcher.Games.CurrentId}";
            } catch (System.Exception error) {
                m_Status.text = error.Message;
                Debug.LogException(error);
            }
        }

        public async void ReturnToLobby() {
            if (!IsReady || m_Launcher.Games.IsBusy) {
                return;
            }

            try {
                await m_Launcher.CloseAsync();
                m_Status.text = "Lobby";
            } catch (System.Exception error) {
                m_Status.text = error.Message;
                Debug.LogException(error);
            }
        }
    }
}