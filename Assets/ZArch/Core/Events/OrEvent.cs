using System;
using System.Collections.Generic;

namespace ZArch {
    public class OrEvent : IUnregisterList, IDisposable {
        private Action m_OnEvent = () => { };

        public List<IUnregister> UnregisterList { get; } = new();

        public OrEvent Or(IEasyEvent easyEvent) {
            if (easyEvent == null) {
                throw new ArgumentNullException(nameof(easyEvent));
            }

            easyEvent.Register(Trigger).AddToUnregisterList(this);
            return this;
        }

        public IUnregister Register(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_OnEvent += onEvent;
            return new CustomUnregister(() => Unregister(onEvent));
        }

        public IUnregister RegisterWithACall(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            onEvent.Invoke();
            return Register(onEvent);
        }

        public void Unregister(Action onEvent) {
            m_OnEvent -= onEvent;
        }

        public void Dispose() {
            this.UnregisterAll();
            m_OnEvent = () => { };
        }

        private void Trigger() => EventDispatch.Invoke(m_OnEvent, handler => ((Action)handler).Invoke());
    }

    public static class OrEventExtensions {
        public static OrEvent Or(this IEasyEvent self, IEasyEvent e) => new OrEvent().Or(self).Or(e);
    }
}
