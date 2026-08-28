using System;

namespace ZArch {
    public abstract partial class Architecture {
        public void SendEvent<T>() where T : new() {
            EnsureStarted();
            m_EventSystem.Send<T>();
        }

        public void SendEvent<T>(T message) {
            EnsureStarted();
            m_EventSystem.Send(message);
        }

        public IUnregister RegisterEvent<T>(Action<T> onEvent) {
            EnsureStarted();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return m_EventSystem.Register(onEvent);
        }

        public void UnregisterEvent<T>(Action<T> onEvent) {
            EnsureStarted();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_EventSystem.Unregister(onEvent);
        }
    }
}