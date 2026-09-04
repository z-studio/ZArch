using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch.GameModules {
    public interface IGameModule {
        string Id { get; }
        IGameModuleRuntime Configure(ArchitectureScope scope, GameEnterContext gameEnterContext);
    }

    /// <summary>
    /// Per-session runtime returned by <see cref="IGameModule.Configure"/>.
    /// Enter runs after content is loaded and bound; Exit runs before content is unloaded.
    /// </summary>
    public interface IGameModuleRuntime {
        Task EnterAsync(CancellationToken cancellationToken);
        Task ExitAsync();
    }

    public static class GameModuleRuntime {
        private sealed class EmptyRuntime : IGameModuleRuntime {
            public Task EnterAsync(CancellationToken cancellationToken) {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task ExitAsync() => Task.CompletedTask;
        }

        public static IGameModuleRuntime Empty { get; } = new EmptyRuntime();
    }

    public interface IGameContentHandle { }

    public interface IGameContentLoader {
        Task<IGameContentHandle> LoadAsync(
            IGameModule module,
            ArchitectureScope scope,
            GameEnterContext context,
            CancellationToken cancellationToken
        );

        Task UnloadAsync(IGameContentHandle content);
    }

    public interface IGameModuleHost {
        GameModuleSession Current { get; }
        bool IsTransitioning { get; }
        IReadOnlyCollection<IGameModule> Modules { get; }

        bool TryGetModule(string gameId, out IGameModule module);

        Task<GameModuleSession> EnterAsync(
            string gameId,
            GameEnterContext context = null,
            CancellationToken cancellationToken = default
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
