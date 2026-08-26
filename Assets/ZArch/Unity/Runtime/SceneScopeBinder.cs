using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace ZArch {
    public sealed class SceneScopeBinder : IDisposable {
        private sealed class Binding {
            public Action<ArchitectureScope> Setup;
            public Func<Scene, ArchitectureScope> ParentSelector;
        }

        private readonly Architecture m_Architecture;
        private readonly Dictionary<string, Binding> m_Bindings = new(StringComparer.Ordinal);
        private readonly Dictionary<int, ArchitectureScope> m_SceneScopes = new();
        private bool m_IsDisposed;

        public bool IsEnabled { get; private set; }

        public SceneScopeBinder(Architecture architecture) {
            m_Architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
        }

        public void Bind(
            string sceneNameOrPath,
            Action<ArchitectureScope> setup,
            Func<Scene, ArchitectureScope> parentSelector = null
        ) {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(sceneNameOrPath)) {
                throw new ArgumentException("Scene name or path is empty.", nameof(sceneNameOrPath));
            }

            m_Bindings[sceneNameOrPath] = new Binding {
                Setup = setup ?? throw new ArgumentNullException(nameof(setup)),
                ParentSelector = parentSelector
            };

            if (IsEnabled) {
                CreateScopesForLoadedScenes();
            }
        }

        public void Unbind(string sceneNameOrPath) {
            EnsureNotDisposed();

            if (!string.IsNullOrWhiteSpace(sceneNameOrPath)) {
                m_Bindings.Remove(sceneNameOrPath);
            }
        }

        public void ClearBindings() {
            EnsureNotDisposed();
            m_Bindings.Clear();
        }

        public void Enable() {
            EnsureNotDisposed();

            if (IsEnabled) {
                return;
            }

            if (!m_Architecture.IsStarted) {
                throw new InvalidOperationException("Architecture is not started.");
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            IsEnabled = true;

            CreateScopesForLoadedScenes();
        }

        private void CreateScopesForLoadedScenes() {
            for (var i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);

                if (scene.isLoaded) {
                    CreateScopeForScene(scene);
                }
            }
        }

        public void Disable() {
            EnsureNotDisposed();
            DisableCore();
        }

        private void DisableCore() {
            if (!IsEnabled) {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            IsEnabled = false;

            foreach (var scope in m_SceneScopes.Values) {
                scope?.Dispose();
            }

            m_SceneScopes.Clear();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => CreateScopeForScene(scene);

        private void CreateScopeForScene(Scene scene) {
            if (!m_Architecture.IsStarted || m_SceneScopes.ContainsKey(scene.handle)) {
                return;
            }

            if (!TryGetBinding(scene, out var binding)) {
                return;
            }

            try {
                var parent = binding.ParentSelector?.Invoke(scene);
                var scopeName = $"Scene:{scene.name}#{scene.handle}";
                ArchitectureScope scope;

                if (parent != null) {
                    scope = parent.CreateChild(
                        scopeName,
                        child => {
                            child.BoundSceneName = scene.name;
                            binding.Setup(child);
                        },
                        scene.path
                    );
                } else {
                    scope = m_Architecture.CreateRootScope(
                        scopeName,
                        root => {
                            root.BoundSceneName = scene.name;
                            binding.Setup(root);
                        },
                        scene.path
                    );
                }

                m_SceneScopes.Add(scene.handle, scope);
            } catch (Exception exception) {
                m_Architecture.ReportException(exception);
            }
        }

        private bool TryGetBinding(Scene scene, out Binding binding) {
            if (!string.IsNullOrEmpty(scene.path) && m_Bindings.TryGetValue(scene.path, out binding)) {
                return true;
            }

            return m_Bindings.TryGetValue(scene.name, out binding);
        }

        private void OnSceneUnloaded(Scene scene) {
            if (!m_SceneScopes.Remove(scene.handle, out var scope)) {
                return;
            }

            scope?.Dispose();
        }

        public void Dispose() {
            if (m_IsDisposed) {
                return;
            }

            DisableCore();
            m_Bindings.Clear();
            m_IsDisposed = true;
        }

        private void EnsureNotDisposed() {
            if (m_IsDisposed) {
                throw new ObjectDisposedException(nameof(SceneScopeBinder));
            }
        }
    }
}
