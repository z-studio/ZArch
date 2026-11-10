using System;
using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    [Serializable]
    public sealed class GameInfo {
        public string id;
        public string entryAddress;
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