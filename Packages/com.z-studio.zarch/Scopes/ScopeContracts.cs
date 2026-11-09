namespace ZArch {
    public enum ScopeState {
        Created,
        Configuring,
        Initializing,
        Active,
        Disposing,
        Disposed,
        Faulted
    }

    public enum EventPropagation {
        Local,
        Bubble
    }

    public interface IArchitectureContext {
        ArchitectureScope Scope { get; }
    }

    public interface IScopeAware {
        void SetScope(ArchitectureScope scope);
    }
}
