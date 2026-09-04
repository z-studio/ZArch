using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ZArch.GameModules.Unity {
    public sealed class UnityGameContentLoader : IGameContentLoader {
        private sealed class SceneContentHandle : IGameContentHandle {
            public UnityGameContentLoader Owner { get; }
            public IGameSceneProvider Provider { get; }
            public IGameSceneHandle SceneHandle { get; }

            public SceneContentHandle(
                UnityGameContentLoader owner,
                IGameSceneProvider provider,
                IGameSceneHandle sceneHandle
            ) {
                Owner = owner;
                Provider = provider;
                SceneHandle = sceneHandle;
            }
        }

        private readonly Dictionary<string, IGameSceneProvider> m_Providers = new(StringComparer.Ordinal);

        public UnityGameContentLoader(params IGameSceneProvider[] providers) {
            if (providers == null) {
                throw new ArgumentNullException(nameof(providers));
            }

            if (providers.Length == 0) {
                RegisterProvider(new BuildSettingsGameSceneProvider(), nameof(providers));
                return;
            }

            foreach (var provider in providers) {
                RegisterProvider(provider, nameof(providers));
            }
        }

        public async Task<IGameContentHandle> LoadAsync(
            IGameModule module,
            ArchitectureScope scope,
            GameEnterContext context,
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

            var providerId = unityModule.SceneProviderId;
            var location = unityModule.SceneLocation;

            if (string.IsNullOrWhiteSpace(providerId)) {
                throw new InvalidOperationException($"Game module '{module.Id}' has an empty scene provider ID.");
            }

            if (string.IsNullOrWhiteSpace(location)) {
                throw new InvalidOperationException($"Game module '{module.Id}' has an empty scene location.");
            }

            if (!m_Providers.TryGetValue(providerId, out var provider)) {
                throw new KeyNotFoundException(
                    $"Scene provider '{providerId}' required by game module '{module.Id}' is not registered."
                );
            }

            cancellationToken.ThrowIfCancellationRequested();

            var sceneHandle = await provider.LoadAsync(location, cancellationToken).ConfigureAwait(true);

            if (sceneHandle == null) {
                throw new InvalidOperationException(
                    $"Scene provider '{provider.Id}' returned a null handle for '{location}'."
                );
            }

            try {
                cancellationToken.ThrowIfCancellationRequested();

                var scene = sceneHandle.Scene;

                if (!scene.IsValid() || !scene.isLoaded) {
                    throw new InvalidOperationException(
                        $"Scene provider '{provider.Id}' returned an invalid or unloaded scene for '{location}'."
                    );
                }

                var entry = FindSceneEntry(scene);
                entry.BindScope(scope);
                return new SceneContentHandle(this, provider, sceneHandle);
            } catch (Exception loadException) {
                try {
                    await provider.UnloadAsync(sceneHandle).ConfigureAwait(true);
                } catch (Exception cleanupException) {
                    throw new AggregateException(
                        $"Loading game content for module '{module.Id}' failed, and rolling back the scene also failed.",
                        loadException,
                        cleanupException
                    );
                }

                throw;
            }
        }

        public Task UnloadAsync(IGameContentHandle content) {
            if (content is not SceneContentHandle sceneContent
                || !ReferenceEquals(sceneContent.Owner, this)) {
                throw new ArgumentException(
                    "Content was not created by this Unity game content loader.",
                    nameof(content)
                );
            }

            return sceneContent.Provider.UnloadAsync(sceneContent.SceneHandle);
        }

        private void RegisterProvider(IGameSceneProvider provider, string parameterName) {
            if (provider == null) {
                throw new ArgumentException("Scene providers contain null.", parameterName);
            }

            if (string.IsNullOrWhiteSpace(provider.Id)) {
                throw new ArgumentException("A scene provider has an empty ID.", parameterName);
            }

            if (!m_Providers.TryAdd(provider.Id, provider)) {
                throw new ArgumentException(
                    $"Duplicate scene provider ID '{provider.Id}'.",
                    parameterName
                );
            }
        }

        private static GameModuleSceneEntry FindSceneEntry(Scene scene) {
            GameModuleSceneEntry result = null;

            foreach (var root in scene.GetRootGameObjects()) {
                foreach (var entry in root.GetComponentsInChildren<GameModuleSceneEntry>(true)) {
                    if (result != null) {
                        throw new InvalidOperationException(
                            $"Scene '{scene.path}' contains more than one {nameof(GameModuleSceneEntry)}."
                        );
                    }

                    result = entry;
                }
            }

            return result
                   ?? throw new InvalidOperationException(
                       $"Scene '{scene.path}' does not contain a {nameof(GameModuleSceneEntry)}."
                   );
        }
    }
}
