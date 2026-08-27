using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch.GameModules {
    public sealed class GameScopeFactory : IGameScopeFactory {
        private readonly ArchitectureScope m_ParentScope;

        public GameScopeFactory(ArchitectureScope parentScope) {
            m_ParentScope = parentScope ?? throw new ArgumentNullException(nameof(parentScope));
        }

        public Task<ArchitectureScope> CreateAsync(
            IGameModule module,
            GameLaunchContext context,
            CancellationToken cancellationToken
        ) {
            if (module == null) {
                throw new ArgumentNullException(nameof(module));
            }

            if (string.IsNullOrWhiteSpace(module.Id)) {
                throw new ArgumentException("Game module ID is empty.", nameof(module));
            }

            context ??= GameLaunchContext.Empty;

            return m_ParentScope.CreateChildAsync(
                $"Game:{module.Id}",
                (scope, token) => {
                    token.ThrowIfCancellationRequested();
                    module.Configure(scope, context);
                    return Task.CompletedTask;
                },
                module,
                cancellationToken: cancellationToken
            );
        }
    }
}
