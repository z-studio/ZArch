namespace ZArch {
    public interface IUtility { }

    public interface IModel : IBelongToScope,
                              ICanSetScope,
                              ICanGetUtility,
                              ICanPublishEvents,
                              IInitializable,
                              IDeinitializable { }

    public interface ISystem : IBelongToScope,
                               ICanSetScope,
                               ICanGetModel,
                               ICanGetUtility,
                               ICanSubscribeToEvents,
                               ICanPublishEvents,
                               ICanGetSystem,
                               IInitializable,
                               IDeinitializable { }

    public interface IController : IBelongToScope,
                                   ICanSendCommand,
                                   ICanGetSystem,
                                   ICanGetModel,
                                   ICanSubscribeToEvents,
                                   ICanSendQuery,
                                   ICanGetUtility { }
}
