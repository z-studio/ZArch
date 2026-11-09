using UnityEngine;

namespace ZArch.Unity.Samples {
    [CreateAssetMenu(menuName = "ZArch/Samples/Round Game")]
    public sealed class RoundGameDefinition : GameDefinition {
        protected override void ConfigureServices(ArchitectureScope services) {
            services.Register(new RoundModel());
            services.Register(new RoundSystem());
        }
    }
}