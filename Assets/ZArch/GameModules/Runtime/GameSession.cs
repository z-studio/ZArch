using System;

namespace ZArch.GameModules {
    public sealed class GameSession {
        public IGameModule Module { get; }
        public GameLaunchContext Context { get; }
        public ArchitectureScope Scope { get; }
        public IGameContentHandle Content { get; internal set; }
        public EGameSessionState State { get; internal set; }

        internal GameSession(
            IGameModule module,
            GameLaunchContext context,
            ArchitectureScope scope
        ) {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            State = EGameSessionState.Entering;
        }
    }
}
