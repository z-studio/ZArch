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
using ZFramework.Unity;

namespace Demo.Editor {
    public static class SampleBuilder {
        public const string Root = "Assets/SessionDemo";
        public const string AppPath = Root + "/App/App.unity";

        [MenuItem("QFramework/Sessions/Create Lobby And Games Demo")]
        public static void Build() {
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("Save or close untitled scenes before generating the demo.");
            Directory.CreateDirectory(Root + "/App/Content");
            Directory.CreateDirectory(Root + "/GameA");
            Directory.CreateDirectory(Root + "/GameB");
            AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            EditorSceneManager.SaveScene(scene, AppPath);

            try {
                BuildGame<GameAEntry, GameADefinition>("GameA", entry => entry.PlayRound);
                BuildGame<GameBEntry, GameBDefinition>("GameB", entry => entry.PlayRound);

                BuildFoundation(out var resource);
                var root = new GameObject("App").AddComponent<AppEntry>();
                root.gameObject.AddComponent<SampleInput>();
                Set(root, "m_Resource", resource);
                var canvas = Canvas(root.transform, 0);
                var view = canvas.gameObject.AddComponent<AppView>();
                Set(root, "m_View", view);
                Set(view, "m_Status", Text(canvas, "Status", "Starting", -210, 245));
                Set(view, "m_Account", Text(canvas, "Account", "Signed out", -210, 185));
                Set(view, "m_Coins", Text(canvas, "Coins", "Coins: 0", -210, 130));
                var login = new GameObject("LoginPanel", typeof(RectTransform));
                login.transform.SetParent(canvas, false);
                Set(view, "m_LoginPanel", login);
                SetArray(view, "m_LoginButtons",
                    Button(login.transform, "LoginAlice", "Login as Alice", -210, 35, root.LoginAlice),
                    Button(login.transform, "LoginBob", "Login as Bob", -210, -40, root.LoginBob));
                var lobby = new GameObject("LobbyPanel", typeof(RectTransform));
                lobby.transform.SetParent(canvas, false);
                Set(view, "m_LobbyPanel", lobby);
                SetArray(view, "m_GameButtons",
                    Button(lobby.transform, "OpenGameA", "Open Game A", -210, 35, root.OpenGameA),
                    Button(lobby.transform, "OpenGameB", "Open Game B", -210, -40, root.OpenGameB));
                Set(view, "m_Return", Button(lobby.transform, "ReturnToLobby", "Return to lobby", -210, -115, root.ReturnToLobby));
                Set(view, "m_Logout", Button(canvas, "Logout", "Logout / Cancel login", -210, -200, root.Logout));
                Set(view, "m_Retry", Button(canvas, "RetryStartup", "Retry startup", 210, -200, root.RetryStartup));
                Text(canvas, "Hint", "App > Account > Game / ZFramework resources", 0, -270, 850);
                EditorSceneManager.SaveScene(scene, AppPath);
            } finally {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != AppPath).ToList();
            scenes.RemoveAll(item => item.path == Root + "/App/Lobby.unity");
            scenes.Add(new EditorBuildSettingsScene(AppPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            ConfigureCollectors();
            AssetDatabase.SaveAssets();
            Debug.Log($"Saved {AppPath} and App, login and two games. Open App and press Play.");
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
                SetString(definition, "m_SceneAddress", "");
                if (name == "GameB") {
                    // A 演示 Prefab 入口，B 演示 Additive 场景入口；生命周期和业务写法一致。
                    var active = SceneManager.GetActiveScene();
                    var gameScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    PrefabUtility.InstantiatePrefab(prefab, gameScene);
                    EditorSceneManager.SaveScene(gameScene, folder + "/GameBScene.unity");
                    EditorSceneManager.CloseScene(gameScene, true);
                    SceneManager.SetActiveScene(active);
                    Set(definition, "m_Prefab", null);
                    SetString(definition, "m_SceneAddress", "GameBScene");
                }
                File.WriteAllText(folder + "/GameConfig.txt", name + " dynamic resource");
                var marker = new GameObject("Marker");
                PrefabUtility.SaveAsPrefabAsset(marker, folder + "/Marker.prefab");
                UnityEngine.Object.DestroyImmediate(marker);
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }


        [MenuItem("QFramework/Sessions/Build Demo YooAsset Packages")]
        public static void BuildResourcePackages() {
            if (!File.Exists(AppPath))
                throw new InvalidOperationException("Generate the demo scene and resources first.");
            ConfigureCollectors();
            var version = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            foreach (var id in new[] { "app", "game-a", "game-b" }) {
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
            Debug.Log("Built App and two game YooAsset resource packages into StreamingAssets for the current target.");
        }

        private static void ConfigureCollectors() {
            Directory.CreateDirectory(Root + "/App/Content");
            File.WriteAllText(Root + "/App/Content/AppInfo.txt", "App common content");
            AssetDatabase.Refresh();
            if (!BundleCollectorSettingData.HasSettingAsset()) {
                Directory.CreateDirectory(Root + "/Editor");
                var setting = ScriptableObject.CreateInstance<BundleCollectorSetting>();
                AssetDatabase.CreateAsset(setting, Root + "/Editor/BundleCollectorSetting.asset");
            }
            foreach (var item in new[] { ("app", "App/Content"), ("game-a", "GameA"), ("game-b", "GameB") }) {
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
                    // Unity 不允许场景和普通资源放入同一个 AssetBundle。
                    PackRuleName = item.Item1 == "game-b" ? nameof(PackSeparately) : nameof(PackDirectory),
                    FilterRuleName = nameof(CollectAll)
                });
                BundleCollectorSettingData.ModifyPackage(package);
            }
            BundleCollectorSettingData.SaveFile();
        }


        private static void BuildFoundation(out ResourceComponent resource) {
            var foundation = new GameObject("ZFramework");
            var basic = new GameObject("Base"); basic.transform.SetParent(foundation.transform, false); basic.AddComponent<BaseComponent>();
            var pools = new GameObject("ObjectPool"); pools.transform.SetParent(foundation.transform, false); pools.AddComponent<ObjectPoolComponent>();
            var events = new GameObject("Event"); events.transform.SetParent(foundation.transform, false); events.AddComponent<EventComponent>();
            var fsm = new GameObject("Fsm"); fsm.transform.SetParent(foundation.transform, false); fsm.AddComponent<FsmComponent>();
            var scenes = new GameObject("Scene"); scenes.transform.SetParent(foundation.transform, false); scenes.AddComponent<SceneComponent>();
            var procedures = new GameObject("Procedure"); procedures.transform.SetParent(foundation.transform, false);
            var procedure = procedures.AddComponent<ProcedureComponent>();
            var serialized = new SerializedObject(procedure);
            var types = new[] { typeof(ProcedureLaunch), typeof(ProcedureLogin), typeof(ProcedureLobby), typeof(ProcedureGame) };
            var available = serialized.FindProperty("m_AvailableProcedureTypeNames");
            available.arraySize = types.Length;
            for (var i = 0; i < types.Length; i++) available.GetArrayElementAtIndex(i).stringValue = types[i].FullName;
            serialized.FindProperty("m_EntranceProcedureTypeName").stringValue = typeof(ProcedureLaunch).FullName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var assets = new GameObject("Resource"); assets.transform.SetParent(foundation.transform, false);
            resource = assets.AddComponent<ResourceComponent>();
            resource.PackageName = "app";
            resource.PlayMode = EPlayMode.EditorSimulateMode;
            resource.AutoUnloadBundleWhenUnused = true;
        }
        private static void SetArray(UnityEngine.Object owner, string field, params UnityEngine.Object[] values) {
            var serialized = new SerializedObject(owner);
            var array = serialized.FindProperty(field); array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
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

        private static void SetString(UnityEngine.Object owner, string field, string value) {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(UnityEngine.Object owner, string field, UnityEngine.Object value) {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
