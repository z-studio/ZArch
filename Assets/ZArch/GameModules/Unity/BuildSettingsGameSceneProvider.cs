using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ZArch.GameModules.Unity {
    public sealed class BuildSettingsGameSceneProvider : IGameSceneProvider {
        private sealed class BuildSceneHandle : IGameSceneHandle {
            public Scene Scene { get; }

            public BuildSceneHandle(Scene scene) {
                Scene = scene;
            }
        }

        public string Id => GameSceneProviderIds.kBuildSettings;

        public async Task<IGameSceneHandle> LoadAsync(string location, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(location)) {
                throw new ArgumentException("Scene location cannot be empty.", nameof(location));
            }

            cancellationToken.ThrowIfCancellationRequested();
            Scene loadedScene = default;

            void CaptureLoadedScene(Scene scene, LoadSceneMode _) {
                if (Matches(scene, location)) {
                    loadedScene = scene;
                }
            }

            SceneManager.sceneLoaded += CaptureLoadedScene;

            try {
                var operation = SceneManager.LoadSceneAsync(location, LoadSceneMode.Additive)
                                ?? throw new InvalidOperationException(
                                    $"Unity did not start loading scene '{location}'."
                                );

                while (!operation.isDone) {
                    await Task.Yield();
                }
            } finally {
                SceneManager.sceneLoaded -= CaptureLoadedScene;
            }

            if (!loadedScene.IsValid() || !loadedScene.isLoaded) {
                throw new InvalidOperationException(
                    $"Loaded scene '{location}' could not be identified. " +
                    "Ensure it is enabled in Build Settings, or select the provider that owns this scene."
                );
            }

            try {
                cancellationToken.ThrowIfCancellationRequested();
                return new BuildSceneHandle(loadedScene);
            } catch {
                await UnloadSceneAsync(loadedScene).ConfigureAwait(true);
                throw;
            }
        }

        public Task UnloadAsync(IGameSceneHandle handle, CancellationToken cancellationToken) {
            if (handle is not BuildSceneHandle buildHandle) {
                throw new ArgumentException(
                    "Scene handle was not created by the Build Settings provider.",
                    nameof(handle)
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
            return UnloadSceneAsync(buildHandle.Scene);
        }

        private static async Task UnloadSceneAsync(Scene scene) {
            if (!scene.IsValid() || !scene.isLoaded) {
                return;
            }

            var operation = SceneManager.UnloadSceneAsync(scene);

            if (operation == null) {
                return;
            }

            while (!operation.isDone) {
                await Task.Yield();
            }
        }

        private static bool Matches(Scene scene, string location) {
            if (string.Equals(scene.name, location, StringComparison.Ordinal)
                || string.Equals(scene.path, location, StringComparison.Ordinal)) {
                return true;
            }

            var requestedPath = location.EndsWith(".unity", StringComparison.Ordinal)
                ? location
                : $"{location}.unity";

            return string.Equals(scene.path, requestedPath, StringComparison.Ordinal);
        }
    }
}
