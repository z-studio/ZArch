namespace ZArch {
    public abstract class AbstractCommand : ICommand {
        public ArchitectureScope Scope { get; private set; }

        public void SetScope(ArchitectureScope scope) => Scope = scope;
        void ICommand.Execute() => OnExecute();
        protected abstract void OnExecute();
    }

    public abstract class AbstractCommand<TResult> : ICommand<TResult> {
        public ArchitectureScope Scope { get; private set; }

        public void SetScope(ArchitectureScope scope) => Scope = scope;
        TResult ICommand<TResult>.Execute() => OnExecute();
        protected abstract TResult OnExecute();
    }
}