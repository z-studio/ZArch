namespace ZArch {
    public interface IModel : IScopeAware, IInitializable, IDeinitializable,
        ICanGetService, ICanPublishEvent { }

    public interface ISystem : IScopeAware, IInitializable, IDeinitializable,
        ICanGetModel, ICanGetSystem, ICanGetService, ICanSubscribeEvent, ICanPublishEvent { }
}
