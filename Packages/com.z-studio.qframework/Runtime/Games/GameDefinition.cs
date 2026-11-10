using UnityEngine;

namespace QFramework {
    /// <summary>游戏包中的入口资源。只保存配置，每次进入都会创建新的会话与游戏对象。</summary>
    public abstract class GameDefinition : ScriptableObject {
        [SerializeField]
        private GameEntry m_Prefab;

        [SerializeField, Tooltip("场景入口地址。填写后使用场景，Prefab 留空；场景中放一个未激活的 GameEntry 根对象。")]
        private string m_SceneAddress;

        public GameEntry Prefab => m_Prefab;
        public string SceneAddress => m_SceneAddress;
        public abstract void Configure(Session session);
    }
}