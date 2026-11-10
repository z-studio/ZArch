using Demo.App;
using QFramework;

namespace Demo.GameA {
    public sealed class PlayRoundCommand : AbstractCommand {
        protected override void OnExecute() {
            var roundModel = this.GetModel<RoundModel>();
            var wallet = this.GetModel<WalletModel>();

            roundModel.rounds.Value++;       // 当前游戏的回合数。
            wallet.SetCoins(wallet.coins.Value + 10);       // 当前账号的钱包，切换游戏不会重建。
        }
    }
}
