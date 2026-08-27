using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch.GameModules {
    public interface IGameModule {
        string Id { get; }
        void Configure(ArchitectureScope scope, GameLaunchContext context);
    }

    public interface IGameContentHandle { }

    public interface IGameContentLoader {
        Task<IGameContentHandle> LoadAsync(
            IGameModule module,
            ArchitectureScope scope,
            GameLaunchContext context,
            CancellationToken cancellationToken
        );

        Task UnloadAsync(IGameContentHandle content, CancellationToken cancellationToken);
    }

    public interface IGameScopeFactory {
        Task<ArchitectureScope> CreateAsync(
            IGameModule module,
            GameLaunchContext context,
            CancellationToken cancellationToken
        );
    }

    public interface IGameLauncher {
        GameSession Current { get; }
        bool IsTransitioning { get; }
        IReadOnlyCollection<IGameModule> Modules { get; }

        bool TryGetModule(string gameId, out IGameModule module);

        Task<GameSession> EnterAsync(
            string gameId,
            GameLaunchContext context = null,
            CancellationToken cancellationToken = default
        );

        Task ExitAsync();
    }

    public sealed class GameLaunchContext {
        public static GameLaunchContext Empty { get; } = new();

        public object Arguments { get; }

        public GameLaunchContext(object arguments = null) {
            Arguments = arguments;
        }

        public bool TryGetArguments<T>(out T arguments) {
            if (Arguments is T typed) {
                arguments = typed;
                return true;
            }

            arguments = default;
            return false;
        }

        public T GetArguments<T>() {
            if (TryGetArguments<T>(out var arguments)) {
                return arguments;
            }

            var actual = Arguments?.GetType().FullName ?? "null";
            throw new InvalidOperationException(
                $"Game launch arguments are {actual}, not {typeof(T).FullName}."
            );
        }
    }

    public enum EGameSessionState {
        Entering,
        Active,
        Exiting,
        Disposed,
        Faulted
    }
}
