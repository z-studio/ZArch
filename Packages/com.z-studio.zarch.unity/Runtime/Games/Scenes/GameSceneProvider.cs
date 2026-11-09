using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ZArch.Unity {
    public static class GameSceneProviderIds {
        public const string BuildSettings = "build-settings";
    }

    public interface IGameSceneHandle {
        Scene Scene { get; }
    }

    public interface IGameSceneProvider {
        string Id { get; }
        Task<IGameSceneHandle> LoadAsync(string location, CancellationToken cancellationToken, Action<Scene> bind);
        Task UnloadAsync(IGameSceneHandle handle);
    }
}