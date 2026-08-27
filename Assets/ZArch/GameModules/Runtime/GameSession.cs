using System;

namespace ZArch.GameModules {
    public sealed class GameSession {
        public IGameModule Module { get; }
        public GameLaunchContext Context { get; }
        public ArchitectureScope Scope { get; }
        internal IGameContentHandle Content { get; set; }
        internal bool IsCleanedUp { get; set; }

        internal GameSession(
            IGameModule module,
            GameLaunchContext context,
            ArchitectureScope scope
        ) {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }
    }
}
