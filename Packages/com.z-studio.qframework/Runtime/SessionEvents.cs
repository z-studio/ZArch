using System;

namespace QFramework {
    public static class SessionEvents {
        // 使用 QF 原有能力接口，保持 Controller/Model/System 的分层约束。
        public static void SendAppEvent<T>(this ICanSendEvent self, T message) {
            var architecture = self.GetArchitecture();

            if (architecture is Session session) {
                if (session.IsDisposed) {
                    throw new ObjectDisposedException(session.Name);
                }

                architecture = session.App;
            }

            architecture.SendEvent(message);
        }

        public static IUnregister RegisterAppEvent<T>(this ICanRegisterEvent self, Action<T> handler) {
            var architecture = self.GetArchitecture();
            return architecture is Session session
                ? session.RegisterAppEvent(handler)
                : architecture.RegisterEvent(handler);
        }

        public static IUnregister UnregisterWhenSessionEnds(this IUnregister subscription, Session session) =>
            session.Track(subscription);
    }
}