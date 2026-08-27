namespace ZArch {
    public interface ICommand : IBelongToScope,
                                ICanSetScope,
                                ICanGetSystem,
                                ICanGetModel,
                                ICanGetUtility,
                                ICanSendArchitectureEvent,
                                ICanSendCommand,
                                ICanSendQuery {
        void Execute();
    }

    public interface ICommand<TResult> : IBelongToScope,
                                         ICanSetScope,
                                         ICanGetSystem,
                                         ICanGetModel,
                                         ICanGetUtility,
                                         ICanSendArchitectureEvent,
                                         ICanSendCommand,
                                         ICanSendQuery {
        TResult Execute();
    }

    public interface IQuery<TResult> : IBelongToScope,
                                       ICanSetScope,
                                       ICanGetModel,
                                       ICanGetSystem,
                                       ICanSendQuery {
        TResult Execute();
    }
}
