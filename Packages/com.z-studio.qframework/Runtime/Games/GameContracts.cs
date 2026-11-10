using System;
using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    [Serializable]
    public sealed class GameInfo {
        public string id;
        // 业务 ID 与资源包名分开；未指定包名时使用 id。
        public string packageName;
        public string entryAddress;
    }

    /// <summary>仅属于本次进入的业务参数，不写进可复用的入口资源。</summary>
    public sealed class GameArguments : IUtility {
        private readonly object m_Value;
        public GameArguments(object value) => m_Value = value;
        public T TryGet<T>() where T : class => m_Value as T;
        public T Get<T>() => m_Value is T value ? value : throw new InvalidOperationException($"Game arguments must be {typeof(T).Name}.");
    }

    public interface IGamePackageLoader {
        Task<IGamePackage> LoadAsync(GameInfo info, CancellationToken cancellation);
    }

    public interface IGamePackage {
        void Configure(Session session);
        IGame CreateGame();
        Task UnloadAsync();
    }

    public interface IGame {
        Task EnterAsync(Session session, CancellationToken cancellation);

        // 初始化中途失败也可能调用；应能清理部分创建的对象，失败后允许重试。
        Task ExitAsync(CancellationToken cancellation);
    }

    public enum GameState {
        Idle,
        Loading,
        Running,
        Closing,
        CloseFailed,
        Closed
    }
}
