using UnityEditor;
using UnityEngine;

namespace ZArch.Unity.Editor {
    public sealed class ArchitectureDebugWindow : EditorWindow {
        private Vector2 m_Scroll;
        private ArchitectureDebugInfo m_Info;
        private MonoBehaviour[] m_Bootstraps = System.Array.Empty<MonoBehaviour>();
        private int m_SelectedBootstrap;
        private double m_NextRefresh;

        [MenuItem("Tools/ZArch/Architecture Debug")]
        public static void Open() => GetWindow<ArchitectureDebugWindow>("Architecture Debug");

        private void OnEnable() => EditorApplication.update += OnEditorUpdate;

        private void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        private void OnEditorUpdate() {
            if (!Application.isPlaying) {
                return;
            }

            if (EditorApplication.timeSinceStartup < m_NextRefresh) {
                return;
            }

            m_NextRefresh = EditorApplication.timeSinceStartup + 0.5d;
            Refresh();
            Repaint();
        }

        private void OnGUI() {
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Refresh", GUILayout.Width(80))) {
                    Refresh();
                }

                GUILayout.Label(Application.isPlaying ? "Playing" : "Edit Mode", EditorStyles.miniLabel);
            }

            if (!Application.isPlaying) {
                EditorGUILayout.HelpBox("进入 Play Mode 后查看 Scope 树。", MessageType.Info);
                return;
            }

            m_Info ??= CaptureSelected();

            if (m_Bootstraps.Length == 0) {
                EditorGUILayout.HelpBox("没有找到 ArchitectureBootstrap。", MessageType.Warning);
                return;
            }

            var names = new string[m_Bootstraps.Length];

            for (var i = 0; i < m_Bootstraps.Length; i++) {
                names[i] = m_Bootstraps[i].name;
            }

            var selected = EditorGUILayout.Popup("Bootstrap", m_SelectedBootstrap, names);

            if (selected != m_SelectedBootstrap) {
                m_SelectedBootstrap = selected;
                m_Info = CaptureSelected();
            }

            if (!m_Info.IsStarted) {
                EditorGUILayout.HelpBox("Architecture 未启动。", MessageType.Warning);
                return;
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.LabelField("Architecture", m_Info.ArchitectureType);
            EditorGUILayout.Space(6);

            if (m_Info.Roots == null || m_Info.Roots.Length == 0) {
                EditorGUILayout.LabelField("(no scopes)");
            } else {
                foreach (var root in m_Info.Roots) {
                    DrawScope(root, 0);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void Refresh() {
            var synchronous = Object.FindObjectsByType<ArchitectureBootstrap>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
            var asynchronous = Object.FindObjectsByType<AsyncArchitectureBootstrap>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
            m_Bootstraps = new MonoBehaviour[synchronous.Length + asynchronous.Length];
            synchronous.CopyTo(m_Bootstraps, 0);
            asynchronous.CopyTo(m_Bootstraps, synchronous.Length);

            if (m_SelectedBootstrap >= m_Bootstraps.Length) {
                m_SelectedBootstrap = 0;
            }

            m_Info = CaptureSelected();
        }

        private ArchitectureDebugInfo CaptureSelected() {
            if (m_Bootstraps.Length == 0) {
                return new ArchitectureDebugInfo();
            }

            var architecture = m_Bootstraps[m_SelectedBootstrap] switch {
                ArchitectureBootstrap synchronous => synchronous.Architecture,
                AsyncArchitectureBootstrap asynchronous => asynchronous.Architecture,
                _ => null
            };
            return ArchitectureDebug.Capture(architecture);
        }

        private static void DrawScope(ScopeDebugInfo scope, int depth) {
            if (scope == null) {
                return;
            }

            var previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = depth;
            EditorGUILayout.LabelField(scope.Name, EditorStyles.boldLabel);
            EditorGUI.indentLevel = depth + 1;
            EditorGUILayout.LabelField("State", scope.State.ToString());

            if (!string.IsNullOrEmpty(scope.Tag))
                EditorGUILayout.LabelField("Tag", scope.Tag);

            if (!string.IsNullOrEmpty(scope.BoundSceneName)) {
                EditorGUILayout.LabelField("Scene", scope.BoundSceneName);
            }

            EditorGUILayout.LabelField("Services");

            if (scope.Services == null || scope.Services.Length == 0) {
                EditorGUI.indentLevel = depth + 2;
                EditorGUILayout.LabelField("(empty)");
            } else {
                foreach (var service in scope.Services) {
                    EditorGUI.indentLevel = depth + 2;
                    var implementation = service.IsCreated ? service.ImplementationType : "(not created)";
                    var flags = $"{service.Lifetime}, {(service.IsInitialized ? "initialized" : "pending")}";
                    EditorGUILayout.LabelField(service.ServiceType, implementation);
                    EditorGUI.indentLevel = depth + 3;
                    EditorGUILayout.LabelField(flags, EditorStyles.miniLabel);
                }
            }

            EditorGUI.indentLevel = previousIndent;
            EditorGUILayout.Space(6);

            if (scope.Children == null) {
                return;
            }

            foreach (var child in scope.Children) {
                DrawScope(child, depth + 1);
            }
        }
    }
}
