using System;
using UnityEngine;

namespace ZArch.Unity {
    [CreateAssetMenu(menuName = "ZArch/Game Definition", fileName = "Game")]
    public class GameDefinition : ScriptableObject, IGameDefinition {
        [SerializeField]
        private string m_Id;

        [SerializeField]
        private string m_SceneLocation;

        [SerializeField]
        private string m_SceneProviderId = GameSceneProviderIds.BuildSettings;

        public string Id => m_Id;
        public string SceneLocation => m_SceneLocation;
        public string SceneProviderId => m_SceneProviderId;

        [ContextMenu("ZArch/Validate Definition")]
        public void Validate() {
            if (string.IsNullOrWhiteSpace(m_Id)) {
                throw new InvalidOperationException($"Game asset '{name}': Id is empty.");
            }

            if (string.IsNullOrWhiteSpace(m_SceneLocation)) {
                throw new InvalidOperationException($"Game '{m_Id}': Scene Location is empty.");
            }

            if (string.IsNullOrWhiteSpace(m_SceneProviderId)) {
                throw new InvalidOperationException($"Game '{m_Id}': Scene Provider Id is empty.");
            }
        }

        void IGameDefinition.Configure(ArchitectureScope services) => ConfigureServices(services);

        protected virtual void ConfigureServices(ArchitectureScope services) {
        }
    }
}