namespace ZArch {
    public interface IUtility { }

    public interface IModel : IBelongToScope,
                              ICanSetScope,
                              ICanGetUtility,
                              ICanSendArchitectureEvent,
                              IInitializable,
                              IDeinitializable { }

    public interface ISystem : IBelongToScope,
                               ICanSetScope,
                               ICanGetModel,
                               ICanGetUtility,
                               ICanRegisterArchitectureEvent,
                               ICanSendArchitectureEvent,
                               ICanGetSystem,
                               IInitializable,
                               IDeinitializable { }

    public interface IController : IBelongToScope,
                                   ICanSendCommand,
                                   ICanGetSystem,
                                   ICanGetModel,
                                   ICanRegisterArchitectureEvent,
                                   ICanSendQuery,
                                   ICanGetUtility { }
}
