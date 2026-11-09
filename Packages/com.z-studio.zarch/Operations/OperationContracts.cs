namespace ZArch {
    public interface ICommand : IScopeAware, ICanGetModel, ICanGetSystem, ICanGetService,
        ICanPublishEvent, ICanSendCommand, ICanSendQuery {
        void Execute();
    }

    public interface ICommand<TResult> : IScopeAware, ICanGetModel, ICanGetSystem, ICanGetService,
        ICanPublishEvent, ICanSendCommand, ICanSendQuery {
        TResult Execute();
    }

    public interface IQuery<TResult> : IScopeAware, ICanGetModel, ICanGetSystem, ICanSendQuery {
        TResult Execute();
    }
}
