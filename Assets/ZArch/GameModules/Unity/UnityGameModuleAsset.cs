using UnityEngine;

namespace ZArch.GameModules.Unity {
    public abstract class UnityGameModuleAsset : ScriptableObject, IUnityGameModule {
        [SerializeField]
        [Tooltip("Unique, case-sensitive module ID used by GameLauncher.EnterAsync.")]
        private string m_Id;

        [SerializeField]
        [Tooltip("Scene name or Assets/... path loaded additively for this game.")]
        private string m_SceneNameOrPath;

        public string Id => m_Id;
        public string SceneNameOrPath => m_SceneNameOrPath;

        public abstract void Configure(
            ArchitectureScope scope,
            GameLaunchContext context
        );
    }
}
