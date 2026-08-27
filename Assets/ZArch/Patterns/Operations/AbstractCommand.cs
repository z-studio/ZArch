namespace ZArch {
    public abstract class AbstractCommand : ICommand {
        private ArchitectureScope m_Scope;

        public ArchitectureScope GetScope() => m_Scope;
        public void SetScope(ArchitectureScope scope) => m_Scope = scope;
        void ICommand.Execute() => OnExecute();
        protected abstract void OnExecute();
    }

    public abstract class AbstractCommand<TResult> : ICommand<TResult> {
        private ArchitectureScope m_Scope;

        public ArchitectureScope GetScope() => m_Scope;
        public void SetScope(ArchitectureScope scope) => m_Scope = scope;
        TResult ICommand<TResult>.Execute() => OnExecute();
        protected abstract TResult OnExecute();
    }
}
