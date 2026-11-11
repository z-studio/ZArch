using System;
using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    /// <summary>目录中的一条记录，说明去哪里加载游戏。它不是游戏对象，也不保存回合状态。</summary>
    [Serializable]
    public sealed class GameInfo {
        // 大厅选游戏时使用的目录键；服务器房间号、渠道等本次业务数据放 GameArguments。
        public string id;

        // ZFramework/YooAsset 资源包名；留空时使用 id，不要求与服务器游戏 ID 相同。
        public string packageName;

        // 包内 GameDefinition 配置资源的地址，不是场景或根 Prefab 的直接地址。
        public string entryAddress;
    }

    /// <summary>GameLauncher 放进游戏 Session 的本次参数。持有传入对象，不复制，也不写入可复用配置。</summary>
    public sealed class GameArguments : IUtility {
        private readonly object m_Value;
        public GameArguments(object value) => m_Value = value;
        public T TryGet<T>() where T : class => m_Value as T;

        public T Get<T>() =>
            m_Value is T value ? value
                : throw new InvalidOperationException($"Game arguments must be {typeof(T).Name}.");
    }

    /// <summary>加载器：按目录记录取得入口资源，返回本次加载的持有对象。App 不需要引用具体游戏类型。</summary>
    public interface IGamePackageLoader {
        Task<IGamePackage> LoadAsync(GameInfo info, CancellationToken cancellation);
    }

    /// <summary>
    /// 本次加载的游戏包持有对象，由加载器创建、GameLauncher 释放；不是 YooAsset.ResourcePackage 本身。
    /// Configure 注册业务，CreateGame 创建运行对象，UnloadAsync 在游戏与会话结束后归还资源引用。
    /// </summary>
    public interface IGamePackage {
        void Configure(Session session);
        IGame CreateGame();
        Task UnloadAsync();
    }

    /// <summary>
    /// 加载器创建的一次游戏运行对象，由 GameLauncher 调用进入和退出。
    /// Unity 实现负责创建/销毁根对象，并把业务回调转发给 GameEntry；业务一般只继承 GameEntry。
    /// </summary>
    public interface IGame {
        Task EnterAsync(Session session, CancellationToken cancellation);

        // 初始化中途失败也可能调用；应能清理部分创建的对象，失败后允许重试。
        Task ExitAsync(CancellationToken cancellation);
    }

    /// <summary>GameLauncher 的工作状态；AppFlow 的大厅/登录状态由 AppState 表达。</summary>
    public enum GameState {
        /// <summary>没有当前游戏，可以打开。</summary>
        Idle,

        /// <summary>入口加载、会话初始化或业务进入尚未完成。</summary>
        Loading,

        /// <summary>游戏已进入。</summary>
        Running,

        /// <summary>按顺序退出对象、释放会话、归还资源。</summary>
        Closing,

        /// <summary>仍有未完成的关闭步骤；再次 Close/Shutdown 会继续清理。</summary>
        CloseFailed,

        /// <summary>整个启动器已关闭；下次登录会创建新启动器。</summary>
        Closed
    }
}