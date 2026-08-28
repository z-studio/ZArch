namespace ZArch {
    public interface IUtility { }

    public interface IModel : IBelongToScope,
                              ICanSetScope,
                              ICanGetUtility,
                              ICanPublishArchitectureEvents,
                              IInitializable,
                              IDeinitializable { }

    public interface ISystem : IBelongToScope,
                               ICanSetScope,
                               ICanGetModel,
                               ICanGetUtility,
                               ICanSubscribeToArchitectureEvents,
                               ICanPublishArchitectureEvents,
                               ICanGetSystem,
                               IInitializable,
                               IDeinitializable { }

    public interface IController : IBelongToScope,
                                   ICanSendCommand,
                                   ICanGetSystem,
                                   ICanGetModel,
                                   ICanSubscribeToArchitectureEvents,
                                   ICanSendQuery,
                                   ICanGetUtility { }
}
