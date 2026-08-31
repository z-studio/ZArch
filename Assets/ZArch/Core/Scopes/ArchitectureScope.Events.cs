using System;
using System.Collections.Generic;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        private enum EEventSubscriptionSource {
            Scoped,
            Architecture
        }

        private sealed class TrackedEventUnregister : IUnregister {
            private ArchitectureScope m_Owner;
            private IUnregister m_Unregister;
            private Delegate m_Handler;
            private readonly EEventSubscriptionSource m_Source;

            public TrackedEventUnregister(
                ArchitectureScope owner,
                IUnregister unregister,
                Delegate handler,
                EEventSubscriptionSource source
            ) {
                m_Owner = owner;
                m_Unregister = unregister;
                m_Handler = handler;
                m_Source = source;
            }

            public bool Matches(Delegate handler, EEventSubscriptionSource source) =>
                m_Source == source && Equals(m_Handler, handler);

            public void Unregister() {
                var unregister = m_Unregister;

                if (unregister == null) {
                    return;
                }

                m_Unregister = null;
                m_Handler = null;
                var owner = m_Owner;
                m_Owner = null;
                owner?.m_EventUnregisters.Remove(this);
                unregister.Unregister();
            }
        }

        public IUnregister Subscribe<T>(Action<T> onEvent) {
            EnsureNotDisposed();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            var tracked = new TrackedEventUnregister(
                this,
                m_Events.Subscribe(onEvent),
                onEvent,
                EEventSubscriptionSource.Scoped
            );

            m_EventUnregisters.Add(tracked);
            return tracked;
        }

        public void Unsubscribe<T>(Action<T> onEvent) {
            EnsureNotDisposed();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            var matches = new List<TrackedEventUnregister>();

            foreach (var unregister in m_EventUnregisters) {
                if (unregister.Matches(onEvent, EEventSubscriptionSource.Scoped)) {
                    matches.Add(unregister);
                }
            }

            foreach (var unregister in matches) {
                unregister.Unregister();
            }
        }

        internal IUnregister SubscribeArchitectureEvent<T>(Action<T> onEvent) {
            EnsureNotDisposed();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            var tracked = new TrackedEventUnregister(
                this,
                Architecture.Subscribe(onEvent),
                onEvent,
                EEventSubscriptionSource.Architecture
            );

            m_EventUnregisters.Add(tracked);
            return tracked;
        }

        internal void UnsubscribeArchitectureEvent<T>(Action<T> onEvent) {
            EnsureNotDisposed();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            var matches = new List<TrackedEventUnregister>();

            foreach (var unregister in m_EventUnregisters) {
                if (unregister.Matches(onEvent, EEventSubscriptionSource.Architecture)) {
                    matches.Add(unregister);
                }
            }

            foreach (var unregister in matches) {
                unregister.Unregister();
            }
        }

        public void Publish<T>(T message, EEventPropagation propagation = EEventPropagation.Local) {
            EnsureNotDisposed();

            if (propagation is not EEventPropagation.Local and not EEventPropagation.Bubble) {
                throw new ArgumentOutOfRangeException(nameof(propagation), propagation, "Unknown propagation mode.");
            }

            List<Exception> exceptions = null;

            for (var scope = this; scope != null;
                 scope = propagation == EEventPropagation.Bubble ? scope.Parent : null) {
                try {
                    scope.m_Events.Publish(message);
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
}