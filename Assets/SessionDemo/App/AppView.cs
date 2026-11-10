using QFramework;
using UnityEngine;

namespace Demo.App {
    /// <summary>只负责显示和按钮可用状态。第一次阅读业务流程时可以跳过本文件。</summary>
    public sealed class AppView : MonoBehaviour {
        [SerializeField] private GameObject m_LoginPanel;
        [SerializeField] private GameObject m_LobbyPanel;
        [SerializeField] private UnityEngine.UI.Text m_Coins;
        [SerializeField] private UnityEngine.UI.Text m_Status;
        [SerializeField] private UnityEngine.UI.Text m_Account;
        [SerializeField] private UnityEngine.UI.Button[] m_GameButtons;
        [SerializeField] private UnityEngine.UI.Button[] m_LoginButtons;
        [SerializeField] private UnityEngine.UI.Button m_Logout;
        [SerializeField] private UnityEngine.UI.Button m_Return;
        [SerializeField] private UnityEngine.UI.Button m_Retry;
        private Session m_AccountSession;
        private IUnregister m_WalletSubscription;
        private AppState m_LastState = (AppState)(-1);
        private bool m_LastStopping;
        private string m_Error;

        public void Render(AppFlow flow, bool stopping, GamePackageProgress progress = null) {
            if (m_LastState == flow.State && m_LastStopping == stopping
                && ReferenceEquals(m_AccountSession, flow.UserSession) && flow.State != AppState.EnteringGame) return;
            m_LastState = flow.State;
            m_LastStopping = stopping;
            BindWallet(flow.UserSession);
            ShowPanels(flow.State);
            SetButtons(flow.State, stopping);
            m_Status.text = m_Error ?? GetStatusText(flow.State);
            if (m_Error == null && flow.State == AppState.EnteringGame && progress != null)
                m_Status.text = $"{progress.GameId}: {progress.Stage} {progress.Progress:P0}";
        }

        private void BindWallet(Session account) {
            if (ReferenceEquals(m_AccountSession, account)) return;
            m_WalletSubscription?.Unregister();
            m_AccountSession = account;
            if (account == null) return;
            var wallet = account.GetModel<WalletModel>();
            var subscription = wallet.coins.RegisterAndInvoke(ShowCoins);
            m_WalletSubscription = account.Track(subscription);
        }
        private void ShowCoins(int coins) => m_Coins.text = $"Coins: {coins}";

        private void ShowPanels(AppState state) {
            bool showLobby = state == AppState.Lobby || state == AppState.Game || state == AppState.EnteringGame
                             || state == AppState.LeavingGame || state == AppState.GameCloseFailed;
            m_LoginPanel.SetActive(!showLobby);
            m_LobbyPanel.SetActive(showLobby);
            m_Coins.gameObject.SetActive(showLobby);
            if (showLobby) m_Account.text = m_AccountSession.GetModel<AccountModel>().AccountId;
            else if (state == AppState.LoggingOut) m_Account.text = "Closing account...";
            else if (state == AppState.LogoutFailed) m_Account.text = "Cleanup failed - retry logout";
            else m_Account.text = "Signed out";
        }
        private void SetButtons(AppState state, bool stopping) {
            foreach (var button in m_LoginButtons) button.interactable = !stopping && state == AppState.Login;
            foreach (var button in m_GameButtons)
                button.interactable = !stopping && (state == AppState.Lobby || state == AppState.Game);
            m_Return.interactable = !stopping && (state == AppState.Game || state == AppState.GameCloseFailed);
            m_Logout.interactable = !stopping && state != AppState.Login && state != AppState.Created
                                   && state != AppState.Starting && state != AppState.Closed && state != AppState.LoggingOut;
            m_Retry.gameObject.SetActive(!stopping && state == AppState.Created);
        }
        private static string GetStatusText(AppState state) {
            switch (state) {
                case AppState.Created: return "Startup failed - retry";
                case AppState.Starting: return "Loading settings and resources...";
                case AppState.Login: return "Please sign in";
                case AppState.Authenticating: return "Signing in...";
                case AppState.Lobby: return "Lobby";
                case AppState.EnteringGame: return "Opening game...";
                case AppState.Game: return "Playing";
                case AppState.LeavingGame: return "Returning to lobby...";
                case AppState.GameCloseFailed: return "Game exit failed - retry";
                case AppState.LoggingOut: return "Signing out...";
                case AppState.LogoutFailed: return "Sign out failed - retry";
                default: return "App closed";
            }
        }
        public void ShowError(string message) { m_Error = message; m_LastState = (AppState)(-1); }
        public void ClearError() { m_Error = null; m_LastState = (AppState)(-1); }
        private void OnDestroy() => m_WalletSubscription?.Unregister();
    }
}
