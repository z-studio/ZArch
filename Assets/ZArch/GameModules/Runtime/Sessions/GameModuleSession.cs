using System;

namespace ZArch.GameModules {
    public sealed class GameModuleSession {
        public IGameModule Module { get; }
        public GameEnterContext Context { get; }
        public ArchitectureScope Scope { get; }
        internal IGameContentHandle Content { get; set; }
        internal IGameModuleLifecycle Lifecycle { get; set; }
        internal bool IsLifecycleDeactivated { get; set; }
        internal bool IsContentUnloaded { get; set; }
        internal bool IsScopeDisposed { get; set; }
        internal bool IsCleanedUp => IsLifecycleDeactivated && IsContentUnloaded && IsScopeDisposed;

        internal GameModuleSession(
            IGameModule module,
            GameEnterContext context,
            ArchitectureScope scope
        ) {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            IsLifecycleDeactivated = true;
        }
    }
}
