using System;
using System.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Demo.App {
    public sealed class LobbyEntry : MonoBehaviour, IController {
        [SerializeField]
        private UnityEngine.UI.Text m_Coins;

        [SerializeField]
        private UnityEngine.UI.Text m_Status;

        [SerializeField]
        private UnityEngine.UI.Button[] m_Buttons;

        // 目录只有地址，没有任何 GameA/GameB 类型或资源对象引用。
        [SerializeField]
        private GameInfo[] m_Games;

        private IUnregister m_WalletSubscription;
        private bool m_Transitioning;
        private Task m_ShutdownTask;
        private IArchitecture m_App;
        public GameLauncher Launcher { get; private set; }
        public IArchitecture GetArchitecture() => DemoApp.Interface;


        private readonly SampleGameResources m_Resources = new();
        private bool m_Stopping;
        public Task Initialization { get; private set; }

        private void Start() {
            Initialization = InitializeAsync();
        }

        private async Task InitializeAsync() {
            m_Transitioning = true;

            foreach (var button in m_Buttons) {
                button.interactable = false;
            }

            try {
                m_Status.text = "Preparing YooAsset packages...";
                await m_Resources.InitializeAsync(m_Games);

                if (m_Stopping) {
                    return;
                }

                m_App = GetArchitecture();
                Launcher = new GameLauncher(m_App,
                    new UnityGamePackageLoader(new YooAssetGameDefinitionLoader()), m_Games);
                this.GetUtility<GameNavigation>().Launcher = Launcher;
                m_WalletSubscription = this.GetModel<WalletModel>().coins
                    .RegisterAndInvoke(value => m_Coins.text = $"App coins: {value}");
                m_Status.text = "Lobby";
                m_Transitioning = false;

                foreach (var button in m_Buttons) {
                    button.interactable = true;
                }
            } catch (Exception error) {
                if (this) {
                    m_Status.text = "Resource preparation failed";
                }

                Debug.LogException(error, this);

                try {
                    await m_Resources.ShutdownAsync();
                } catch (Exception cleanup) {
                    Debug.LogException(cleanup);
                }

                throw;
            }
        }

        public void OpenGameA() => OpenGame("game-a");
        public void OpenGameB() => OpenGame("game-b");

        public async void OpenGame(string id) {
            if (m_Transitioning || m_Stopping) {
                return;
            }

            await Transition(() => Launcher.OpenAsync(id));
        }

        public async void ReturnToLobby() {
            if (m_Transitioning || m_Stopping) {
                return;
            }

            await Transition(() => Launcher.CloseAsync());
        }

        private async Task Transition(Func<Task> operation) {
            m_Transitioning = true;

            foreach (var button in m_Buttons) {
                button.interactable = false;
            }

            try {
                await operation();
            } catch (Exception error) {
                Debug.LogException(error, this);
            } finally {
                m_Transitioning = false;

                if (this) {
                    m_Status.text = $"{Launcher.State} {Launcher.CurrentGameId}";

                    foreach (var button in m_Buttons) {
                        button.interactable = true;
                    }
                }
            }
        }

        // App 正常关闭时显式 await；OnDestroy 仅作为场景意外销毁的兜底。
        public Task ShutdownAsync() {
            m_Stopping = true;

            foreach (var button in m_Buttons) {
                if (button) {
                    button.interactable = false;
                }
            }

            if (m_ShutdownTask == null || m_ShutdownTask.IsFaulted) {
                m_ShutdownTask = ShutdownCoreAsync();
            }

            return m_ShutdownTask;
        }

        private async Task ShutdownCoreAsync() {
            if (Initialization != null) {
                try {
                    await Initialization;
                } catch {
                    /* 初始化路径已经报告并清理。 */
                }
            }

            if (Launcher != null) {
                await Launcher.ShutdownAsync();
            }

            m_WalletSubscription?.Unregister();
            m_WalletSubscription = null;
            await m_Resources.ShutdownAsync();
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