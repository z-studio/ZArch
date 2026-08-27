using System;

namespace ZArch {
    public interface IEasyEvent {
        IUnregister Register(Action onEvent);
    }

    public class EasyEvent : IEasyEvent {
        private Action m_OnEvent = () => { };

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

        public void Unregister(Action onEvent) => m_OnEvent -= onEvent;

        public void Trigger() => EventDispatch.Invoke(m_OnEvent, handler => ((Action)handler).Invoke());
    }

    public class EasyEvent<T> : IEasyEvent {
        private Action<T> m_OnEvent = _ => { };

        public IUnregister Register(Action<T> onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_OnEvent += onEvent;
            return new CustomUnregister(() => Unregister(onEvent));
        }

        public void Unregister(Action<T> onEvent) => m_OnEvent -= onEvent;

        public void Trigger(T t) => EventDispatch.Invoke(m_OnEvent, handler => ((Action<T>)handler).Invoke(t));

        IUnregister IEasyEvent.Register(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return Register(_ => onEvent());
        }
    }

    public class EasyEvent<T, K> : IEasyEvent {
        private Action<T, K> m_OnEvent = (_, __) => { };

        public IUnregister Register(Action<T, K> onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_OnEvent += onEvent;
            return new CustomUnregister(() => Unregister(onEvent));
        }

        public void Unregister(Action<T, K> onEvent) => m_OnEvent -= onEvent;

        public void Trigger(T t, K k) =>
            EventDispatch.Invoke(m_OnEvent, handler => ((Action<T, K>)handler).Invoke(t, k));

        IUnregister IEasyEvent.Register(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return Register((_, __) => onEvent());
        }
    }

    public class EasyEvent<T, K, S> : IEasyEvent {
        private Action<T, K, S> m_OnEvent = (_, __, ___) => { };

        public IUnregister Register(Action<T, K, S> onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            m_OnEvent += onEvent;
            return new CustomUnregister(() => Unregister(onEvent));
        }

        public void Unregister(Action<T, K, S> onEvent) => m_OnEvent -= onEvent;

        public void Trigger(T t, K k, S s) =>
            EventDispatch.Invoke(m_OnEvent, handler => ((Action<T, K, S>)handler).Invoke(t, k, s));

        IUnregister IEasyEvent.Register(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return Register((_, __, ___) => onEvent());
        }
    }

}
