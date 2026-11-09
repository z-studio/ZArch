namespace ZArch {
    // 由角色接口组合能力。业务代码继承基类即可，无需逐项实现这些接口。
    public interface ICanGetModel : IArchitectureContext { }
    public interface ICanGetSystem : IArchitectureContext { }
    public interface ICanGetService : IArchitectureContext { }
    public interface ICanSubscribeEvent : IArchitectureContext { }
    public interface ICanPublishEvent : IArchitectureContext { }
    public interface ICanSendCommand : IArchitectureContext { }
    public interface ICanSendQuery : IArchitectureContext { }

    public interface IController : ICanGetModel, ICanGetSystem, ICanGetService,
        ICanSubscribeEvent, ICanSendCommand, ICanSendQuery { }
}
