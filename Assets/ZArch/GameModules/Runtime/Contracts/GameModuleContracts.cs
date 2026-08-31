using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch.GameModules {
    public interface IGameModule {
        string Id { get; }
        void Configure(ArchitectureScope scope, GameEnterContext context);
    }

    public interface IGameContentHandle { }

    public interface IGameContentLoader {
        Task<IGameContentHandle> LoadAsync(
            IGameModule module,
            ArchitectureScope scope,
            GameEnterContext context,
            CancellationToken cancellationToken,
            string packageName
        );

        Task UnloadAsync(IGameContentHandle content);
    }

    public interface IGameScopeFactory {
        Task<ArchitectureScope> CreateAsync(
            IGameModule module,
            GameEnterContext context,
            CancellationToken cancellationToken
        );
    }

    public interface IGameModuleLauncher {
        GameModuleSession Current { get; }
        bool IsTransitioning { get; }
        IReadOnlyCollection<IGameModule> Modules { get; }

        bool TryGetModule(string gameId, out IGameModule module);

        Task<GameModuleSession> EnterAsync(
            string gameId,
            GameEnterContext context = null,
            CancellationToken cancellationToken = default,
            string packageName = ""
        );

        Task ExitAsync();
        Task ShutdownAsync();
    }

    public sealed class GameEnterContext {
        public static GameEnterContext Empty { get; } = new();

        public object Arguments { get; }

        public GameEnterContext(object arguments = null) {
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
}
