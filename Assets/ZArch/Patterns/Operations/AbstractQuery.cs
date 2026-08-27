namespace ZArch {
    public abstract class AbstractQuery<TResult> : IQuery<TResult> {
        private ArchitectureScope m_Scope;

        public ArchitectureScope GetScope() => m_Scope;
        public void SetScope(ArchitectureScope scope) => m_Scope = scope;
        public TResult Execute() => OnExecute();
        protected abstract TResult OnExecute();
    }
}
