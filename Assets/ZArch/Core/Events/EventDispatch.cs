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
}
