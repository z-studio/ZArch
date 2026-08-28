using System;
using System.Collections.Generic;

namespace ZArch {
    internal sealed class SignalRegistry {
        private readonly Dictionary<Type, ISignal> m_Signals = new();

        public void AddSignal<T>() where T : ISignal, new() => m_Signals.Add(typeof(T), new T());

        public T GetSignal<T>() where T : ISignal {
            return m_Signals.TryGetValue(typeof(T), out var signal) ? (T)signal : default;
        }

        public T GetOrAddSignal<T>() where T : ISignal, new() {
            var signalType = typeof(T);

            if (m_Signals.TryGetValue(signalType, out var signal)) {
                return (T)signal;
            }

            var created = new T();
            m_Signals.Add(signalType, created);
            return created;
        }

        public void Clear() => m_Signals.Clear();
    }

    internal sealed class TypeEventBus {
        private readonly SignalRegistry m_Signals = new();

        public void Publish<T>() where T : new() => m_Signals.GetSignal<Signal<T>>()?.Emit(new T());

        public void Publish<T>(T message) => m_Signals.GetSignal<Signal<T>>()?.Emit(message);

        public IUnregister Subscribe<T>(Action<T> onEvent) => m_Signals.GetOrAddSignal<Signal<T>>().Subscribe(onEvent);

        public void Unsubscribe<T>(Action<T> onEvent) => m_Signals.GetSignal<Signal<T>>()?.Unsubscribe(onEvent);

        public void Clear() => m_Signals.Clear();
    }
}
