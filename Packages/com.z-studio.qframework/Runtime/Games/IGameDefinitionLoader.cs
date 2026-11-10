using System;
using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    /// <summary>只加载入口资源，具体资源库负责依赖、缓存和引用计数。</summary>
    public interface IGameDefinitionLoader {
        Task<GameDefinitionHandle> LoadAsync(GameInfo info, CancellationToken cancellation);
    }

    /// <summary>持有入口及其依赖，游戏对象销毁和会话释放之后才 Dispose。</summary>
    public sealed class GameDefinitionHandle : IDisposable {
        private GameDefinition m_Asset;
        private Action m_Release;

        public GameDefinition Asset =>
            m_Release != null ? m_Asset : throw new ObjectDisposedException(nameof(GameDefinitionHandle));

        public GameDefinitionHandle(GameDefinition asset, Action release) {
            if (!asset) {
                throw new ArgumentNullException(nameof(asset));
            }

            m_Asset = asset;
            m_Release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public void Dispose() {
            if (m_Release == null) {
                return;
            }

            m_Release();
            m_Release = null;
            m_Asset = null;
        }
    }
}