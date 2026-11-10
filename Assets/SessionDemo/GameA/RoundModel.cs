using QFramework;

namespace Demo.GameA {
    public sealed class RoundModel : AbstractModel {
        public readonly BindableProperty<int> rounds = new(0);

        protected override void OnInit() {
        }
    }
}