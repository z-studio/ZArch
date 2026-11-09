using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UIText = UnityEngine.UI.Text;
using UIButton = UnityEngine.UI.Button;
using UIImage = UnityEngine.UI.Image;

namespace ZArch.Unity.Samples.Editor {
    /// <summary>只在编辑器生成并保存示例；运行时不动态构建界面。</summary>
    public static class SampleSceneBuilder {
        private static string Root =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SampleSceneBuilder).Assembly) == null
                ? "Assets/ZArch/Samples"
                : "Assets/ZArchSamples";

        private static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        [MenuItem("ZArch/Samples/Rebuild Sample Scenes")]
        public static void Build() {
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                throw new InvalidOperationException("Save or open a scene before generating ZArch samples.");
            foreach (var folder in new[] { "Counter", "Inventory", "Games" })
                System.IO.Directory.CreateDirectory($"{Root}/{folder}");
            AssetDatabase.Refresh();
            BuildCounter();
            BuildInventory();
            var first = Definition("GameA", "game-a");
            var second = Definition("GameB", "game-b");
            BuildGame("GameA", first);
            BuildGame("GameB", second);
            BuildLobby(first, second);
            var paths = new[] { "Counter/Counter", "Inventory/Inventory", "Games/Lobby", "Games/GameA", "Games/GameB" }
                .Select(path => $"{Root}/{path}.unity");
            var existing = EditorBuildSettings.scenes.ToList();

            foreach (var path in paths) {
                if (!existing.Any(scene => scene.path == path)) existing.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = existing.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("ZArch: saved Counter, Inventory, Lobby, GameA and GameB sample scenes.");
        }

        private static void InScene(string path, Action create) {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try {
                create();
                EditorSceneManager.SaveScene(scene, path);
            } finally {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
        }

        private static RectTransform Canvas(Transform parent, string name = "Canvas") {
            var camera = new GameObject("BackgroundCamera").AddComponent<Camera>();
            camera.transform.SetParent(parent, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.06f, 0.1f);
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            go.transform.SetParent(parent, false);
            go.GetComponent<UnityEngine.Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 640);
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        private static void Input(Transform parent) {
            var go = new GameObject("Input", typeof(SampleInput));
            go.transform.SetParent(parent, false);
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

        private static UIText Text(Transform parent, string name, string value, float x, float y, float width = 400) {
            var text = Rect(parent, name, x, y, width, 50).gameObject.AddComponent<UIText>();
            text.font = Font;
            text.fontSize = 25;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.9f, 0.94f, 1);
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static UIButton Button(Transform parent, string name, string label, float x, float y,
            UnityAction action) {
            var rect = Rect(parent, name, x, y, 290, 52);
            var image = rect.gameObject.AddComponent<UIImage>();
            image.color = new Color(0.15f, 0.32f, 0.57f);
            var button = rect.gameObject.AddComponent<UIButton>();
            button.targetGraphic = image;
            UnityEventTools.AddPersistentListener(button.onClick, action);
            Text(rect, "Label", label, 0, 0, 280);
            return button;
        }

        private static void Set(UnityEngine.Object owner, string field, UnityEngine.Object value) {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(UnityEngine.Object owner, string field, params UnityEngine.Object[] values) {
            var serialized = new SerializedObject(owner);
            var property = serialized.FindProperty(field);
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildCounter() =>
            InScene($"{Root}/Counter/Counter.unity", () => {
                var root = new GameObject("CounterEntry").AddComponent<CounterEntry>();
                var canvas = Canvas(root.transform);
                Input(root.transform);
                Text(canvas, "Title", "01 / Counter", 0, 190);
                Text(canvas, "Flow", "Button > System > Model > View", 0, 125, 650);
                var view = canvas.gameObject.AddComponent<CounterView>();
                Set(view, "m_Count", Text(canvas, "Count", "0", 0, 25));
                Button(canvas, "IncreaseButton", "Increase", 0, -70, view.Increase);
                SetArray(root, "m_Views", view);
            });

        private static void BuildInventory() =>
            InScene($"{Root}/Inventory/Inventory.unity", () => {
                var root = new GameObject("InventoryEntry").AddComponent<InventoryEntry>();
                var canvas = Canvas(root.transform);
                Input(root.transform);
                Text(canvas, "Title", "02 / Shared Inventory", 0, 230);
                var first = Rect(canvas, "Toolbar", -220, 70, 430, 220).gameObject.AddComponent<InventoryView>();
                var second = Rect(canvas, "BagPanel", 220, 70, 430, 220).gameObject.AddComponent<InventoryView>();
                Set(first, "m_Count", Text(first.transform, "Count", "Apples: 0", 0, 45));
                Set(second, "m_Count", Text(second.transform, "Count", "Apples: 0", 0, 45));
                Set(first, "m_Panel", second.gameObject);
                Set(second, "m_Panel", second.gameObject);
                Button(first.transform, "AddAppleButton", "Add Apple", 0, -30, first.AddApple);
                Button(second.transform, "AddAppleButton", "Add Apple", 0, -30, second.AddApple);
                Button(canvas, "TogglePanelButton", "Show / Hide Bag", 0, -140, first.TogglePanel);
                Text(canvas, "Hint", "Both views read the same Model", 0, -220, 650);
                SetArray(root, "m_Views", first, second);
            });

        private static RoundGameDefinition Definition(string name, string id) {
            var path = $"{Root}/Games/{name}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<RoundGameDefinition>(path);

            if (!definition) {
                definition = ScriptableObject.CreateInstance<RoundGameDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("m_Id").stringValue = id;
            serialized.FindProperty("m_SceneLocation").stringValue = name;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private static void BuildGame(string name, GameDefinition definition) =>
            InScene($"{Root}/Games/{name}.unity", () => {
                var entry = new GameObject("GameEntry").AddComponent<RoundGameEntry>();
                var canvas = Canvas(entry.transform);
                Text(canvas, "Title", name, 220, 190);
                var view = Rect(canvas, "RoundView", 220, 0, 420, 300).gameObject.AddComponent<RoundView>();
                Set(view, "m_Rounds", Text(view.transform, "Rounds", "Rounds: 0", 0, 80));
                Set(view, "m_Coins", Text(view.transform, "Coins", "Coins: 100", 0, 20));
                Button(view.transform, "PlayRoundButton", "Play (+10 coins)", 0, -60, view.PlayRound);
                Input(entry.transform);
                Set(entry, "m_StandaloneGame", definition);
                SetArray(entry, "m_Views", view);
            });

        private static void BuildLobby(params GameDefinition[] games) =>
            InScene($"{Root}/Games/Lobby.unity", () => {
                var entry = new GameObject("LobbyEntry").AddComponent<LobbyEntry>();
                var canvas = Canvas(entry.transform);
                Input(entry.transform);
                var view = canvas.gameObject.AddComponent<LobbyView>();
                Set(view, "m_Launcher", entry);
                Text(canvas, "Title", "03 / Lobby", -220, 235);
                Set(view, "m_Coins", Text(canvas, "Coins", "Shared coins: 100", -220, 175));
                Set(view, "m_Status", Text(canvas, "Status", "Lobby", -220, 115));
                Button(canvas, "OpenGameAButton", "Open Game A", -220, 35, view.OpenGameA);
                Button(canvas, "OpenGameBButton", "Open Game B", -220, -40, view.OpenGameB);
                Button(canvas, "ReturnButton", "Return to Lobby", -220, -115, view.ReturnToLobby);
                Text(canvas, "Hint", "Coins persist / Rounds reset", 0, -245, 800);
                SetArray(entry, "m_Games", games);
                SetArray(entry, "m_Views", view);
            });
    }
}