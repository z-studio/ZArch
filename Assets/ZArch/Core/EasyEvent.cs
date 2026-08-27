using System;
using System.Collections.Generic;

namespace ZArch {
    internal static class EventDispatch {
        public static void Invoke(Delegate handlers, Action<Delegate> invoke) {
            if (handlers == null) {
                return;
            }

            List<Exception> exceptions = null;

            foreach (var handler in handlers.GetInvocationList()) {
                try {
                    invoke(handler);
                } catch (Exception exception) {
                    exceptions ??= new List<Exception>();

                    if (exception is AggregateException aggregate) {
                        exceptions.AddRange(aggregate.Flatten().InnerExceptions);
                    } else {
                        exceptions.Add(exception);
                    }
                }
            }

            if (exceptions != null) {
                throw new AggregateException(exceptions);
            }
        }
    }

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

    public class EasyEvents {
        private readonly Dictionary<Type, IEasyEvent> m_TypeEvents = new();

        public void AddEvent<T>() where T : IEasyEvent, new() => m_TypeEvents.Add(typeof(T), new T());

        public T GetEvent<T>() where T : IEasyEvent {
            return m_TypeEvents.TryGetValue(typeof(T), out var e) ? (T)e : default;
        }

        public T GetOrAddEvent<T>() where T : IEasyEvent, new() {
            var eType = typeof(T);

            if (m_TypeEvents.TryGetValue(eType, out var e)) {
                return (T)e;
            }

            var created = new T();
            m_TypeEvents.Add(eType, created);
            return created;
        }

        public void Clear() => m_TypeEvents.Clear();
    }

    public class TypeEventSystem {
        private readonly EasyEvents m_Events = new();

        public void Send<T>() where T : new() => m_Events.GetEvent<EasyEvent<T>>()?.Trigger(new T());

        public void Send<T>(T e) => m_Events.GetEvent<EasyEvent<T>>()?.Trigger(e);

        public IUnregister Register<T>(Action<T> onEvent) => m_Events.GetOrAddEvent<EasyEvent<T>>().Register(onEvent);

        public void Unregister<T>(Action<T> onEvent) => m_Events.GetEvent<EasyEvent<T>>()?.Unregister(onEvent);

        public void Clear() => m_Events.Clear();
    }

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