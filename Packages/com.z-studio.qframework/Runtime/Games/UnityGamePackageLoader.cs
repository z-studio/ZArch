using System;
using System.Threading;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace QFramework {
    /// <summary>
    /// 把入口配置变成可运行的 Unity 游戏，不直接管理 YooAsset 包或资源池。
    /// Package 持有配置引用，RuntimeGame 持有本次创建的对象；GameLauncher 决定它们何时退出和释放。
    /// </summary>
    public sealed class UnityGamePackageLoader : IGamePackageLoader {
        private readonly IGameDefinitionLoader m_Definitions;
        private readonly IGameSceneLoader m_Scenes;

        public UnityGamePackageLoader(IGameDefinitionLoader definitions, IGameSceneLoader scenes = null) {
            m_Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            m_Scenes = scenes;
        }

        public async Task<IGamePackage> LoadAsync(GameInfo info, CancellationToken cancellation) {
            if (info == null) {
                throw new ArgumentNullException(nameof(info));
            }

            cancellation.ThrowIfCancellationRequested();
            var handle = await m_Definitions.LoadAsync(info, cancellation)
                         ?? throw new InvalidOperationException("Resource loader returned no entry handle.");

            try {
                cancellation.ThrowIfCancellationRequested();
                var definition = handle.Asset;

                if (!definition || (!definition.Prefab && string.IsNullOrWhiteSpace(definition.SceneAddress))) {
                    throw new InvalidOperationException($"Game '{info.id}' needs a prefab or scene entry.");
                }

                if (definition.Prefab && !string.IsNullOrWhiteSpace(definition.SceneAddress)) {
                    throw new InvalidOperationException("Choose one entry: prefab or scene.");
                }

                if (!string.IsNullOrWhiteSpace(definition.SceneAddress) && m_Scenes == null) {
                    throw new InvalidOperationException("Configure an IGameSceneLoader for scene entries.");
                }

                if (definition.Prefab && definition.Prefab.gameObject.activeSelf) {
                    throw new InvalidOperationException($"Game '{info.id}' root prefab must be inactive.");
                }

                return new Package(handle, m_Scenes);
            } catch (Exception error) {
                try {
                    handle.Dispose();
                } catch (Exception cleanup) {
                    throw new AggregateException(error, cleanup);
                }

                throw;
            }
        }

        // 配置的持有者：运行对象与会话结束后，GameLauncher 才调用 UnloadAsync 归还入口引用。
        private sealed class Package : IGamePackage {
            private GameDefinitionHandle m_Handle;
            private readonly IGameSceneLoader m_Scenes;

            public Package(GameDefinitionHandle handle, IGameSceneLoader scenes) {
                m_Scenes = scenes;
                m_Handle = handle;
            }

            public void Configure(Session session) => m_Handle.Asset.Configure(session);
            public IGame CreateGame() => new RuntimeGame(m_Handle.Asset, m_Scenes);

            public Task UnloadAsync() {
                m_Handle?.Dispose();
                m_Handle = null;
                return Task.CompletedTask;
            }
        }

        // 运行对象的持有者：每次 CreateGame 都是新的实例，不把 m_Entry / m_Scene 写回配置资产。
        private sealed class RuntimeGame : IGame {
            private readonly GameDefinition m_Definition;
            private readonly IGameSceneLoader m_Scenes;
            private GameSceneHandle m_Scene;
            private GameEntry m_Entry;
            private bool m_EnterStarted;

            public RuntimeGame(GameDefinition definition, IGameSceneLoader scenes) {
                m_Definition = definition;
                m_Scenes = scenes;
            }

            public async Task EnterAsync(Session session, CancellationToken cancellation) {
                cancellation.ThrowIfCancellationRequested();

                // 1. 先取得未激活的业务根对象；此时还不能依赖 Unity 的 Awake/OnEnable 访问会话。
                if (m_Definition.Prefab) {
                    m_Entry = Object.Instantiate(m_Definition.Prefab);
                } else {
                    // 底层场景加载完成后才检查取消，确保句柄始终能由退出路径归还。
                    m_Scene = await m_Scenes.LoadAsync(m_Definition.SceneAddress, cancellation);
                    cancellation.ThrowIfCancellationRequested();

                    foreach (var root in m_Scene.Scene.GetRootGameObjects()) {
                        foreach (var entry in root.GetComponentsInChildren<GameEntry>(true)) {
                            if (m_Entry) {
                                throw new InvalidOperationException("A game scene must contain exactly one GameEntry.");
                            }

                            m_Entry = entry;
                        }
                    }

                    if (!m_Entry || m_Entry.transform.parent || m_Entry.gameObject.activeSelf) {
                        throw new InvalidOperationException("A game scene needs one inactive root GameEntry.");
                    }
                }

                // 2. 标记业务开始后再调用。即使 OnEnter 中途失败，也需要 OnExit 收尾。
                m_EnterStarted = true;
                await m_Entry.EnterAsync(session, cancellation);
                cancellation.ThrowIfCancellationRequested();
                // 3. 所有进入步骤成功后才激活，Procedure 此后才能把大厅切换为游戏显示。
                m_Entry.gameObject.SetActive(true);
            }

            public async Task ExitAsync(CancellationToken cancellation) {
                if (!m_Entry) {
                    if (m_Scene != null) {
                        await m_Scene.UnloadAsync();
                        m_Scene = null;
                    }

                    return;
                }

                // 业务退出仍可访问会话；根对象真正消失之后，GameLauncher 才会释放 Model/System。
                if (m_EnterStarted) {
                    await m_Entry.ExitAsync(cancellation);
                }

                m_Entry.gameObject.SetActive(false);

                if (m_Scene != null) {
                    await m_Scene.UnloadAsync();
                    m_Scene = null;
                } else {
                    Object.Destroy(m_Entry.gameObject);

                    // Destroy 延迟到帧末完成。OnDestroy 可能仍需业务数据，必须等对象真正消失再继续。
                    while (m_Entry) {
                        await Task.Yield();
                    }
                }

                m_Entry = null;
            }
        }
    }
}