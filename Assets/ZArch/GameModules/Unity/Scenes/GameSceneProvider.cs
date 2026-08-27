using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ZArch.GameModules.Unity {
    public static class GameSceneProviderIds {
        public const string kBuildSettings = "build-settings";
    }

    /// <summary>
    /// Opaque lease for a loaded scene. Implementations should retain any native
    /// Addressables, YooAsset or AssetBundle handles required to unload it safely.
    /// </summary>
    public interface IGameSceneHandle {
        Scene Scene { get; }
    }

    /// <summary>
    /// Loads and unloads scenes from one content system.
    /// </summary>
    public interface IGameSceneProvider {
        string Id { get; }

        Task<IGameSceneHandle> LoadAsync(string location, CancellationToken cancellationToken);

        Task UnloadAsync(IGameSceneHandle handle);
    }
}
