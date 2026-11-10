using System;
using System.IO;
using System.Linq;
using Demo.App;
using Demo.GameA;
using Demo.GameB;
using QFramework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using YooAsset;
using YooAsset.Editor;

namespace Demo.Editor {
    public static class SampleBuilder {
        public const string Root = "Assets/SessionDemo";
        public const string LobbyPath = Root + "/App/Lobby.unity";

        [MenuItem("QFramework/Sessions/Create Lobby And Games Demo")]
        public static void Build() {
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("Save or close untitled scenes before generating the demo.");
            Directory.CreateDirectory(Root + "/App");
            Directory.CreateDirectory(Root + "/GameA");
            Directory.CreateDirectory(Root + "/GameB");
            AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try {
                BuildGame<GameAEntry, GameADefinition>("GameA", entry => entry.PlayRound);
                BuildGame<GameBEntry, GameBDefinition>("GameB", entry => entry.PlayRound);
                var root = new GameObject("App").AddComponent<LobbyEntry>();
                root.gameObject.AddComponent<SampleInput>();
                var canvas = Canvas(root.transform, 0);
                Text(canvas, "Title", "QFramework + Sessions", -210, 230, 440);
                Set(root, "m_Coins", Text(canvas, "Coins", "App coins: 100", -210, 160));
                Set(root, "m_Status", Text(canvas, "Status", "Lobby", -210, 100));
                var buttons = new[] {
                    Button(canvas, "OpenGameA", "Open Game A", -210, 25, root.OpenGameA),
                    Button(canvas, "OpenGameB", "Open Game B", -210, -45, root.OpenGameB),
                    Button(canvas, "ReturnToLobby", "Return to lobby", -210, -115, root.ReturnToLobby)
                };
                var serialized = new SerializedObject(root);
                var array = serialized.FindProperty("m_Buttons");
                array.arraySize = buttons.Length;
                for (var i = 0; i < buttons.Length; i++)
                    array.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
                var games = serialized.FindProperty("m_Games");
                games.arraySize = 2;

                for (var i = 0; i < 2; i++) {
                    var game = games.GetArrayElementAtIndex(i);
                    game.FindPropertyRelative("id").stringValue = i == 0 ? "game-a" : "game-b";
                    game.FindPropertyRelative("entryAddress").stringValue =
                        "Entry";
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                Text(canvas, "Hint", "Shared wallet / Fresh rounds on re-entry", 0, -230, 850);
                EditorSceneManager.SaveScene(scene, LobbyPath);
            } finally {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != LobbyPath).ToList();
            scenes.Add(new EditorBuildSettingsScene(LobbyPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            ConfigureCollectors();
            AssetDatabase.SaveAssets();
            Debug.Log($"Saved {LobbyPath} and two game resource packages. Open Lobby and press Play.");
        }

        private static void BuildGame<TEntry, TDefinition>(string name, Func<TEntry, UnityAction> play)
            where TEntry : GameEntry where TDefinition : GameDefinition {
            var root = new GameObject(name);
            root.SetActive(false);

            try {
                var entry = root.AddComponent<TEntry>();
                var canvas = Canvas(root.transform, 1);
                Text(canvas, "Title", name, 220, 190);
                Set(entry, "m_Rounds", Text(canvas, "Rounds", "Rounds: 0", 220, 90));
                Button(canvas, "PlayRound", "Play round", 220, -10, play(entry));
                var folder = $"{Root}/{name}";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Game.prefab");
                var path = folder + "/Entry.asset";
                var definition = AssetDatabase.LoadAssetAtPath<TDefinition>(path);

                if (!definition) {
                    definition = ScriptableObject.CreateInstance<TDefinition>();
                    AssetDatabase.CreateAsset(definition, path);
                }

                Set(definition, "m_Prefab", prefab.GetComponent<TEntry>());
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }


        [MenuItem("QFramework/Sessions/Build Demo YooAsset Packages")]
        public static void BuildResourcePackages() {
            if (!File.Exists(LobbyPath))
                throw new InvalidOperationException("Generate the demo scene and resources first.");
            ConfigureCollectors();
            var version = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            foreach (var id in new[] { "game-a", "game-b" }) {
                var parameters = new ScriptableBuildParameters {
                    BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                    BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                    BuildPipeline = nameof(ScriptableBuildPipeline),
                    BuildBundleType = (int)EBundleType.AssetBundle,
                    BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                    PackageName = id,
                    PackageVersion = version,
                    VerifyBuildingResult = true,
                    CompressOption = ECompressOption.LZ4,
                    BuiltinShadersBundleName = id + ".shaders",
                    BundledCopyOption = EBundledCopyOption.ClearAndCopyAll
                };
                var result = new ScriptableBuildPipeline().Run(parameters, true);
                if (!result.Success) throw new InvalidOperationException($"{id}: {result.ErrorInfo}");
                // 资源包内才出现的入口/组件类型，也需要保留到 Player 中。
                var linker = Path.Combine(result.OutputPackageDirectory, "link.xml");
                if (!File.Exists(linker)) throw new InvalidOperationException($"{id}: YooAsset did not generate link.xml.");
                var linkerFolder = $"{Root}/Editor/Linker/{id}";
                Directory.CreateDirectory(linkerFolder);
                File.Copy(linker, linkerFolder + "/link.xml", true);
            }
            AssetDatabase.Refresh();
            Debug.Log("Built two YooAsset resource packages into StreamingAssets for the current target.");
        }

        private static void ConfigureCollectors() {
            if (!BundleCollectorSettingData.HasSettingAsset()) {
                Directory.CreateDirectory(Root + "/Editor");
                var setting = ScriptableObject.CreateInstance<BundleCollectorSetting>();
                AssetDatabase.CreateAsset(setting, Root + "/Editor/BundleCollectorSetting.asset");
            }
            foreach (var item in new[] { ("game-a", "GameA"), ("game-b", "GameB") }) {
                var package = BundleCollectorSettingData.Setting.Packages.FirstOrDefault(p => p.PackageName == item.Item1);
                if (package == null) package = BundleCollectorSettingData.CreatePackage(item.Item1);
                package.EnableAddressable = true;
                // 只更新本示例命名的分组，保留已有的其他分组与资源包。
                var group = package.Groups.FirstOrDefault(g => g.GroupName == "SessionDemo");
                if (group == null) group = BundleCollectorSettingData.CreateGroup(package, "SessionDemo");
                group.Collectors.Clear();
                var folder = $"{Root}/{item.Item2}";
                BundleCollectorSettingData.CreateCollector(group, new BundleCollector {
                    CollectPath = folder,
                    CollectorGUID = AssetDatabase.AssetPathToGUID(folder),
                    CollectorType = ECollectorType.MainAssetCollector,
                    AddressRuleName = nameof(AddressByFileName),
                    PackRuleName = nameof(PackDirectory),
                    FilterRuleName = nameof(CollectAll)
                });
                BundleCollectorSettingData.ModifyPackage(package);
            }
            BundleCollectorSettingData.SaveFile();
        }

        private static Transform Canvas(Transform parent, int order) {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 640);
            scaler.matchWidthOrHeight = 0.5f;
            return go.transform;
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height) {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }

        private static UnityEngine.UI.Text Text(Transform parent, string name, string value, float x, float y,
            float width = 410) {
            var text = Rect(parent, name, x, y, width, 50).gameObject.AddComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static UnityEngine.UI.Button Button(Transform parent, string name, string label, float x, float y,
            UnityAction action) {
            var go = Rect(parent, name, x, y, 290, 52).gameObject;
            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.12f, 0.28f, 0.5f);
            var button = go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            UnityEventTools.AddPersistentListener(button.onClick, action);
            Text(go.transform, "Label", label, 0, 0, 280);
            return button;
        }

        private static void Set(UnityEngine.Object owner, string field, UnityEngine.Object value) {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}