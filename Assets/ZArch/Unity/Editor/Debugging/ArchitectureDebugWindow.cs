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
        private MonoBehaviour[] m_Bootstraps = Array.Empty<MonoBehaviour>();
        private int m_SelectedBootstrap;
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

            if (m_Bootstraps.Length == 0) {
                DrawMessage("No ArchitectureBootstrap was found in the loaded scenes.", MessageType.Warning);
                return;
            }

            m_Info ??= CaptureSelected();

            if (!m_Info.IsStarted) {
                DrawMessage("The selected architecture has not started.", MessageType.Warning);
                return;
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawOverview();
            DrawEvents("Global Events", m_Info.Events, "architecture/events", 0, true);
            DrawScopes();
            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar() {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                if (Application.isPlaying && m_Bootstraps.Length > 0) {
                    var names = new string[m_Bootstraps.Length];

                    for (var i = 0; i < m_Bootstraps.Length; i++) {
                        names[i] = m_Bootstraps[i].name;
                    }

                    var selected = EditorGUILayout.Popup(
                        m_SelectedBootstrap,
                        names,
                        EditorStyles.toolbarPopup,
                        GUILayout.Width(160)
                    );

                    if (selected != m_SelectedBootstrap) {
                        m_SelectedBootstrap = selected;
                        m_Info = CaptureSelected();
                    }
                } else {
                    GUILayout.Label("Architecture", EditorStyles.miniLabel, GUILayout.Width(160));
                }

                GUILayout.FlexibleSpace();

                m_SearchText = GUILayout.TextField(
                    m_SearchText,
                    EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(120),
                    GUILayout.MaxWidth(260)
                );

                m_AutoRefresh = GUILayout.Toggle(
                    m_AutoRefresh,
                    new GUIContent("Auto", "Refresh every 0.5 seconds"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(45)
                );

                if (GUILayout.Button(
                        new GUIContent("Refresh", "Refresh now"),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(58)
                    )) {
                    Refresh();
                }
            }
        }

        private void DrawOverview() {
            var scopeCount = 0;
            var serviceCount = 0;
            var bindingCount = 0;
            var eventCount = m_Info.Events?.Length ?? 0;
            var subscriberCount = CountSubscribers(m_Info.Events);

            if (m_Info.Roots != null) {
                foreach (var root in m_Info.Roots) {
                    Accumulate(
                        root,
                        ref scopeCount,
                        ref serviceCount,
                        ref bindingCount,
                        ref eventCount,
                        ref subscriberCount
                    );
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                using (new EditorGUILayout.HorizontalScope()) {
                    DrawStatusDot(s_ActiveColor);
                    GUILayout.Label(ShortTypeName(m_Info.ArchitectureType), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Running", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(2);

                GUILayout.Label(
                    $"{scopeCount} scopes    {serviceCount} services    {bindingCount} bindings    "
                    + $"{eventCount} events    "
                    + $"{subscriberCount} subscribers",
                    EditorStyles.miniLabel
                );
            }

            EditorGUILayout.Space(4);
        }

        private void DrawScopes() {
            DrawSectionTitle("Scopes");

            if (m_Info.Roots == null || m_Info.Roots.Length == 0) {
                DrawEmptyRow("No scopes");
                return;
            }

            var visibleCount = 0;

            for (var i = 0; i < m_Info.Roots.Length; i++) {
                var root = m_Info.Roots[i];

                if (!MatchesScope(root)) {
                    continue;
                }

                visibleCount++;
                DrawScope(root, $"scope/{i}:{root.Name}", 0);
            }

            if (visibleCount == 0) {
                DrawEmptyRow("No matching scopes");
            }
        }

        private void DrawScope(ScopeDebugInfo scope, string key, int depth) {
            if (scope == null) {
                return;
            }

            var eventCount = scope.Events?.Length ?? 0;
            var serviceCount = scope.Services?.Length ?? 0;
            var bindingCount = scope.Bindings?.Length ?? 0;
            var childCount = scope.Children?.Length ?? 0;
            var expanded = GetFoldout(key, depth == 0) || HasSearch;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                GUILayout.Space(depth * 14f);
                expanded = DrawArrow(expanded);
                DrawStatusDot(scope.State == EScopeState.Active ? s_ActiveColor : s_InactiveColor);
                GUILayout.Label(scope.Name, EditorStyles.boldLabel);

                if (!string.IsNullOrEmpty(scope.BoundSceneName)) {
                    GUILayout.Label(scope.BoundSceneName, EditorStyles.miniLabel);
                } else if (!string.IsNullOrEmpty(scope.Tag)) {
                    GUILayout.Label(scope.Tag, EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label(
                    $"{eventCount} events   {serviceCount} services   {bindingCount} bindings   {childCount} children",
                    EditorStyles.miniLabel
                );

                GUILayout.Label(scope.State.ToString(), EditorStyles.miniLabel, GUILayout.Width(72));
            }

            m_Foldouts[key] = expanded;

            if (!expanded) {
                return;
            }

            if (CountMatchingEvents(scope.Events) > 0) {
                DrawEvents("Events", scope.Events, $"{key}/events", depth + 1, false);
            }

            if (CountMatchingServices(scope.Services) > 0) {
                DrawServices("Services", scope.Services, depth + 1, false);
            }

            if (CountMatchingServices(scope.Bindings) > 0) {
                DrawServices("Bindings", scope.Bindings, depth + 1, true);
            }

            if (scope.Children == null) {
                return;
            }

            for (var i = 0; i < scope.Children.Length; i++) {
                var child = scope.Children[i];

                if (MatchesScope(child)) {
                    DrawScope(child, $"{key}/{i}:{child.Name}", depth + 1);
                }
            }
        }

        private void DrawEvents(
            string label,
            EventSubscriptionDebugInfo[] events,
            string key,
            int depth,
            bool defaultExpanded
        ) {
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

                var subscribers = registration.Subscribers ?? Array.Empty<EventHandlerDebugInfo>();
                var eventKey = $"{key}/{registration.EventType}";
                var showHandlers = GetFoldout(eventKey, false) || HandlerMatchesSearch(registration);

                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space((depth + 1) * 14f);
                    showHandlers = DrawArrow(showHandlers);
                    GUILayout.Label(ShortTypeName(registration.EventType), GUILayout.MinWidth(120));
                    GUILayout.FlexibleSpace();

                    GUILayout.Label(
                        subscribers.Length == 1 ? "1 subscriber" : $"{subscribers.Length} subscribers",
                        EditorStyles.miniLabel,
                        GUILayout.Width(88)
                    );
                }

                m_Foldouts[eventKey] = showHandlers;

                if (!showHandlers) {
                    continue;
                }

                foreach (var subscriber in subscribers) {
                    if (HasSearch && !MatchesHandler(subscriber) && !Contains(registration.EventType, m_SearchText)) {
                        continue;
                    }

                    using (new EditorGUILayout.HorizontalScope()) {
                        GUILayout.Space((depth + 3) * 14f);
                        GUILayout.Label(FormatHandler(subscriber), EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void DrawServices(
            string label,
            ServiceRegistrationDebugInfo[] services,
            int depth,
            bool areBindings
        ) {
            var matchingServices = CountMatchingServices(services);
            DrawSectionTitle($"{label}  {matchingServices}", depth);

            if (matchingServices == 0) {
                DrawEmptyRow(HasSearch ? $"No matching {label.ToLowerInvariant()}" : $"No {label.ToLowerInvariant()}", depth + 1);
                return;
            }

            foreach (var service in services) {
                if (!MatchesService(service)) {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space((depth + 1) * 14f);
                    GUILayout.Label(ShortTypeName(service.ServiceType), GUILayout.MinWidth(120));
                    var implementation = ShortTypeName(service.ImplementationType);

                    if (service.IsCreated && implementation != ShortTypeName(service.ServiceType)) {
                        GUILayout.Label($"→ {implementation}", EditorStyles.miniLabel);
                    }

                    GUILayout.FlexibleSpace();

                    var state = areBindings
                        ? "Bound"
                        : service.IsCreated
                        ? service.IsInitialized ? "Ready" : "Pending"
                        : "Not created";

                    var status = areBindings ? state : $"{service.Lifetime} · {state}";
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
            var synchronous = FindObjectsByType<ArchitectureBootstrap>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            var asynchronous = FindObjectsByType<AsyncArchitectureBootstrap>(
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

        private bool GetFoldout(string key, bool defaultValue) => m_Foldouts.GetValueOrDefault(key, defaultValue);

        private bool HasSearch => !string.IsNullOrWhiteSpace(m_SearchText);

        private bool MatchesScope(ScopeDebugInfo scope) {
            if (!HasSearch || scope == null) {
                return scope != null;
            }

            if (Contains(scope.Name, m_SearchText)
                || Contains(scope.Tag, m_SearchText)
                || Contains(scope.BoundSceneName, m_SearchText)
                || CountMatchingEvents(scope.Events) > 0
                || CountMatchingServices(scope.Services) > 0
                || CountMatchingServices(scope.Bindings) > 0) {
                return true;
            }

            if (scope.Children == null) {
                return false;
            }

            foreach (var child in scope.Children) {
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
                || Contains(registration.EventType, m_SearchText)
                || HandlerMatchesSearch(registration));

        private bool HandlerMatchesSearch(EventSubscriptionDebugInfo registration) {
            if (!HasSearch || registration?.Subscribers == null) {
                return false;
            }

            foreach (var subscriber in registration.Subscribers) {
                if (MatchesHandler(subscriber)) {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesHandler(EventHandlerDebugInfo handler) =>
            handler != null
            && (Contains(handler.DeclaringType, m_SearchText)
                || Contains(handler.TargetType, m_SearchText)
                || Contains(handler.MethodName, m_SearchText));

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
                || Contains(service.ServiceType, m_SearchText)
                || Contains(service.ImplementationType, m_SearchText));

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
            var owner = ShortTypeName(handler.DeclaringType);
            var method = handler.MethodName;

            if (!string.IsNullOrEmpty(method) && method[0] == '<') {
                var end = method.IndexOf('>');

                if (end > 1) {
                    method = $"{method.Substring(1, end - 1)} (lambda)";
                }
            }

            return handler.IsStatic ? $"{owner}.{method} · static" : $"{owner}.{method}";
        }

        private static int CountSubscribers(EventSubscriptionDebugInfo[] events) {
            if (events == null) {
                return 0;
            }

            var count = 0;

            foreach (var registration in events) {
                count += registration?.Subscribers?.Length ?? 0;
            }

            return count;
        }

        private static void Accumulate(
            ScopeDebugInfo scope,
            ref int scopeCount,
            ref int serviceCount,
            ref int bindingCount,
            ref int eventCount,
            ref int subscriberCount
        ) {
            if (scope == null) {
                return;
            }

            scopeCount++;
            serviceCount += scope.Services?.Length ?? 0;
            bindingCount += scope.Bindings?.Length ?? 0;
            eventCount += scope.Events?.Length ?? 0;
            subscriberCount += CountSubscribers(scope.Events);

            if (scope.Children == null) {
                return;
            }

            foreach (var child in scope.Children) {
                Accumulate(
                    child,
                    ref scopeCount,
                    ref serviceCount,
                    ref bindingCount,
                    ref eventCount,
                    ref subscriberCount
                );
            }
        }
    }
}
