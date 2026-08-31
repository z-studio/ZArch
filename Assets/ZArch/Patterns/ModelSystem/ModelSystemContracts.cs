namespace ZArch {
    public interface IUtility { }

    public interface IModel : IBelongToScope,
                              ICanSetScope,
                              ICanGetUtility,
                              ICanPublishEvent,
                              IInitializable,
                              IDeinitializable { }

    public interface ISystem : IBelongToScope,
                               ICanSetScope,
                               ICanGetModel,
                               ICanGetUtility,
                               ICanSubscribeEvent,
                               ICanPublishEvent,
                               ICanGetSystem,
                               IInitializable,
                               IDeinitializable { }

    public interface IController : IBelongToScope,
                                   ICanSendCommand,
                                   ICanGetSystem,
                                   ICanGetModel,
                                   ICanSubscribeEvent,
                                   ICanSendQuery,
                                   ICanGetUtility { }
}
