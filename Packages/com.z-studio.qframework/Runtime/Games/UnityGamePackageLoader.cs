using System;
using System.Threading;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace QFramework {
    /// <summary>组合资源加载适配器与游戏生命周期，不直接管理底层资源包。</summary>
    public sealed class UnityGamePackageLoader : IGamePackageLoader {
        private readonly IGameDefinitionLoader m_Definitions;

        public UnityGamePackageLoader(IGameDefinitionLoader definitions) {
            m_Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
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

                if (!definition || !definition.Prefab) {
                    throw new InvalidOperationException($"Game '{info.id}' needs an entry resource and a root prefab.");
                }

                if (definition.Prefab.gameObject.activeSelf) {
                    throw new InvalidOperationException($"Game '{info.id}' root prefab must be inactive.");
                }

                return new Package(handle);
            } catch (Exception error) {
                try {
                    handle.Dispose();
                } catch (Exception cleanup) {
                    throw new AggregateException(error, cleanup);
                }

                throw;
            }
        }

        private sealed class Package : IGamePackage {
            private GameDefinitionHandle m_Handle;

            public Package(GameDefinitionHandle handle) {
                m_Handle = handle;
            }

            public void Configure(Session session) => m_Handle.Asset.Configure(session);
            public IGame CreateGame() => new RuntimeGame(m_Handle.Asset.Prefab);

            public Task UnloadAsync() {
                m_Handle?.Dispose();
                m_Handle = null;
                return Task.CompletedTask;
            }
        }

        private sealed class RuntimeGame : IGame {
            private readonly GameEntry m_Prefab;
            private GameEntry m_Entry;

            public RuntimeGame(GameEntry prefab) {
                m_Prefab = prefab;
            }

            public async Task EnterAsync(Session session, CancellationToken cancellation) {
                cancellation.ThrowIfCancellationRequested();
                m_Entry = Object.Instantiate(m_Prefab);
                await m_Entry.EnterAsync(session, cancellation);
                cancellation.ThrowIfCancellationRequested();
                m_Entry.gameObject.SetActive(true);
            }

            public async Task ExitAsync(CancellationToken cancellation) {
                if (!m_Entry) {
                    return;
                }

                await m_Entry.ExitAsync(cancellation);
                m_Entry.gameObject.SetActive(false);
                Object.Destroy(m_Entry.gameObject);

                // 等待 OnDestroy 完成，之后才能销毁服务、释放资源句柄。
                while (m_Entry) {
                    await Task.Yield();
                }

                m_Entry = null;
            }
        }
    }
}