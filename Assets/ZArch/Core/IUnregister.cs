using System;
using System.Collections.Generic;

namespace ZArch {
    public interface IUnregister {
        void Unregister();
    }

    public interface IUnregisterList {
        List<IUnregister> UnregisterList { get; }
    }

    public static class UnregisterListExtension {
        private sealed class TrackedUnregister : IUnregister {
            private List<IUnregister> m_Owner;
            private IUnregister m_Unregister;

            public TrackedUnregister(List<IUnregister> owner, IUnregister unregister) {
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
                owner?.Remove(this);
                unregister.Unregister();
            }
        }

        public static IUnregister AddToUnregisterList(this IUnregister unregister, IUnregisterList list) {
            if (unregister == null) {
                throw new ArgumentNullException(nameof(unregister));
            }

            if (list == null) {
                throw new ArgumentNullException(nameof(list));
            }

            var tracked = new TrackedUnregister(list.UnregisterList, unregister);
            list.UnregisterList.Add(tracked);
            return tracked;
        }

        public static void UnregisterAll(this IUnregisterList self) {
            if (self == null) {
                throw new ArgumentNullException(nameof(self));
            }

            List<Exception> exceptions = null;
            var unregisters = self.UnregisterList.ToArray();
            self.UnregisterList.Clear();

            for (var i = unregisters.Length - 1; i >= 0; i--) {
                try {
                    unregisters[i]?.Unregister();
                } catch (Exception exception) {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(exception);
                }
            }

            if (exceptions != null) {
                throw new AggregateException(exceptions);
            }
        }
    }

    public sealed class CustomUnregister : IUnregister {
        private Action m_OnUnregister;

        public CustomUnregister(Action onUnregister) =>
            m_OnUnregister = onUnregister ?? throw new ArgumentNullException(nameof(onUnregister));

        public void Unregister() {
            var action = m_OnUnregister;
            m_OnUnregister = null;
            action?.Invoke();
        }
    }
}
