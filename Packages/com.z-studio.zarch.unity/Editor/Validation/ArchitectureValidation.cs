using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZArch.Unity.Editor {
    /// <summary>进入 Play/构建前检查配置。预览场景只读打开，不修改用户当前场景。</summary>
    [InitializeOnLoad]
    public static class ArchitectureValidation {
        static ArchitectureValidation() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        private static void OnPlayModeChanged(PlayModeStateChange state) {
            if (state != PlayModeStateChange.ExitingEditMode) {
                return;
            }

            var errors = ValidateLoadedScenes();

            if (errors.Count == 0) {
                return;
            }

            EditorApplication.isPlaying = false;

            foreach (var error in errors) {
                Debug.LogError("ZArch configuration: " + error);
            }
        }

        [MenuItem("ZArch/Validate Loaded Scenes")]
        public static void ValidateMenu() {
            var errors = ValidateLoadedScenes();

            foreach (var error in errors) {
                Debug.LogError("ZArch configuration: " + error);
            }

            if (errors.Count == 0) {
                Debug.Log("ZArch: loaded scene configuration is valid.");
            }
        }

        public static List<string> ValidateLoadedScenes() {
            var entries = UnityEngine.Object
                .FindObjectsByType<ArchitectureEntry>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(entry =>
                    entry.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(entry.gameObject.scene))
                .ToArray();
            var errors = ValidateEntries(entries);
            var checkedDefinitions = new HashSet<GameDefinition>();

            foreach (var launcher in entries.OfType<GameLauncher>()) {
                var ids = new HashSet<string>(StringComparer.Ordinal);

                if (launcher.Definitions == null) {
                    errors.Add($"{launcher.name}: Games is null.");
                    continue;
                }

                for (var i = 0; i < launcher.Definitions.Count; i++) {
                    var definition = launcher.Definitions[i];

                    if (!definition) {
                        errors.Add($"{launcher.name}: Games[{i}] is missing.");
                        continue;
                    }

                    if (!ids.Add(definition.Id)) {
                        errors.Add($"{launcher.name}: duplicate game ID '{definition.Id}'.");
                    }

                    if (checkedDefinitions.Add(definition)) {
                        errors.AddRange(ValidateDefinition(definition));
                    }
                }
            }

            if (!entries.OfType<GameLauncher>().Any()) {
                foreach (var game in entries.OfType<GameEntry>()) {
                    if (!game.StandaloneGame) {
                        errors.Add($"{game.name}: assign Standalone Game before playing this scene directly.");
                    }
                }
            }

            return errors;
        }

        public static List<string> ValidateEntries(IEnumerable<ArchitectureEntry> entries) {
            var errors = new List<string>();
            var owners = new Dictionary<ArchitectureView, ArchitectureEntry>();

            foreach (var entry in entries) {
                try {
                    entry.ValidateViews();
                } catch (Exception error) {
                    errors.Add($"{entry.gameObject.scene.path}/{entry.name}: {error.Message}");
                }

                if (entry.Views == null) {
                    continue;
                }

                foreach (var view in entry.Views) {
                    if (!view) {
                        continue;
                    }

                    if (owners.TryGetValue(view, out var owner) && owner != entry) {
                        errors.Add(
                            $"View '{view.name}' is assigned to both '{owner.name}' and '{entry.name}'. Keep one owner.");
                    } else {
                        owners[view] = entry;
                    }
                }
            }

            foreach (var sceneEntries in entries.GroupBy(entry => entry.gameObject.scene)) {
                var games = sceneEntries.OfType<GameEntry>().ToArray();

                if (games.Length == 0) {
                    continue;
                }

                if (games.Length != 1) {
                    errors.Add($"Scene '{sceneEntries.Key.path}': keep exactly one GameEntry.");
                }

                if (sceneEntries.OfType<SceneArchitecture>().Any()) {
                    errors.Add(
                        $"Scene '{sceneEntries.Key.path}': GameEntry must not share its scene with SceneArchitecture/GameLauncher.");
                }

                if (games.Any(game => !game.isActiveAndEnabled)) {
                    errors.Add($"Scene '{sceneEntries.Key.path}': GameEntry must be enabled and active.");
                }
            }

            return errors;
        }

        public static List<string> ValidateDefinition(GameDefinition definition) {
            var errors = new List<string>();

            if (!definition) {
                errors.Add("Game definition is missing.");
                return errors;
            }

            try {
                definition.Validate();
            } catch (Exception error) {
                errors.Add(error.Message);
                return errors;
            }

            if (definition.SceneProviderId != GameSceneProviderIds.BuildSettings) {
                return errors;
            }

            var scenePath = EditorBuildSettings.scenes.FirstOrDefault(scene => scene.enabled &&
                                                                               (scene.path == definition.SceneLocation
                                                                                   || System.IO.Path
                                                                                       .GetFileNameWithoutExtension(
                                                                                           scene.path) == definition
                                                                                       .SceneLocation))?.path;

            if (scenePath == null) {
                errors.Add(
                    $"Game '{definition.Id}': add '{definition.SceneLocation}' to enabled Build Settings scenes.");
                return errors;
            }

            Scene scene = default;

            try {
                scene = EditorSceneManager.OpenPreviewScene(scenePath);
                var entries = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<ArchitectureEntry>(true)).ToArray();
                var games = entries.OfType<GameEntry>().ToArray();

                if (games.Length != 1) {
                    errors.Add(
                        $"Game '{definition.Id}', scene '{scenePath}': expected one GameEntry, found {games.Length}.");
                }

                if (entries.OfType<SceneArchitecture>().Any()) {
                    errors.Add(
                        $"Game '{definition.Id}': remove SceneArchitecture/GameLauncher from the game scene; use GameEntry.");
                }

                if (games.Any(entry => !entry.isActiveAndEnabled)) {
                    errors.Add($"Game '{definition.Id}': GameEntry must be enabled and active.");
                }

                errors.AddRange(ValidateEntries(entries));
            } catch (Exception error) {
                errors.Add($"Game '{definition.Id}': {error.Message}");
            } finally {
                if (scene.IsValid()) {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            return errors;
        }
    }

    public sealed class ArchitectureBuildValidation : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) {
            var errors = new List<string>();

            // 只验证构建引用的游戏定义；项目里未使用的配置资产不应阻塞构建。
            var definitions = new HashSet<GameDefinition>();

            foreach (var buildScene in EditorBuildSettings.scenes.Where(scene => scene.enabled)) {
                Scene preview = default;

                try {
                    preview = EditorSceneManager.OpenPreviewScene(buildScene.path);
                    var entries = preview.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<ArchitectureEntry>(true)).ToArray();
                    errors.AddRange(ArchitectureValidation.ValidateEntries(entries));

                    foreach (var launcher in entries.OfType<GameLauncher>()) {
                        var ids = new HashSet<string>(StringComparer.Ordinal);

                        if (launcher.Definitions == null) {
                            errors.Add($"{launcher.name}: Games is null.");
                            continue;
                        }

                        foreach (var game in launcher.Definitions) {
                            if (!game) {
                                errors.Add($"{launcher.name}: Games contains a missing definition.");
                                continue;
                            }

                            if (!ids.Add(game.Id)) {
                                errors.Add($"{launcher.name}: duplicate game ID '{game.Id}'.");
                            }

                            definitions.Add(game);
                        }
                    }
                } finally {
                    if (preview.IsValid()) {
                        EditorSceneManager.ClosePreviewScene(preview);
                    }
                }
            }

            foreach (var definition in definitions) {
                errors.AddRange(ArchitectureValidation.ValidateDefinition(definition));
            }

            errors.AddRange(ArchitectureValidation.ValidateLoadedScenes());

            if (errors.Count > 0) {
                throw new BuildFailedException("ZArch configuration:\n" + string.Join("\n", errors));
            }
        }
    }
}