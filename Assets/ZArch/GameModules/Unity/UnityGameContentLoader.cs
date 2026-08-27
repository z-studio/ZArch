using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ZArch.GameModules.Unity {
    public sealed class UnityGameContentLoader : IGameContentLoader {
        private sealed class SceneContentHandle : IGameContentHandle {
            public Scene Scene { get; }

            public SceneContentHandle(Scene scene) {
                Scene = scene;
            }
        }

        public async Task<IGameContentHandle> LoadAsync(
            IGameModule module,
            ArchitectureScope scope,
            GameLaunchContext context,
            CancellationToken cancellationToken
        ) {
            if (module is not IUnityGameModule unityModule) {
                throw new ArgumentException(
                    $"Game module {module?.GetType().FullName ?? "null"} does not implement {nameof(IUnityGameModule)}.",
                    nameof(module)
                );
            }

            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (string.IsNullOrWhiteSpace(unityModule.SceneNameOrPath)) {
                throw new InvalidOperationException($"Game module '{module.Id}' has an empty scene name or path.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Scene loadedScene = default;

            void CaptureLoadedScene(Scene scene, LoadSceneMode _) {
                if (Matches(scene, unityModule.SceneNameOrPath)) {
                    loadedScene = scene;
                }
            }

            SceneManager.sceneLoaded += CaptureLoadedScene;

            try {
                var operation = SceneManager.LoadSceneAsync(
                    unityModule.SceneNameOrPath,
                    LoadSceneMode.Additive
                ) ?? throw new InvalidOperationException(
                    $"Unity did not start loading scene '{unityModule.SceneNameOrPath}'."
                );

                while (!operation.isDone) {
                    await Task.Yield();
                }
            } finally {
                SceneManager.sceneLoaded -= CaptureLoadedScene;
            }

            if (!loadedScene.IsValid() || !loadedScene.isLoaded) {
                throw new InvalidOperationException(
                    $"Loaded scene '{unityModule.SceneNameOrPath}' could not be identified."
                );
            }

            try {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = FindSceneEntry(loadedScene);
                entry.BindScope(scope);
                return new SceneContentHandle(loadedScene);
            } catch {
                await UnloadSceneAsync(loadedScene).ConfigureAwait(true);
                throw;
            }
        }

        public Task UnloadAsync(IGameContentHandle content, CancellationToken cancellationToken) {
            if (content is not SceneContentHandle sceneContent) {
                throw new ArgumentException("Content was not created by this Unity game content loader.", nameof(content));
            }

            return UnloadSceneAsync(sceneContent.Scene);
        }

        private static GameSceneEntry FindSceneEntry(Scene scene) {
            GameSceneEntry result = null;

            foreach (var root in scene.GetRootGameObjects()) {
                foreach (var entry in root.GetComponentsInChildren<GameSceneEntry>(true)) {
                    if (result != null) {
                        throw new InvalidOperationException(
                            $"Scene '{scene.path}' contains more than one {nameof(GameSceneEntry)}."
                        );
                    }

                    result = entry;
                }
            }

            return result ?? throw new InvalidOperationException(
                $"Scene '{scene.path}' does not contain a {nameof(GameSceneEntry)}."
            );
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

        private static bool Matches(Scene scene, string nameOrPath) {
            if (string.Equals(scene.name, nameOrPath, StringComparison.Ordinal)
                || string.Equals(scene.path, nameOrPath, StringComparison.Ordinal)) {
                return true;
            }

            var requestedPath = nameOrPath.EndsWith(".unity", StringComparison.Ordinal)
                ? nameOrPath
                : $"{nameOrPath}.unity";

            return string.Equals(scene.path, requestedPath, StringComparison.Ordinal);
        }
    }
}
