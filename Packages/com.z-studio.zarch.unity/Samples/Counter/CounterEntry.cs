namespace ZArch.Unity.Samples {
    public sealed class CounterEntry : SceneArchitecture {
        protected override void ConfigureServices(ArchitectureScope services) {
            services.Register(new CounterModel());
        }
    }
}