using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZArch.Unity {
    public abstract class UnregisterTrigger : MonoBehaviour {
        private sealed class TrackedUnregister : IUnregister {
            private UnregisterTrigger m_Owner;
            private IUnregister m_Unregister;

            public TrackedUnregister(UnregisterTrigger owner, IUnregister unregister) {
                m_Owner = owner;
                m_Unregister = unregister;
            }

            public void Unregister() {
                var unregister = m_Unregister;

                if (unregister == null) {
                    return;
                }

                m_Unregister = null;
                var owner = m_Owner;
                m_Owner = null;
                owner?.Remove(this);
                unregister.Unregister();
            }
        }

        private readonly HashSet<TrackedUnregister> m_Unregisters = new();

        public IUnregister AddUnregister(IUnregister unregister) {
            if (unregister == null) {
                throw new ArgumentNullException(nameof(unregister));
            }

            var tracked = new TrackedUnregister(this, unregister);
            m_Unregisters.Add(tracked);
            return tracked;
        }

        private void Remove(TrackedUnregister unregister) => m_Unregisters.Remove(unregister);

        public void Unregister() {
            var snapshot = new List<TrackedUnregister>(m_Unregisters);
            m_Unregisters.Clear();

            foreach (var unregister in snapshot) {
                try {
                    unregister.Unregister();
                } catch (Exception exception) {
                    Debug.LogException(exception);
                }
            }
        }
    }

    public sealed class UnregisterOnDestroyTrigger : UnregisterTrigger {
        private void OnDestroy() => Unregister();
    }

    public sealed class UnregisterOnDisableTrigger : UnregisterTrigger {
        private void OnDisable() => Unregister();
    }

    public sealed class UnregisterCurrentSceneUnloadedTrigger : MonoBehaviour {
        private sealed class SceneUnregister : IUnregister {
            private UnregisterCurrentSceneUnloadedTrigger m_Owner;
            private IUnregister m_Unregister;

            public int SceneHandle { get; }

            public SceneUnregister(
                UnregisterCurrentSceneUnloadedTrigger owner,
                int sceneHandle,
                IUnregister unregister
            ) {
                m_Owner = owner;
                SceneHandle = sceneHandle;
                m_Unregister = unregister;
            }

            public void Unregister() {
                var unregister = m_Unregister;

                if (unregister == null) {
                    return;
                }

                m_Unregister = null;
                var owner = m_Owner;
                m_Owner = null;
                owner?.Remove(this);
                unregister.Unregister();
            }
        }

        private static UnregisterCurrentSceneUnloadedTrigger s_Default;
        private readonly Dictionary<int, HashSet<SceneUnregister>> m_SceneUnregisters = new();

        public static UnregisterCurrentSceneUnloadedTrigger Instance {
            get {
                if (!s_Default) {
                    s_Default = new GameObject(nameof(UnregisterCurrentSceneUnloadedTrigger))
                        .AddComponent<UnregisterCurrentSceneUnloadedTrigger>();
                }

                return s_Default;
            }
        }

        private void Awake() {
            DontDestroyOnLoad(gameObject);
            hideFlags = HideFlags.HideInHierarchy;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        public IUnregister AddUnregister(IUnregister unregister) =>
            AddUnregister(unregister, SceneManager.GetActiveScene());

        public IUnregister AddUnregister(IUnregister unregister, Scene scene) {
            if (unregister == null) {
                throw new ArgumentNullException(nameof(unregister));
            }

            if (!scene.IsValid()) {
                throw new ArgumentException("Scene is invalid.", nameof(scene));
            }

            if (!m_SceneUnregisters.TryGetValue(scene.handle, out var unregisters)) {
                unregisters = new HashSet<SceneUnregister>();
                m_SceneUnregisters.Add(scene.handle, unregisters);
            }

            var tracked = new SceneUnregister(this, scene.handle, unregister);
            unregisters.Add(tracked);
            return tracked;
        }

        private void Remove(SceneUnregister unregister) {
            if (!m_SceneUnregisters.TryGetValue(unregister.SceneHandle, out var unregisters)) {
                return;
            }

            unregisters.Remove(unregister);

            if (unregisters.Count == 0) {
                m_SceneUnregisters.Remove(unregister.SceneHandle);
            }
        }

        private void OnDestroy() {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            UnregisterAllScenes();

            if (ReferenceEquals(s_Default, this)) {
                s_Default = null;
            }
        }

        private void OnSceneUnloaded(Scene scene) {
            if (!m_SceneUnregisters.Remove(scene.handle, out var unregisters)) {
                return;
            }

            UnregisterSnapshot(unregisters);
        }

        private void UnregisterAllScenes() {
            var allUnregisters = new List<SceneUnregister>();

            foreach (var unregisters in m_SceneUnregisters.Values) {
                allUnregisters.AddRange(unregisters);
            }

            m_SceneUnregisters.Clear();
            UnregisterSnapshot(allUnregisters);
        }

        private static void UnregisterSnapshot(IEnumerable<SceneUnregister> unregisters) {
            foreach (var unregister in new List<SceneUnregister>(unregisters)) {
                try {
                    unregister.Unregister();
                } catch (Exception exception) {
                    Debug.LogException(exception);
                }
            }
        }
    }

    public static class UnityUnregisterExtension {
        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component {
            if (!gameObject) {
                throw new ArgumentNullException(nameof(gameObject));
            }

            var trigger = gameObject.GetComponent<T>();
            return trigger ? trigger : gameObject.AddComponent<T>();
        }

        public static IUnregister UnregisterWhenGameObjectDestroyed(
            this IUnregister unregister,
            GameObject gameObject
        ) {
            if (unregister == null) {
                throw new ArgumentNullException(nameof(unregister));
            }

            return GetOrAddComponent<UnregisterOnDestroyTrigger>(gameObject).AddUnregister(unregister);
        }

        public static IUnregister UnregisterWhenGameObjectDestroyed<T>(this IUnregister unregister, T component)
            where T : Component {
            if (!component) {
                throw new ArgumentNullException(nameof(component));
            }

            return unregister.UnregisterWhenGameObjectDestroyed(component.gameObject);
        }

        public static IUnregister UnregisterWhenDisabled(this IUnregister unregister, GameObject gameObject) {
            if (unregister == null) {
                throw new ArgumentNullException(nameof(unregister));
            }

            return GetOrAddComponent<UnregisterOnDisableTrigger>(gameObject).AddUnregister(unregister);
        }

        public static IUnregister UnregisterWhenDisabled<T>(this IUnregister unregister, T component)
            where T : Component {
            if (!component) {
                throw new ArgumentNullException(nameof(component));
            }

            return unregister.UnregisterWhenDisabled(component.gameObject);
        }

        public static IUnregister UnregisterWhenCurrentSceneUnloaded(this IUnregister unregister) =>
            unregister.UnregisterWhenSceneUnloaded(SceneManager.GetActiveScene());

        public static IUnregister UnregisterWhenSceneUnloaded(this IUnregister unregister, Scene scene) {
            if (unregister == null) {
                throw new ArgumentNullException(nameof(unregister));
            }

            return UnregisterCurrentSceneUnloadedTrigger.Instance.AddUnregister(unregister, scene);
        }

        public static IUnregister UnregisterWhenGameObjectSceneUnloaded(
            this IUnregister unregister,
            GameObject gameObject
        ) {
            if (!gameObject) {
                throw new ArgumentNullException(nameof(gameObject));
            }

            return unregister.UnregisterWhenSceneUnloaded(gameObject.scene);
        }
    }
}
