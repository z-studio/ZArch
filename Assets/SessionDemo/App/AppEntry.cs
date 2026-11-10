using System;
using System.Threading;
using System.Threading.Tasks;
using QFramework;
using UnityEngine;
using ZFramework.Unity;

namespace Demo.App {
    public sealed class AppEntry : MonoBehaviour, IController {
        [SerializeField]
        private ResourceComponent m_Resource;

        [SerializeField, Min(1)]
        private float m_FoundationTimeoutSeconds = 60;

        [SerializeField]
        private GameObject m_LoginPanel;

        [SerializeField]
        private GameObject m_LobbyPanel;

        [SerializeField]
        private UnityEngine.UI.Text m_Coins;

        [SerializeField]
        private UnityEngine.UI.Text m_Status;

        [SerializeField]
        private UnityEngine.UI.Text m_Account;

        [SerializeField]
        private UnityEngine.UI.Button[] m_GameButtons;

        [SerializeField]
        private UnityEngine.UI.Button[] m_LoginButtons;

        [SerializeField]
        private UnityEngine.UI.Button m_Logout;

        [SerializeField]
        private UnityEngine.UI.Button m_Return;

        [SerializeField]
        private UnityEngine.UI.Button m_Retry;

        private IAppBackend m_Backend;
        private IArchitecture m_App;
        private Session m_BoundAccount;
        private IUnregister m_WalletSubscription;
        private AppState m_LastState = (AppState)(-1);
        private Task m_Shutdown;
        private bool m_Stopping;
        public AppFlow Flow { get; private set; }
        public Task Initialization { get; private set; }
        public IArchitecture GetArchitecture() => Flow?.UserSession ?? m_App;

        private void Start() {
            m_Backend = new DemoBackend();
            m_App = DemoApp.Interface;
            Flow = new AppFlow(m_App, user => new GameLauncher(user,
                    new ZFrameworkGamePackageLoader(m_Resource), m_App.GetModel<ServerSettingsModel>().settings.games),
                user => m_Backend.CloseAccountAsync(user.GetModel<AccountModel>()));
            Initialization = Flow.StartAsync(PrepareAsync);
            ObserveInitialization();
            Refresh();
        }

        private async void ObserveInitialization() {
            try {
                await Initialization;
            } catch (OperationCanceledException) {
            } catch (Exception error) {
                Debug.LogException(error, this);
            }
        }

        private async Task PrepareAsync(CancellationToken cancellation) {
            if (!m_Resource) {
                throw new InvalidOperationException("Assign the App ZFramework ResourceComponent.");
            }

            var settings = await m_Backend.LoadSettingsAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            m_App.GetModel<ServerSettingsModel>().settings = settings;
            var deadline = Time.realtimeSinceStartup + m_FoundationTimeoutSeconds;

            while (!m_Resource.IsInitialized) {
                cancellation.ThrowIfCancellationRequested();

                if (Time.realtimeSinceStartup >= deadline) {
                    throw new TimeoutException(
                        "ZFramework resources did not initialize. Check the foundation configuration before retrying startup.");
                }

                await Task.Yield();
            }

            try {
                await m_Resources.InitializeAsync(m_Resource, settings.games, cancellation);
            } catch (Exception error) {
                try {
                    await m_Resources.ShutdownAsync(m_Resource);
                } catch (Exception cleanup) {
                    throw new AggregateException(error, cleanup);
                }

                throw;
            }
        }

        private readonly SampleGameResources m_Resources = new();

        public Task LoginAsync(string account, CancellationToken cancellation = default) =>
            Flow.LoginAsync(async (user, token) => {
                var data = await m_Backend.LoginAsync(account, token);
                token.ThrowIfCancellationRequested();
                user.RegisterModel(new AccountModel(data));
                user.RegisterModel(new WalletModel(data.coins));
            }, cancellation);

        public void LoginAlice() => Observe(() => LoginAsync("Alice"));
        public void LoginBob() => Observe(() => LoginAsync("Bob"));
        public void OpenGameA() => Observe(() => Flow.OpenGameAsync("game-a"));
        public void OpenGameB() => Observe(() => Flow.OpenGameAsync("game-b"));
        public void ReturnToLobby() => Observe(() => Flow.ReturnToLobbyAsync());
        public void Logout() => Observe(() => Flow.LogoutAsync());
        public void RetryStartup() => Observe(() => Flow.StartAsync(PrepareAsync));

        private async void Observe(Func<Task> action) {
            try {
                await action();
            } catch (OperationCanceledException) {
            } catch (Exception error) {
                Debug.LogException(error, this);
            }

            if (this) {
                Refresh();
            }
        }

        private void Update() {
            if (Flow != null
                && (m_LastState != Flow.State || !ReferenceEquals(m_BoundAccount, Flow.UserSession))) {
                Refresh();
            }
        }

        private void Refresh() {
            m_LastState = Flow.State;

            if (!ReferenceEquals(m_BoundAccount, Flow.UserSession)) {
                m_WalletSubscription?.Unregister();
                m_BoundAccount = Flow.UserSession;
                m_WalletSubscription = m_BoundAccount?.Track(m_BoundAccount.GetModel<WalletModel>().coins
                    .RegisterAndInvoke(value => m_Coins.text = $"Coins: {value}"));
            }

            var hasAccount = Flow.State == AppState.Lobby || Flow.State == AppState.Game
                                                          || Flow.State == AppState.EnteringGame
                                                          || Flow.State == AppState.LeavingGame
                                                          || Flow.State == AppState.GameCloseFailed;
            m_LoginPanel.SetActive(!hasAccount);
            m_LobbyPanel.SetActive(hasAccount);
            m_Status.text = Flow.State.ToString();
            m_Retry.gameObject.SetActive(!m_Stopping && Flow.State == AppState.Created);
            m_Account.text = hasAccount
                ? m_BoundAccount.GetModel<AccountModel>().AccountId
                : Flow.State == AppState.LoggingOut
                    ? "Closing account..."
                    : Flow.State == AppState.LogoutFailed
                        ? "Cleanup failed - retry logout"
                        : "Signed out";
            m_Coins.gameObject.SetActive(hasAccount);

            foreach (var button in m_LoginButtons) {
                button.interactable = !m_Stopping && Flow.State == AppState.Login;
            }

            foreach (var button in m_GameButtons) {
                button.interactable = !m_Stopping && (Flow.State == AppState.Lobby || Flow.State == AppState.Game);
            }

            m_Return.interactable =
                !m_Stopping && (Flow.State == AppState.Game || Flow.State == AppState.GameCloseFailed);
            // 注销可打断登录和游戏加载，也可重试失败的清理。
            m_Logout.interactable = !m_Stopping && Flow.State != AppState.Login && Flow.State != AppState.Created
                                    && Flow.State != AppState.Starting
                                    && Flow.State != AppState.Closed && Flow.State != AppState.LoggingOut;
        }

        public Task ShutdownAsync() {
            m_Stopping = true;

            if (m_Shutdown == null || m_Shutdown.IsFaulted) {
                m_Shutdown = ShutdownCoreAsync();
            }

            return m_Shutdown;
        }

        private async Task ShutdownCoreAsync() {
            if (Flow != null) {
                await Flow.ShutdownAsync();
            }

            m_WalletSubscription?.Unregister();
            m_WalletSubscription = null;
            await m_Resources.ShutdownAsync(m_Resource);
            m_App?.Deinit();
            m_App = null;
        }

        private async void OnDestroy() {
            try {
                await ShutdownAsync();
            } catch (Exception error) {
                Debug.LogException(error);
            }
        }
    }
}