using System;
using System.Collections.Generic;

namespace QFramework {
    // UI / Procedure 共用的显示期订阅记录。业务只使用普通 RegisterEvent，无需操作本类型。
    internal sealed class ControllerSubscriptions {
        private readonly List<Subscription> m_Subscriptions = new();
        private readonly string m_EnterMethod;
        // 非空表示窗口/Procedure 当前可见；每条 Subscription 另有自己的活动标记，避免旧派发跨过重开周期。
        private Func<IArchitecture> m_Architecture;

        public ControllerSubscriptions(string enterMethod) => m_EnterMethod = enterMethod;

        public void Begin(Func<IArchitecture> architecture) {
            Clear();
            m_Architecture = architecture;
        }

        private IArchitecture RequireArchitecture() =>
            m_Architecture != null
                ? m_Architecture() : throw new InvalidOperationException(
                    $"Register subscriptions in {m_EnterMethod} after its base call, not in OnInit or while inactive.");

        public IUnregister RegisterEvent<T>(Action<T> handler) {
            if (handler == null) {
                throw new ArgumentNullException(nameof(handler));
            }

            var architecture = RequireArchitecture();
            var subscription = new Subscription(this, typeof(T), handler);
            var inner = architecture.RegisterEvent<T>(message => {
                // QF 同步派发可能已捕获委托；关闭或重新打开后，旧的委托也不能继续调用。
                if (subscription.IsActive) {
                    handler(message);
                }
            });
            subscription.Attach(inner);
            m_Subscriptions.Add(subscription);
            return subscription;
        }

        public void UnregisterEvent<T>(Action<T> handler) {
            for (var i = m_Subscriptions.Count - 1; i >= 0; i--) {
                var subscription = m_Subscriptions[i];

                if (subscription.EventType == typeof(T) && Equals(subscription.Handler, handler)) {
                    subscription.Unregister();
                    return;
                }
            }
        }

        public IUnregister Track(IUnregister subscription) {
            if (subscription == null) {
                throw new ArgumentNullException(nameof(subscription));
            }

            IArchitecture architecture;

            try {
                architecture = RequireArchitecture();
            } catch {
                subscription.Unregister();
                throw;
            }

            // Session.Track 负责其自身失败时的回滚，避免重复调用外部解除动作。
            if (architecture is Session session) {
                subscription = session.Track(subscription);
            }

            var tracked = new Subscription(this, null, null);
            tracked.Attach(subscription);
            m_Subscriptions.Add(tracked);
            return tracked;
        }

        public void Clear() {
            m_Architecture = null;
            var errors = new List<Exception>();

            foreach (var subscription in m_Subscriptions.ToArray()) {
                try {
                    subscription.Unregister();
                } catch (Exception error) {
                    errors.Add(error);
                }
            }

            if (errors.Count > 0) {
                throw new AggregateException("Controller subscription cleanup failed.", errors);
            }
        }

        private sealed class Subscription : IUnregister {
            private readonly ControllerSubscriptions m_Owner;
            private IUnregister m_Inner;
            public Type EventType { get; private set; }
            public Delegate Handler { get; private set; }
            public bool IsActive => m_Inner != null && m_Owner.m_Architecture != null;

            public Subscription(ControllerSubscriptions owner, Type eventType, Delegate handler) {
                m_Owner = owner;
                EventType = eventType;
                Handler = handler;
            }

            public void Attach(IUnregister inner) => m_Inner = inner;

            public void Unregister() {
                var inner = m_Inner;

                if (inner == null) {
                    return;
                }

                m_Inner = null;
                EventType = null;
                Handler = null;
                m_Owner.m_Subscriptions.Remove(this);
                inner.Unregister();
            }
        }
    }
}