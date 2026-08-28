using System;
using System.Collections.Generic;

namespace ZArch {
    public class AnySignal : ISignal, IUnregisterList, IDisposable {
        private Action m_OnEvent = () => { };

        public List<IUnregister> UnregisterList { get; } = new();

        public AnySignal Or(ISignal signal) {
            if (signal == null) {
                throw new ArgumentNullException(nameof(signal));
            }

            signal.Subscribe(Emit).AddToUnregisterList(this);
            return this;
        }

        public IUnregister Subscribe(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_OnEvent += onEvent;
            return new CustomUnregister(() => Unsubscribe(onEvent));
        }

        public IUnregister SubscribeAndInvoke(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            onEvent.Invoke();
            return Subscribe(onEvent);
        }

        public void Unsubscribe(Action onEvent) {
            m_OnEvent -= onEvent;
        }

        public void Dispose() {
            this.UnregisterAll();
            m_OnEvent = () => { };
        }

        private void Emit() => EventDispatch.Invoke(m_OnEvent, handler => ((Action)handler).Invoke());
    }

    public static class SignalExtensions {
        public static AnySignal Or(this ISignal self, ISignal other) => new AnySignal().Or(self).Or(other);
    }
}
