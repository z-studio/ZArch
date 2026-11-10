using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace QFramework {
    public interface IGameSceneLoader {
        Task<GameSceneHandle> LoadAsync(string address, CancellationToken cancellation);
    }

    /// <summary>卸载失败时保留句柄，下一次退出可以重试。</summary>
    public sealed class GameSceneHandle {
        private readonly Func<Task> m_Unload;
        private bool m_Unloaded;
        public Scene Scene { get; }

        public GameSceneHandle(Scene scene, Func<Task> unload) {
            Scene = scene;
            m_Unload = unload ?? throw new ArgumentNullException(nameof(unload));
        }

        public async Task UnloadAsync() {
            if (m_Unloaded) {
                return;
            }

            await m_Unload();
            m_Unloaded = true;
        }
    }
}