using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace QFramework {
    /// <summary>本次游戏拥有的资源引用；实现负责使用已有基础资源系统。</summary>
    public interface IGameResources : IUtility {
        Task<T> LoadAsync<T>(string address, CancellationToken cancellation = default) where T : Object;
        Task<GameObject> InstantiateAsync(string address, Transform parent = null, CancellationToken cancellation = default);
        bool Release(Object asset);
        bool Destroy(GameObject instance);
    }
}
