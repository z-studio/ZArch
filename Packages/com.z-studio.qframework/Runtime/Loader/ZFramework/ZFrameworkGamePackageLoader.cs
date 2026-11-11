using System;
using System.Threading;
using System.Threading.Tasks;
using ZFramework.Unity;
using UnityEngine.SceneManagement;

namespace QFramework {
    /// <summary>
    /// ZFramework 接入层：准备选中的资源包，借用入口配置，把本次资源访问注册到游戏会话。
    /// 不创建第二套缓存。业务/表现退出后归还资源引用，App 持有的包准备器另行管理包本身。
    /// </summary>
    public sealed class ZFrameworkGamePackageLoader : IGamePackageLoader {
        private readonly ResourceComponent m_Resource;
        private readonly ZFrameworkGamePackages m_Packages;
        private readonly Func<GameInfo, string> m_PackageName;
        private readonly Func<string, CancellationToken, Task> m_ClosePresentation;

        public ZFrameworkGamePackageLoader(ResourceComponent resource, Func<GameInfo, string> packageName = null,
            Func<string, CancellationToken, Task> closePresentation = null, ZFrameworkGamePackages packages = null) {
            m_Resource = resource ? resource : throw new ArgumentNullException(nameof(resource));
            m_PackageName = packageName ?? ZFrameworkGamePackages.PackageName;
            m_Packages = packages;
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

            if (m_Packages != null) {
                // 把同一包名交给准备器，兼容项目自定义的包名映射。
                await m_Packages.PrepareAsync(
                    new GameInfo { id = info.id, packageName = name, entryAddress = info.entryAddress }, cancellation);
            }

            var inner = await new UnityGamePackageLoader(new DefinitionLoader(m_Resource, name),
                new SceneLoader(name)).LoadAsync(info, cancellation);
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

        private sealed class SceneLoader : IGameSceneLoader {
            private readonly string m_Package;
            public SceneLoader(string package) => m_Package = package;

            public async Task<GameSceneHandle> LoadAsync(string address, CancellationToken cancellation) {
                cancellation.ThrowIfCancellationRequested();
                var scenes = ZFrameworkUnityEntry.GetComponent<SceneComponent>();

                if (!scenes) {
                    throw new InvalidOperationException("Configure ZFramework SceneComponent for scene games.");
                }

                var scene = await scenes.LoadSceneAsync(address, LoadSceneMode.Additive, packageName: m_Package);

                if (!scene.IsValid() || !scene.isLoaded) {
                    throw new InvalidOperationException($"Cannot load game scene '{address}'.");
                }

                // 即使此时取消也返回句柄，由统一退出路径卸载已经完成的场景。
                return new GameSceneHandle(scene, async () => {
                    if (!await scenes.UnloadSceneAsync(address, (Action<float>)null, m_Package)) {
                        throw new InvalidOperationException($"Cannot unload game scene '{address}'.");
                    }
                });
            }
        }

        // 只负责入口资产的借用/归还；Prefab/场景的运行规则交给 UnityGamePackageLoader。
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

        // 在通用 Package 外补上 ZFramework 资源记录；会话释放后才归还这些借用。
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

        // 在通用 RuntimeGame 外补上表现清理：UI/实体/声音停止使用之后，资源才能安全归还。
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