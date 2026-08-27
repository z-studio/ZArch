using System;
using System.Collections.Generic;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        private sealed class TrackedEventUnregister : IUnregister {
            private ArchitectureScope m_Owner;
            private IUnregister m_Unregister;

            public TrackedEventUnregister(ArchitectureScope owner, IUnregister unregister) {
                m_Owner = owner;
                m_Unregister = unregister;
            }

            public void Unregister() {
                var unregister = m_Unregister;

                if (unregister == null) {
                    return;
                }

                m_Unregister = null;
                var owner = m_Owner;
                m_Owner = null;
                owner?.m_EventUnregisters.Remove(this);
                unregister.Unregister();
            }
        }

        public IUnregister RegisterEvent<T>(Action<T> onEvent) {
            EnsureNotDisposed();

            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            var tracked = new TrackedEventUnregister(this, m_Events.Register(onEvent));
            m_EventUnregisters.Add(tracked);
            return tracked;
        }

        public void Publish<T>(T message, EEventPropagation propagation = EEventPropagation.Local) {
            EnsureNotDisposed();

            if (propagation is not EEventPropagation.Local and not EEventPropagation.Parents) {
                throw new ArgumentOutOfRangeException(nameof(propagation), propagation, "Unknown propagation mode.");
            }

            List<Exception> exceptions = null;

            for (var scope = this; scope != null;
                 scope = propagation == EEventPropagation.Parents ? scope.Parent : null) {
                try {
                    scope.m_Events.Send(message);
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
