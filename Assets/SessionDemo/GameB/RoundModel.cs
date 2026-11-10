using QFramework;

namespace Demo.GameB {
    public sealed class RoundModel : AbstractModel {
        public readonly BindableProperty<int> rounds = new(0);

        protected override void OnInit() {
        }
    }
}