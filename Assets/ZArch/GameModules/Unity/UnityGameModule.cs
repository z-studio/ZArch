namespace ZArch.GameModules.Unity {
    public interface IUnityGameModule : IGameModule {
        string SceneNameOrPath { get; }
    }
}
