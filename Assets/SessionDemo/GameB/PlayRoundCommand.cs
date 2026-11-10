using Demo.App;
using QFramework;

namespace Demo.GameB {
    public sealed class PlayRoundCommand : AbstractCommand {
        protected override void OnExecute() {
            this.GetModel<RoundModel>().rounds.Value++;

            // 本地没有 WalletModel，自动取得 App 钱包的同一个实例。
            this.GetModel<WalletModel>().coins.Value += 20;
        }
    }
}