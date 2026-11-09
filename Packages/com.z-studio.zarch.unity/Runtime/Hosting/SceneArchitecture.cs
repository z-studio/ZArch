using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZArch.Unity {
    /// <summary>场景入口。同步配置 + 可选异步初始化，统一使用 Initialization/OnReady/ShutdownAsync。</summary>
    public class SceneArchitecture : ArchitectureEntry {
        public Architecture Architecture { get; private set; }
        protected virtual bool KeepAlive => false;

        protected virtual void Awake() {
            if (KeepAlive) {
                DontDestroyOnLoad(gameObject);
            }

            _ = ObserveInitializationAsync(InitializeOnceAsync(InitializeSceneAsync));
        }

        private async Task InitializeSceneAsync() {
            Architecture = new Architecture { UnhandledExceptionHandler = Debug.LogException };

            try {
                ValidateViews();
                
                await Architecture.StartAsync(services => {
                    RegisterFrameworkServices(services);
                    ConfigureServices(services);
                }, LifetimeToken);
                
                Scope = Architecture.RootScope;
                Scope.RegisterShutdown(StopViewsAsync, DeactivateViews);
                await OnInitializeAsync(LifetimeToken);
                LifetimeToken.ThrowIfCancellationRequested();
                ActivateViews();
            } catch (Exception startupError) {
                try {
                    await Architecture.ShutdownAsync();
                } catch (Exception cleanupError) {
                    throw new AggregateException(startupError, cleanupError);
                }

                throw;
            }
        }

        internal virtual void RegisterFrameworkServices(ArchitectureScope services) {
        }

        protected virtual void ConfigureServices(ArchitectureScope services) {
        }

        protected virtual Task OnInitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Task StopViewsAsync() {
            DeactivateViews();
            return Task.CompletedTask;
        }

        protected override Task ShutdownCoreAsync() => Architecture?.ShutdownAsync() ?? Task.CompletedTask;
    }
}