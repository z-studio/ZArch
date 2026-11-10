using System.Threading;
using System.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Demo.GameB {
    public sealed class GameBEntry : GameEntry {
        [SerializeField]
        private UnityEngine.UI.Text m_Rounds;

        protected override Task OnEnterAsync(CancellationToken cancellation) {
            this.GetModel<RoundModel>().rounds.RegisterAndInvoke(value => m_Rounds.text = $"GameB rounds: {value}")
                .UnregisterWhenSessionEnds(Session);
            return Task.CompletedTask;
        }

        // Inspector 中的按钮绑定此方法，沿用标准 QF Command。
        public void PlayRound() => this.SendCommand(new PlayRoundCommand());
    }
}