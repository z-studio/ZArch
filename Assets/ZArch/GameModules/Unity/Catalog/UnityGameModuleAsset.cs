using UnityEngine;

namespace ZArch.GameModules.Unity {
    public abstract class UnityGameModuleAsset : ScriptableObject, IUnityGameModule {
        [SerializeField]
        [Tooltip("Unique, case-sensitive module ID used by GameLauncher.EnterAsync.")]
        private string m_Id;

        [SerializeField]
        [Tooltip("ID of the scene provider used to load this game.")]
        private string m_SceneProviderId = GameSceneProviderIds.kBuildSettings;

        [SerializeField]
        [Tooltip("Provider-specific scene address, key, name or path.")]
        private string m_SceneLocation;

        public string Id => m_Id;
        public string SceneProviderId => m_SceneProviderId;
        public string SceneLocation => m_SceneLocation;

        public abstract void Configure(
            ArchitectureScope scope,
            GameLaunchContext context
        );
    }
}
