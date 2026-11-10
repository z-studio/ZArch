using System;
using System.Threading;
using System.Threading.Tasks;
using QFramework;
using UnityEngine;
using ZFramework.Unity;

namespace Demo.App {
    /// <summary>场景入口：连接业务和界面，接收 Inspector 中的按钮点击。</summary>
    public sealed class AppEntry : MonoBehaviour, IController {
        public static AppEntry Instance { get; private set; }

        [SerializeField] private ResourceComponent m_Resource;
        [SerializeField] private AppView m_View;
        [SerializeField, Min(1)] private float m_FoundationTimeoutSeconds = 60;
        private AppLogic m_Logic;

        public AppFlow Flow => m_Logic?.Flow;
        private readonly TaskCompletionSource<bool> m_FirstStartup = new();
        public Task Initialization => m_FirstStartup.Task;
        public IArchitecture GetArchitecture() => Flow?.UserSession ?? m_Logic?.Architecture;

        private void Awake() {
            Instance = this;
            // 接真实服务器时，将 DemoBackend 替换成 IAppBackend 的实际实现。
            m_Logic = new AppLogic(m_Resource, new DemoBackend(), m_FoundationTimeoutSeconds);
        }
        // 启动 Procedure 调用一次；失败时由 RetryStartup 按钮重试。
        public void StartApp() => Run(StartAppAsync);
        private async Task StartAppAsync() {
            try { await m_Logic.StartAsync(); m_FirstStartup.TrySetResult(true); }
            catch (Exception error) { m_FirstStartup.TrySetException(error); throw; }
        }

        // 按钮绑定这些命名方法；点击后发生什么，可从这里直接跳到业务方法。
        public void LoginAlice() => Run(() => LoginAsync("Alice"));
        public void LoginBob() => Run(() => LoginAsync("Bob"));
        public void OpenGameA() => Run(() => Flow.OpenGameAsync("game-a", new GameLaunchData("demo-room-a")));
        public void OpenGameB() => Run(() => Flow.OpenGameAsync("game-b", new GameLaunchData("demo-room-b")));
        public void ReturnToLobby() => Run(() => Flow.ReturnToLobbyAsync());
        public void Logout() => Run(() => Flow.LogoutAsync());
        public void RetryStartup() => Run(StartAppAsync);

        public Task LoginAsync(string account, CancellationToken cancellation = default) =>
            m_Logic.LoginAsync(account, cancellation);
        public Task ShutdownAsync() => m_Logic?.ShutdownAsync() ?? Task.CompletedTask;

        // Procedure 每帧调用，显示跟随 Flow；业务按钮仍走下方统一错误处理。
        public void RenderView() => RefreshView();
        private void RefreshView() {
            if (m_View && m_Logic != null) m_View.Render(Flow, m_Logic.IsStopping, m_Logic.PackageProgress);
        }

        // async void 只用于 Unity 按钮入口。业务方法返回 Task，错误在这里显示。
        private async void Run(Func<Task> action) {
            if (m_View) m_View.ClearError();
            try { await action(); }
            catch (OperationCanceledException) { /* 注销主动取消请求，属于正常流程。 */ }
            catch (Exception error) {
                if (m_View) m_View.ShowError(error.Message);
                Debug.LogException(error, this);
            }
            RefreshView();
        }
        private async void OnDestroy() {
            if (Instance == this) Instance = null;
            try { await ShutdownAsync(); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
