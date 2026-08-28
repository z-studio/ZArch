using System;

namespace ZArch {
    public partial class Architecture {
        public void Publish<T>() where T : new() {
            EnsureStarted();
            m_Events.Publish<T>();
        }

        public void Publish<T>(T message) {
            EnsureStarted();
            m_Events.Publish(message);
        }

        public IUnregister Subscribe<T>(Action<T> onEvent) {
            EnsureStarted();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return m_Events.Subscribe(onEvent);
        }

        public void Unsubscribe<T>(Action<T> onEvent) {
            EnsureStarted();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_Events.Unsubscribe(onEvent);
        }
    }
}
