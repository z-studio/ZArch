using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch.GameModules {
    internal sealed class GameScopeCreation {
        public ArchitectureScope Scope { get; }
        public IGameModuleRuntime Runtime { get; }

        public GameScopeCreation(ArchitectureScope scope, IGameModuleRuntime runtime) {
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }
    }

    internal sealed class GameScopeFactory {
        private readonly ArchitectureScope m_ParentScope;

        public GameScopeFactory(ArchitectureScope parentScope) {
            m_ParentScope = parentScope ?? throw new ArgumentNullException(nameof(parentScope));
        }

        public async Task<GameScopeCreation> CreateAsync(
            IGameModule module,
            GameEnterContext context,
            CancellationToken cancellationToken
        ) {
            if (module == null) {
                throw new ArgumentNullException(nameof(module));
            }

            if (string.IsNullOrWhiteSpace(module.Id)) {
                throw new ArgumentException("Game module ID is empty.", nameof(module));
            }

            context ??= GameEnterContext.Empty;

            IGameModuleRuntime runtime = null;
            var gameScope = await m_ParentScope.CreateChildAsync(
                $"Game:{module.Id}",
                (scope, token) => {
                    token.ThrowIfCancellationRequested();
                    runtime = module.Configure(scope, context)
                              ?? throw new InvalidOperationException(
                                  $"Game module '{module.Id}' returned a null runtime from Configure. "
                                  + $"Return {nameof(GameModuleRuntime)}.{nameof(GameModuleRuntime.Empty)} when no lifecycle is needed."
                              );
                    return Task.CompletedTask;
                },
                module,
                cancellationToken: cancellationToken
            ).ConfigureAwait(true);

            return new GameScopeCreation(gameScope, runtime);
        }
    }
}
