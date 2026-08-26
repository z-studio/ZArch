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
        public static IUnregister AddToUnregisterList(this IUnregister unregister, IUnregisterList list) {
            if (unregister == null) {
                throw new ArgumentNullException(nameof(unregister));
            }

            if (list == null) {
                throw new ArgumentNullException(nameof(list));
            }

            list.UnregisterList.Add(unregister);
            return unregister;
        }

        public static void UnregisterAll(this IUnregisterList self) {
            if (self == null) {
                throw new ArgumentNullException(nameof(self));
            }

            List<Exception> exceptions = null;

            for (var i = self.UnregisterList.Count - 1; i >= 0; i--) {
                try {
                    self.UnregisterList[i]?.Unregister();
                } catch (Exception exception) {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(exception);
                }
            }

            self.UnregisterList.Clear();

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