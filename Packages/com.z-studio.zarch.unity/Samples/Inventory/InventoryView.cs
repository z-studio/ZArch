using UnityEngine;

namespace ZArch.Unity.Samples {
    public sealed class InventoryView : ArchitectureView {
        [SerializeField]
        private UnityEngine.UI.Text m_Count;

        [SerializeField]
        private GameObject m_Panel;

        protected override void OnReady() {
            Track(GetModel<InventoryModel>().Apples.SubscribeAndInvoke(ShowCount));
        }

        private void ShowCount(int count) => m_Count.text = $"Apples: {count}";

        public void AddApple() {
            if (IsReady) {
                this.SendCommand(new AddAppleCommand());
            }
        }

        public void TogglePanel() => m_Panel.SetActive(!m_Panel.activeSelf);
    }
}