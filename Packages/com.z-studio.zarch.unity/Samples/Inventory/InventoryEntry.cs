namespace ZArch.Unity.Samples {
    public sealed class InventoryEntry : SceneArchitecture {
        protected override void ConfigureServices(ArchitectureScope services) {
            services.Register(new InventoryModel());
            services.Register(new InventorySystem());
        }
    }
}