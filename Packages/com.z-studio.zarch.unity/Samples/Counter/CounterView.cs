using UnityEngine;

namespace ZArch.Unity.Samples {
    public sealed class CounterView : ArchitectureView {
        [SerializeField]
        private UnityEngine.UI.Text m_Count;

        protected override void OnReady() {
            Track(GetModel<CounterModel>().Count.SubscribeAndInvoke(ShowCount));
        }

        private void ShowCount(int count) => m_Count.text = count.ToString();

        // 在 Inspector 的 Button.onClick 中连接，运行前即可看见调用关系。
        public void Increase() {
            if (IsReady) {
                this.SendCommand(new IncreaseCountCommand());
            }
        }
    }
}