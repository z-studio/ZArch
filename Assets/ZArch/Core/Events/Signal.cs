using System;

namespace ZArch {
    public interface ISignal {
        IUnregister Subscribe(Action onEvent);
    }

    public class Signal : ISignal {
        private Action m_OnEvent = () => { };

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

        public void Unsubscribe(Action onEvent) => m_OnEvent -= onEvent;

        public void Emit() => EventDispatch.Invoke(m_OnEvent, handler => ((Action)handler).Invoke());
    }

    public class Signal<T> : ISignal {
        private Action<T> m_OnEvent;

        public IUnregister Subscribe(Action<T> onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_OnEvent += onEvent;
            return new CustomUnregister(() => Unsubscribe(onEvent));
        }

        public void Unsubscribe(Action<T> onEvent) => m_OnEvent -= onEvent;

        public void Emit(T value) => EventDispatch.Invoke(m_OnEvent, handler => ((Action<T>)handler).Invoke(value));

        internal Delegate[] GetSubscriberDebugInfo() => m_OnEvent?.GetInvocationList() ?? Array.Empty<Delegate>();

        IUnregister ISignal.Subscribe(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return Subscribe(_ => onEvent());
        }
    }

    public class Signal<T1, T2> : ISignal {
        private Action<T1, T2> m_OnEvent = (_, __) => { };

        public IUnregister Subscribe(Action<T1, T2> onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_OnEvent += onEvent;
            return new CustomUnregister(() => Unsubscribe(onEvent));
        }

        public void Unsubscribe(Action<T1, T2> onEvent) => m_OnEvent -= onEvent;

        public void Emit(T1 first, T2 second) =>
            EventDispatch.Invoke(m_OnEvent, handler => ((Action<T1, T2>)handler).Invoke(first, second));

        IUnregister ISignal.Subscribe(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return Subscribe((_, __) => onEvent());
        }
    }

    public class Signal<T1, T2, T3> : ISignal {
        private Action<T1, T2, T3> m_OnEvent = (_, __, ___) => { };

        public IUnregister Subscribe(Action<T1, T2, T3> onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_OnEvent += onEvent;
            return new CustomUnregister(() => Unsubscribe(onEvent));
        }

        public void Unsubscribe(Action<T1, T2, T3> onEvent) => m_OnEvent -= onEvent;

        public void Emit(T1 first, T2 second, T3 third) =>
            EventDispatch.Invoke(m_OnEvent, handler => ((Action<T1, T2, T3>)handler).Invoke(first, second, third));

        IUnregister ISignal.Subscribe(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return Subscribe((_, __, ___) => onEvent());
        }
    }
}
