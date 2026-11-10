using System;
using System.Threading;
using System.Threading.Tasks;
using ZFramework.Unity;

namespace QFramework {
    /// <summary>复用 ZFramework 资源池，并在子会话释放前清理游戏表现。</summary>
    public sealed class ZFrameworkGamePackageLoader : IGamePackageLoader {
        private readonly ResourceComponent m_Resource;
        private readonly Func<GameInfo, string> m_PackageName;
        private readonly Func<string, CancellationToken, Task> m_ClosePresentation;

        public ZFrameworkGamePackageLoader(ResourceComponent resource, Func<GameInfo, string> packageName = null,
            Func<string, CancellationToken, Task> closePresentation = null) {
            m_Resource = resource ? resource : throw new ArgumentNullException(nameof(resource));
            m_PackageName = packageName ?? (info => info.id);
            m_ClosePresentation = closePresentation ?? ClosePackagePresentationAsync;
        }

        public async Task<IGamePackage> LoadAsync(GameInfo info, CancellationToken cancellation) {
            if (info == null) {
                throw new ArgumentNullException(nameof(info));
            }

            var name = m_PackageName(info);

            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Game resource package name is empty.", nameof(info));
            }

            var inner =
                await new UnityGamePackageLoader(new DefinitionLoader(m_Resource, name)).LoadAsync(info, cancellation);
            return new GamePackage(inner, new ZFrameworkGameResources(m_Resource, name), name, m_ClosePresentation);
        }

        private static async Task ClosePackagePresentationAsync(string package, CancellationToken cancellation) {
            // 只用于游戏独占包；共享资源请放 App 公共包。场景由游戏 OnExitAsync 显式卸载。
            var ui = ZFrameworkUnityEntry.GetComponent<UIComponent>();

            if (ui) {
                await ui.ReleasePackageUIFormsAsync(package, cancellation);
            }

            var entities = ZFrameworkUnityEntry.GetComponent<EntityComponent>();

            if (entities) {
                await entities.ReleasePackageEntitiesAsync(package, cancellation);
            }

            var sound = ZFrameworkUnityEntry.GetComponent<SoundComponent>();

            if (sound) {
                await sound.ReleasePackageSoundsAsync(package, cancellation);
            }
        }

        private sealed class DefinitionLoader : IGameDefinitionLoader {
            private readonly ResourceComponent m_Resource;
            private readonly string m_PackageName;

            public DefinitionLoader(ResourceComponent resource, string packageName) {
                m_Resource = resource;
                m_PackageName = packageName;
            }

            public async Task<GameDefinitionHandle> LoadAsync(GameInfo info, CancellationToken cancellation) {
                cancellation.ThrowIfCancellationRequested();

                if (!m_Resource.IsInitialized) {
                    throw new InvalidOperationException("ZFramework resources are not ready.");
                }

                var definition =
                    await m_Resource.LoadAssetAsync<GameDefinition>(info.entryAddress, cancellation, m_PackageName);

                if (cancellation.IsCancellationRequested) {
                    if (definition) {
                        m_Resource.UnloadAsset(definition);
                    }

                    cancellation.ThrowIfCancellationRequested();
                }

                if (!definition) {
                    throw new InvalidOperationException($"Cannot load game entry '{info.entryAddress}'.");
                }

                return new GameDefinitionHandle(definition, () => m_Resource.UnloadAsset(definition));
            }
        }

        private sealed class GamePackage : IGamePackage {
            private readonly IGamePackage m_Inner;
            private readonly ZFrameworkGameResources m_Resources;
            private readonly string m_Name;
            private readonly Func<string, CancellationToken, Task> m_Close;

            public GamePackage(IGamePackage inner, ZFrameworkGameResources resources, string name,
                Func<string, CancellationToken, Task> close) {
                m_Inner = inner;
                m_Resources = resources;
                m_Name = name;
                m_Close = close;
            }

            public void Configure(Session session) {
                m_Resources.BindLifetime(session.LifetimeToken);
                session.RegisterUtility<IGameResources>(m_Resources);
                m_Inner.Configure(session);
            }

            public IGame CreateGame() => new Game(m_Inner.CreateGame(), m_Resources, m_Name, m_Close);

            public async Task UnloadAsync() {
                await m_Resources.ReleaseAsync();
                await m_Inner.UnloadAsync();
            }
        }

        private sealed class Game : IGame {
            private readonly IGame m_Inner;
            private readonly ZFrameworkGameResources m_Resources;
            private readonly string m_Name;
            private readonly Func<string, CancellationToken, Task> m_Close;

            public Game(IGame inner, ZFrameworkGameResources resources, string name,
                Func<string, CancellationToken, Task> close) {
                m_Inner = inner;
                m_Resources = resources;
                m_Name = name;
                m_Close = close;
            }

            public async Task EnterAsync(Session session, CancellationToken cancellation) {
                using var registration = cancellation.Register(m_Resources.BeginClose);
                await m_Inner.EnterAsync(session, cancellation);
            }

            public async Task ExitAsync(CancellationToken cancellation) {
                m_Resources.BeginClose();
                await m_Inner.ExitAsync(cancellation);
                await m_Close(m_Name, cancellation);
                await m_Resources.DrainAsync();
                
                // 散图绑定需要在 UI/实体停止使用之后释放，保留到 Session.Deinit 前完成。
                var resource = ZFrameworkUnityEntry.GetComponent<ResourceComponent>();

                if (resource) {
                    await resource.ReleasePackageAssetsAsync(m_Name, cancellation);
                }
            }
        }
    }
}