using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZArch.Unity.Editor {
    public sealed class ArchitectureDebugWindow : EditorWindow {
        private const float k_RefreshInterval = 0.5f;
        private static readonly Color s_ActiveColor = new(0.25f, 0.72f, 0.38f);
        private static readonly Color s_InactiveColor = new(0.55f, 0.55f, 0.55f);

        private readonly Dictionary<string, bool> m_Foldouts = new();
        private Vector2 m_Scroll;
        private ArchitectureDebugInfo m_Info;
        private MonoBehaviour[] m_Entries = Array.Empty<MonoBehaviour>();
        private int m_SelectedEntry;
        private double m_NextRefresh;
        private string m_SearchText = string.Empty;
        private bool m_AutoRefresh = true;

        [MenuItem("Tools/ZArch/Debug Window")]
        public static void Open() => GetWindow<ArchitectureDebugWindow>("ZArch Debug");

        private void OnEnable() {
            EditorApplication.update += OnEditorUpdate;

            if (Application.isPlaying) {
                Refresh();
            }
        }

        private void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        private void OnEditorUpdate() {
            if (!Application.isPlaying || !m_AutoRefresh || EditorApplication.timeSinceStartup < m_NextRefresh) {
                return;
            }

            m_NextRefresh = EditorApplication.timeSinceStartup + k_RefreshInterval;
            Refresh();
            Repaint();
        }

        private void OnGUI() {
            DrawToolbar();

            if (!Application.isPlaying) {
                DrawMessage("Enter Play Mode to inspect the architecture.", MessageType.Info);
                return;
            }

            if (m_Entries.Length == 0) {
                DrawMessage("No architecture entry was found in the loaded scenes.", MessageType.Warning);
                return;
            }

            m_Info ??= CaptureSelected();

            if (!m_Info.isStarted) {
                DrawMessage("The selected architecture has not started.", MessageType.Warning);
                return;
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawOverview();
            DrawEvents("Global Events", m_Info.events, "architecture/events", 0, true);
            DrawScopes();
            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar() {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                if (Application.isPlaying && m_Entries.Length > 0) {
                    var names = new string[m_Entries.Length];

                    for (var i = 0; i < m_Entries.Length; i++) {
                        names[i] = m_Entries[i].name;
                    }

                    var selected = EditorGUILayout.Popup(m_SelectedEntry,
                        names,
                        EditorStyles.toolbarPopup,
                        GUILayout.Width(160));

                    if (selected != m_SelectedEntry) {
                        m_SelectedEntry = selected;
                        m_Info = CaptureSelected();
                    }
                } else {
                    GUILayout.Label("Architecture", EditorStyles.miniLabel, GUILayout.Width(160));
                }

                GUILayout.FlexibleSpace();

                m_SearchText = GUILayout.TextField(m_SearchText,
                    EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(120),
                    GUILayout.MaxWidth(260));

                m_AutoRefresh = GUILayout.Toggle(m_AutoRefresh,
                    new GUIContent("Auto", "Refresh every 0.5 seconds"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(45));

                if (GUILayout.Button(new GUIContent("Refresh", "Refresh now"),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(58))) {
                    Refresh();
                }
            }
        }

        private void DrawOverview() {
            var scopeCount = 0;
            var serviceCount = 0;
            var bindingCount = 0;
            var eventCount = m_Info.events?.Length ?? 0;
            var subscriberCount = CountSubscribers(m_Info.events);

            if (m_Info.roots != null) {
                foreach (var root in m_Info.roots) {
                    Accumulate(root,
                        ref scopeCount,
                        ref serviceCount,
                        ref bindingCount,
                        ref eventCount,
                        ref subscriberCount);
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                using (new EditorGUILayout.HorizontalScope()) {
                    DrawStatusDot(s_ActiveColor);
                    GUILayout.Label(ShortTypeName(m_Info.architectureType), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Running", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(2);

                GUILayout.Label($"{scopeCount} scopes    {serviceCount} services    {bindingCount} bindings    "
                                + $"{eventCount} events    "
                                + $"{subscriberCount} subscribers",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4);
        }

        private void DrawScopes() {
            DrawSectionTitle("Scopes");

            if (m_Info.roots == null || m_Info.roots.Length == 0) {
                DrawEmptyRow("No scopes");
                return;
            }

            var visibleCount = 0;

            for (var i = 0; i < m_Info.roots.Length; i++) {
                var root = m_Info.roots[i];

                if (!MatchesScope(root)) {
                    continue;
                }

                visibleCount++;
                DrawScope(root, $"scope/{i}:{root.name}", 0);
            }

            if (visibleCount == 0) {
                DrawEmptyRow("No matching scopes");
            }
        }

        private void DrawScope(ScopeDebugInfo scope, string key, int depth) {
            if (scope == null) {
                return;
            }

            var eventCount = scope.events?.Length ?? 0;
            var serviceCount = scope.services?.Length ?? 0;
            var bindingCount = scope.bindings?.Length ?? 0;
            var childCount = scope.children?.Length ?? 0;
            var expanded = GetFoldout(key, depth == 0) || HasSearch;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                GUILayout.Space(depth * 14f);
                expanded = DrawArrow(expanded);
                DrawStatusDot(scope.state == ScopeState.Active ? s_ActiveColor : s_InactiveColor);
                GUILayout.Label(scope.name, EditorStyles.boldLabel);

                if (!string.IsNullOrEmpty(scope.boundSceneName)) {
                    GUILayout.Label(scope.boundSceneName, EditorStyles.miniLabel);
                } else if (!string.IsNullOrEmpty(scope.tag)) {
                    GUILayout.Label(scope.tag, EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label(
                    $"{eventCount} events   {serviceCount} services   {bindingCount} bindings   {childCount} children",
                    EditorStyles.miniLabel);

                GUILayout.Label(scope.state.ToString(), EditorStyles.miniLabel, GUILayout.Width(72));
            }

            m_Foldouts[key] = expanded;

            if (!expanded) {
                return;
            }

            if (CountMatchingEvents(scope.events) > 0) {
                DrawEvents("Events", scope.events, $"{key}/events", depth + 1, false);
            }

            if (CountMatchingServices(scope.services) > 0) {
                DrawServices("Services", scope.services, depth + 1, false);
            }

            if (CountMatchingServices(scope.bindings) > 0) {
                DrawServices("Bindings", scope.bindings, depth + 1, true);
            }

            if (scope.children == null) {
                return;
            }

            for (var i = 0; i < scope.children.Length; i++) {
                var child = scope.children[i];

                if (MatchesScope(child)) {
                    DrawScope(child, $"{key}/{i}:{child.name}", depth + 1);
                }
            }
        }

        private void DrawEvents(string label,
            EventSubscriptionDebugInfo[] events,
            string key,
            int depth,
            bool defaultExpanded) {
            var matchingEvents = CountMatchingEvents(events);
            var expanded = GetFoldout(key, defaultExpanded) || HasSearch;
            DrawFoldoutTitle(label, matchingEvents, depth, ref expanded);
            m_Foldouts[key] = expanded;

            if (!expanded) {
                return;
            }

            if (matchingEvents == 0) {
                DrawEmptyRow(HasSearch ? "No matching events" : "No subscriptions", depth + 1);
                return;
            }

            foreach (var registration in events) {
                if (!MatchesEvent(registration)) {
                    continue;
                }

                var subscribers = registration.subscribers ?? Array.Empty<EventHandlerDebugInfo>();
                var eventKey = $"{key}/{registration.eventType}";
                var showHandlers = GetFoldout(eventKey, false) || HandlerMatchesSearch(registration);

                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space((depth + 1) * 14f);
                    showHandlers = DrawArrow(showHandlers);
                    GUILayout.Label(ShortTypeName(registration.eventType), GUILayout.MinWidth(120));
                    GUILayout.FlexibleSpace();

                    GUILayout.Label(subscribers.Length == 1 ? "1 subscriber" : $"{subscribers.Length} subscribers",
                        EditorStyles.miniLabel,
                        GUILayout.Width(88));
                }

                m_Foldouts[eventKey] = showHandlers;

                if (!showHandlers) {
                    continue;
                }

                foreach (var subscriber in subscribers) {
                    if (HasSearch && !MatchesHandler(subscriber) && !Contains(registration.eventType, m_SearchText)) {
                        continue;
                    }

                    using (new EditorGUILayout.HorizontalScope()) {
                        GUILayout.Space((depth + 3) * 14f);
                        GUILayout.Label(FormatHandler(subscriber), EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void DrawServices(string label,
            ServiceRegistrationDebugInfo[] services,
            int depth,
            bool areBindings) {
            var matchingServices = CountMatchingServices(services);
            DrawSectionTitle($"{label}  {matchingServices}", depth);

            if (matchingServices == 0) {
                DrawEmptyRow(HasSearch ? $"No matching {label.ToLowerInvariant()}" : $"No {label.ToLowerInvariant()}",
                    depth + 1);
                return;
            }

            foreach (var service in services) {
                if (!MatchesService(service)) {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space((depth + 1) * 14f);
                    GUILayout.Label(ShortTypeName(service.serviceType), GUILayout.MinWidth(120));
                    var implementation = ShortTypeName(service.implementationType);

                    if (service.isCreated && implementation != ShortTypeName(service.serviceType)) {
                        GUILayout.Label($"→ {implementation}", EditorStyles.miniLabel);
                    }

                    GUILayout.FlexibleSpace();

                    var state = areBindings
                        ? "Bound"
                        : service.isCreated
                            ? service.isInitialized ? "Ready" : "Pending"
                            : "Not created";

                    var status = areBindings ? state : $"{service.lifetime} · {state}";
                    GUILayout.Label(status, EditorStyles.miniLabel, GUILayout.Width(112));
                }
            }
        }

        private static void DrawSectionTitle(string title, int depth = 0) {
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(depth * 14f);
                GUILayout.Label(title, EditorStyles.boldLabel);
            }
        }

        private static void DrawFoldoutTitle(string title, int count, int depth, ref bool expanded) {
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(depth * 14f);
                expanded = EditorGUILayout.Foldout(expanded, $"{title}  {count}", true, EditorStyles.foldoutHeader);
            }
        }

        private static void DrawEmptyRow(string message, int depth = 0) {
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(depth * 14f);
                GUILayout.Label(message, EditorStyles.centeredGreyMiniLabel);
            }
        }

        private static void DrawMessage(string message, MessageType type) {
            GUILayout.Space(8);
            EditorGUILayout.HelpBox(message, type);
        }

        private static void DrawStatusDot(Color color) {
            var rect = GUILayoutUtility.GetRect(10, 16, GUILayout.Width(10));
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 5, 6, 6), color);
        }

        private static bool DrawArrow(bool expanded) {
            var rect = GUILayoutUtility.GetRect(14, EditorGUIUtility.singleLineHeight, GUILayout.Width(14));
            return EditorGUI.Foldout(rect, expanded, GUIContent.none, true);
        }

        private void Refresh() {
            m_Entries = FindObjectsByType<ArchitectureEntry>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (m_SelectedEntry >= m_Entries.Length) {
                m_SelectedEntry = 0;
            }

            m_Info = CaptureSelected();
        }

        private ArchitectureDebugInfo CaptureSelected() {
            if (m_Entries.Length == 0) {
                return new ArchitectureDebugInfo();
            }

            var entry = m_Entries[m_SelectedEntry] as ArchitectureEntry;
            var architecture = entry?.Scope?.Architecture;

            return ArchitectureDebug.Capture(architecture);
        }

        private bool GetFoldout(string key, bool defaultValue) => m_Foldouts.GetValueOrDefault(key, defaultValue);

        private bool HasSearch => !string.IsNullOrWhiteSpace(m_SearchText);

        private bool MatchesScope(ScopeDebugInfo scope) {
            if (!HasSearch || scope == null) {
                return scope != null;
            }

            if (Contains(scope.name, m_SearchText)
                || Contains(scope.tag, m_SearchText)
                || Contains(scope.boundSceneName, m_SearchText)
                || CountMatchingEvents(scope.events) > 0
                || CountMatchingServices(scope.services) > 0
                || CountMatchingServices(scope.bindings) > 0) {
                return true;
            }

            if (scope.children == null) {
                return false;
            }

            foreach (var child in scope.children) {
                if (MatchesScope(child)) {
                    return true;
                }
            }

            return false;
        }

        private int CountMatchingEvents(EventSubscriptionDebugInfo[] events) {
            if (events == null) {
                return 0;
            }

            var count = 0;

            foreach (var registration in events) {
                if (MatchesEvent(registration)) {
                    count++;
                }
            }

            return count;
        }

        private bool MatchesEvent(EventSubscriptionDebugInfo registration) =>
            registration != null
            && (!HasSearch
                || Contains(registration.eventType, m_SearchText)
                || HandlerMatchesSearch(registration));

        private bool HandlerMatchesSearch(EventSubscriptionDebugInfo registration) {
            if (!HasSearch || registration?.subscribers == null) {
                return false;
            }

            foreach (var subscriber in registration.subscribers) {
                if (MatchesHandler(subscriber)) {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesHandler(EventHandlerDebugInfo handler) =>
            handler != null
            && (Contains(handler.declaringType, m_SearchText)
                || Contains(handler.targetType, m_SearchText)
                || Contains(handler.methodName, m_SearchText));

        private int CountMatchingServices(ServiceRegistrationDebugInfo[] services) {
            if (services == null) {
                return 0;
            }

            var count = 0;

            foreach (var service in services) {
                if (MatchesService(service)) {
                    count++;
                }
            }

            return count;
        }

        private bool MatchesService(ServiceRegistrationDebugInfo service) =>
            service != null
            && (!HasSearch
                || Contains(service.serviceType, m_SearchText)
                || Contains(service.implementationType, m_SearchText));

        private static bool Contains(string value, string search) =>
            !string.IsNullOrEmpty(value)
            && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string ShortTypeName(string fullName) {
            if (string.IsNullOrEmpty(fullName)) {
                return "—";
            }

            var genericArgumentsIndex = fullName.IndexOf("[[", StringComparison.Ordinal);
            var typeName = genericArgumentsIndex >= 0 ? fullName.Substring(0, genericArgumentsIndex) : fullName;
            var separatorIndex = Math.Max(typeName.LastIndexOf('+'), typeName.LastIndexOf('.'));
            var name = separatorIndex >= 0 ? typeName.Substring(separatorIndex + 1) : typeName;
            var genericIndex = name.IndexOf('`');
            return genericIndex >= 0 ? name.Substring(0, genericIndex) : name;
        }

        private static string FormatHandler(EventHandlerDebugInfo handler) {
            var owner = ShortTypeName(handler.declaringType);
            var method = handler.methodName;

            if (!string.IsNullOrEmpty(method) && method[0] == '<') {
                var end = method.IndexOf('>');

                if (end > 1) {
                    method = $"{method.Substring(1, end - 1)} (lambda)";
                }
            }

            return handler.isStatic ? $"{owner}.{method} · static" : $"{owner}.{method}";
        }

        private static int CountSubscribers(EventSubscriptionDebugInfo[] events) {
            if (events == null) {
                return 0;
            }

            var count = 0;

            foreach (var registration in events) {
                count += registration?.subscribers?.Length ?? 0;
            }

            return count;
        }

        private static void Accumulate(ScopeDebugInfo scope,
            ref int scopeCount,
            ref int serviceCount,
            ref int bindingCount,
            ref int eventCount,
            ref int subscriberCount) {
            if (scope == null) {
                return;
            }

            scopeCount++;
            serviceCount += scope.services?.Length ?? 0;
            bindingCount += scope.bindings?.Length ?? 0;
            eventCount += scope.events?.Length ?? 0;
            subscriberCount += CountSubscribers(scope.events);

            if (scope.children == null) {
                return;
            }

            foreach (var child in scope.children) {
                Accumulate(child,
                    ref scopeCount,
                    ref serviceCount,
                    ref bindingCount,
                    ref eventCount,
                    ref subscriberCount);
            }
        }
    }
}