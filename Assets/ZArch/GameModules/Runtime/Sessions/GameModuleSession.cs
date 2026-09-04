using System;

namespace ZArch.GameModules {
    public sealed class GameModuleSession {
        public IGameModule Module { get; }
        public GameEnterContext Context { get; }
        public ArchitectureScope Scope { get; }
        internal IGameContentHandle Content { get; set; }
        internal IGameModuleRuntime Runtime { get; }
        internal bool IsRuntimeExited { get; set; } = true;
        internal bool IsContentUnloaded { get; set; }
        internal bool IsScopeDisposed { get; set; }
        internal bool IsCleanedUp => IsRuntimeExited && IsContentUnloaded && IsScopeDisposed;

        internal GameModuleSession(
            IGameModule module,
            GameEnterContext context,
            ArchitectureScope scope,
            IGameModuleRuntime runtime
        ) {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }
    }
}
