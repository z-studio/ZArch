using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    // 引擎适配接口。普通 Unity 项目通过 GameDefinition 配置，无需自己实现。
    public interface IGameDefinition {
        string Id { get; }
        void Configure(ArchitectureScope services);
    }

    public interface IGameContentLoader {
        void Validate(IGameDefinition game);

        Task<IGameContent> LoadAsync(IGameDefinition game, ArchitectureScope scope,
            CancellationToken cancellationToken);
    }

    public interface IGameContent {
        Task EnterAsync(object arguments, CancellationToken cancellationToken);
        Task ExitAsync();
        Task UnloadAsync();
    }

    public enum EGameState {
        Idle,
        Opening,
        Running,
        Closing,
        CloseFailed,
        ShuttingDown,
        ShutdownFailed,
        Closed
    }

    // 加载失败但内容尚未释放时，适配器必须交还句柄，以便保留服务并重试清理。
    public sealed class GameContentLoadException : AggregateException {
        public IGameContent Content { get; }

        public GameContentLoadException(IGameContent content, Exception loadError, Exception cleanupError)
            : base("Game content failed to load and rollback failed.", loadError, cleanupError) {
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }
    }
}