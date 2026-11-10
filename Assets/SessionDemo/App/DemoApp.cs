using QFramework;

namespace Demo.App {
    public sealed class DemoApp : Architecture<DemoApp> {
        protected override void Init() {
            RegisterModel(new WalletModel());
            RegisterUtility(new GameNavigation());
        }
    }

    public sealed class WalletModel : AbstractModel {
        public readonly BindableProperty<int> coins = new(100);

        protected override void OnInit() {
        }
    }

    public sealed class GameNavigation : IUtility {
        public GameLauncher Launcher { get; set; }
    }
}