namespace ZArch {
    public abstract class AbstractQuery<TResult> : IQuery<TResult> {
        public ArchitectureScope Scope { get; private set; }

        public void SetScope(ArchitectureScope scope) => Scope = scope;
        public TResult Execute() => OnExecute();
        protected abstract TResult OnExecute();
    }
}
