using System.Threading;
using System.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Demo.GameA {
    public sealed class GameAEntry : GameEntry {
        public string RoomId { get; private set; }
        [SerializeField] private UnityEngine.UI.Text m_Rounds;
        private int m_Coins;

        protected override async Task OnEnterAsync(CancellationToken cancellation) {
            // 参数来自这一次大厅点击，不写入可复用的入口资源。
            RoomId = this.GetUtility<GameArguments>().TryGet<Demo.App.GameLaunchData>()?.RoomId;
            m_Coins = this.GetModel<Demo.App.WalletModel>().coins.Value;
            // 父级账号发出的普通事件，这里直接监听；游戏退出时由 Session 自动取消。
            this.RegisterEvent<Demo.App.CoinsChanged>(OnCoinsChanged);
            BindRoundCount();
            await LoadGameContentAsync(cancellation);
        }

        private void BindRoundCount() {
            // 这个模型只属于本次进入的游戏。退出后会释放，下次从 0 开始。
            var roundModel = this.GetModel<RoundModel>();
            var subscription = roundModel.rounds.RegisterAndInvoke(ShowRounds);
            subscription.UnregisterWhenSessionEnds(Session);
        }
        private void ShowRounds(int rounds) => m_Rounds.text = $"GameA rounds: {rounds} / Coins: {m_Coins}";
        private void OnCoinsChanged(Demo.App.CoinsChanged message) {
            m_Coins = message.Coins;
            ShowRounds(this.GetModel<RoundModel>().rounds.Value);
        }

        private async Task LoadGameContentAsync(CancellationToken cancellation) {
            // GameResources 由基类提供，绑定当前游戏包；退出时自动归还这些引用。
            await GameResources.LoadAsync<TextAsset>("GameConfig", cancellation);
            await GameResources.InstantiateAsync("Marker", transform, cancellation);
        }

        // Inspector 中的按钮只发送命令，业务修改在 PlayRoundCommand 中。
        public void PlayRound() => this.SendCommand(new PlayRoundCommand());
    }
}
