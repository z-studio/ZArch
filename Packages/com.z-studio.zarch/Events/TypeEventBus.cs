using System;
using System.Collections.Generic;

namespace ZArch {
    internal sealed class EventSubscriptionDebugInfo {
        public Type EventType { get; internal set; }
        public Delegate[] Subscribers { get; internal set; }
    }

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
        private readonly Dictionary<Type, Func<Delegate[]>> m_DebugSubscribers = new();

        public void Publish<T>() where T : new() => m_Signals.GetSignal<Signal<T>>()?.Emit(new T());

        public void Publish<T>(T message) => m_Signals.GetSignal<Signal<T>>()?.Emit(message);

        public IUnregister Subscribe<T>(Action<T> onEvent) {
            var signal = m_Signals.GetOrAddSignal<Signal<T>>();
            var unregister = signal.Subscribe(onEvent);

            if (!m_DebugSubscribers.ContainsKey(typeof(T))) {
                m_DebugSubscribers.Add(typeof(T), signal.GetSubscriberDebugInfo);
            }

            return unregister;
        }

        public void Unsubscribe<T>(Action<T> onEvent) => m_Signals.GetSignal<Signal<T>>()?.Unsubscribe(onEvent);

        public IReadOnlyList<EventSubscriptionDebugInfo> GetDebugInfo() {
            var result = new List<EventSubscriptionDebugInfo>(m_DebugSubscribers.Count);

            foreach (var pair in m_DebugSubscribers) {
                var subscribers = pair.Value();

                if (subscribers.Length == 0) {
                    continue;
                }

                result.Add(
                    new EventSubscriptionDebugInfo {
                        EventType = pair.Key,
                        Subscribers = subscribers
                    }
                );
            }

            result.Sort((left, right) => string.Compare(
                            left.EventType?.FullName,
                            right.EventType?.FullName,
                            StringComparison.Ordinal
                        )
            );

            return result;
        }

        public void Clear() {
            m_Signals.Clear();
            m_DebugSubscribers.Clear();
        }
    }
}