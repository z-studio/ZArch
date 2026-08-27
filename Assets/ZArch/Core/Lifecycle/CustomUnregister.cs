using System;

namespace ZArch {
    public sealed class CustomUnregister : IUnregister {
        private Action m_OnUnregister;

        public CustomUnregister(Action onUnregister) =>
            m_OnUnregister = onUnregister ?? throw new ArgumentNullException(nameof(onUnregister));

        public void Unregister() {
            var action = m_OnUnregister;
            m_OnUnregister = null;
            action?.Invoke();
        }
    }
}
