using System;
using System.Collections.Generic;

namespace ZArch {
    internal sealed class EasyEvents {
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

    internal sealed class TypeEventSystem {
        private readonly EasyEvents m_Events = new();

        public void Send<T>() where T : new() => m_Events.GetEvent<EasyEvent<T>>()?.Trigger(new T());

        public void Send<T>(T e) => m_Events.GetEvent<EasyEvent<T>>()?.Trigger(e);

        public IUnregister Register<T>(Action<T> onEvent) => m_Events.GetOrAddEvent<EasyEvent<T>>().Register(onEvent);

        public void Unregister<T>(Action<T> onEvent) => m_Events.GetEvent<EasyEvent<T>>()?.Unregister(onEvent);

        public void Clear() => m_Events.Clear();
    }
}
