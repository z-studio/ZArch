using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ZArch.Unity {
    public sealed class UnityGameContentLoader : IGameContentLoader {
        private sealed class SceneContent : IGameContent {
            private readonly IGameSceneProvider m_Provider;
            private readonly IGameSceneHandle m_Handle;
            private readonly GameEntry m_Entry;

            public SceneContent(IGameSceneProvider provider, IGameSceneHandle handle, GameEntry entry) {
                m_Provider = provider;
                m_Handle = handle;
                m_Entry = entry;
            }

            public Task EnterAsync(object arguments, CancellationToken cancellationToken) =>
                m_Entry.InitializeGameAsync(arguments, cancellationToken);

            public Task ExitAsync() => m_Entry ? m_Entry.ExitGameAsync() : Task.CompletedTask;
            public Task UnloadAsync() => m_Provider.UnloadAsync(m_Handle);
        }

        private readonly Dictionary<string, IGameSceneProvider> m_Providers = new(StringComparer.Ordinal);

        public UnityGameContentLoader(params IGameSceneProvider[] providers) {
            if (providers == null) {
                throw new ArgumentNullException(nameof(providers));
            }

            if (providers.Length == 0) {
                providers = new IGameSceneProvider[] { new BuildSettingsGameSceneProvider() };
            }

            foreach (var provider in providers) {
                if (provider == null || string.IsNullOrWhiteSpace(provider.Id)) {
                    throw new ArgumentException("Scene providers contains a missing provider or empty ID.",
                        nameof(providers));
                }

                if (!m_Providers.TryAdd(provider.Id, provider)) {
                    throw new ArgumentException($"Duplicate scene provider ID '{provider.Id}'.", nameof(providers));
                }
            }
        }

        public void Validate(IGameDefinition game) {
            if (game is not GameDefinition definition || !definition) {
                throw new ArgumentException("Unity games must use a GameDefinition asset.", nameof(game));
            }

            definition.Validate();

            if (!m_Providers.ContainsKey(definition.SceneProviderId)) {
                throw new InvalidOperationException(
                    $"Game '{game.Id}': scene provider '{definition.SceneProviderId}' is not configured.");
            }

            if (definition.SceneProviderId == GameSceneProviderIds.BuildSettings
                && !UnityEngine.Application.CanStreamedLevelBeLoaded(definition.SceneLocation)) {
                throw new InvalidOperationException(
                    $"Game '{game.Id}': add scene '{definition.SceneLocation}' to enabled Build Settings scenes.");
            }
        }

        public async Task<IGameContent> LoadAsync(IGameDefinition game, ArchitectureScope scope,
            CancellationToken cancellationToken) {
            Validate(game);

            if (scope?.IsActive != true) {
                throw new ArgumentException("Game scope must be active.", nameof(scope));
            }

            var definition = (GameDefinition)game;
            var provider = m_Providers[definition.SceneProviderId];
            GameEntry entry = null;
            Exception bindingError = null;

            void BindLoadedScene(Scene scene) {
                try {
                    var entries = new List<GameEntry>();

                    foreach (var root in scene.GetRootGameObjects()) {
                        entries.AddRange(root.GetComponentsInChildren<GameEntry>(true));
                    }

                    foreach (var candidate in entries) {
                        candidate.MarkManaged();
                    }

                    if (entries.Count != 1) {
                        throw new InvalidOperationException(
                            $"Scene '{scene.path}' must contain exactly one GameEntry; found {entries.Count}.");
                    }

                    entry = entries[0];

                    if (!entry.isActiveAndEnabled) {
                        throw new InvalidOperationException(
                            $"Scene '{scene.path}': GameEntry must be enabled and active.");
                    }

                    entry.BindEnvironment(scope);
                } catch (Exception error) {
                    bindingError = error;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var handle = await provider.LoadAsync(definition.SceneLocation, cancellationToken, BindLoadedScene)
                         ?? throw new InvalidOperationException(
                             $"Scene provider '{provider.Id}' returned a null handle.");
            var content = new SceneContent(provider, handle, entry);

            try {
                if (bindingError != null) {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(bindingError).Throw();
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (!handle.Scene.IsValid() || !handle.Scene.isLoaded) {
                    throw new InvalidOperationException(
                        $"Scene provider '{provider.Id}' returned an invalid or unloaded scene.");
                }

                if (!entry || entry.gameObject.scene != handle.Scene) {
                    throw new InvalidOperationException(
                        $"Scene provider '{provider.Id}' did not bind the returned scene before Start.");
                }

                return content;
            } catch (Exception loadError) {
                try {
                    await content.UnloadAsync();
                } catch (Exception cleanupError) {
                    throw new GameContentLoadException(content, loadError, cleanupError);
                }

                throw;
            }
        }
    }
}