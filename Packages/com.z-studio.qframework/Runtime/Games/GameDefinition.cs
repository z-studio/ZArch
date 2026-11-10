using UnityEngine;

namespace QFramework {
    /// <summary>游戏包中的入口资源。只保存配置，每次进入都会创建新的会话与游戏对象。</summary>
    public abstract class GameDefinition : ScriptableObject {
        [SerializeField]
        private GameEntry m_Prefab;

        public GameEntry Prefab => m_Prefab;
        public abstract void Configure(Session session);
    }
}