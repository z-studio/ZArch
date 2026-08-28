namespace ZArch {
    public enum EScopeState {
        Created,
        Configuring,
        Initializing,
        Active,
        Disposing,
        Disposed,
        Faulted
    }

    public enum EEventPropagation {
        Local,
        Bubble
    }

    public interface IBelongToScope {
        ArchitectureScope GetScope();
    }

    public interface ICanSetScope {
        void SetScope(ArchitectureScope scope);
    }
}
