namespace ZArch.GameModules.Unity {
    public interface IUnityGameModule : IGameModule {
        string SceneProviderId { get; }
        string SceneLocation { get; }
    }
}
