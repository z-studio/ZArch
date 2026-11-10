using System.Threading;
using System.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Demo.GameB {
    public sealed class GameBEntry : GameEntry {
        [SerializeField]
        private UnityEngine.UI.Text m_Rounds;

        protected override async Task OnEnterAsync(CancellationToken cancellation) {
            this.GetModel<RoundModel>().rounds.RegisterAndInvoke(value => m_Rounds.text = $"GameB rounds: {value}")
                .UnregisterWhenSessionEnds(Session);
            
            // 动态加载的多个资源由本游戏拥有，退出时接入层统一归还。
            await Resources.LoadAsync<TextAsset>("GameConfig", cancellation);
            await Resources.InstantiateAsync("Marker", transform, cancellation);
        }

        // Inspector 中的按钮绑定此方法，沿用标准 QF Command。
        public void PlayRound() => this.SendCommand(new PlayRoundCommand());
    }
}