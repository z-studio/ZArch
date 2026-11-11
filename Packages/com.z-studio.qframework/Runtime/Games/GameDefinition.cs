using UnityEngine;

namespace QFramework {
    /// <summary>
    /// 游戏包里的配置资产：指定 Prefab/场景，以及要注册的本游戏 Model/System。
    /// 加载器借用此资产，每次进入另外创建 Session 与游戏对象；不在资产里保存账号或回合数据。
    /// </summary>
    public abstract class GameDefinition : ScriptableObject {
        [SerializeField]
        private GameEntry m_Prefab;

        [SerializeField, Tooltip("场景入口地址。填写后使用场景，Prefab 留空；场景中放一个未激活的 GameEntry 根对象。")]
        private string m_SceneAddress;

        public GameEntry Prefab => m_Prefab;
        public string SceneAddress => m_SceneAddress;

        /// <summary>只注册新建的游戏本地对象；GameLauncher 会在注册全部完成后调用 Session.Initialize。</summary>
        public abstract void Configure(Session session);
    }
}