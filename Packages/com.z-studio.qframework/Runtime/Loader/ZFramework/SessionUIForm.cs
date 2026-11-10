using System;
using ZFramework.Unity;

namespace QFramework {
    /// <summary>将会话与业务打开参数一起传给 ZFramework UI，不改变 UI 的池与生命周期。</summary>
    public sealed class SessionUIContext {
        public Session Session { get; }
        public object Data { get; }

        public SessionUIContext(Session session, object data = null) {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Data = data;
        }
    }

    public abstract class SessionUIForm : UGuiForm, IController, IEventRegistrationOwner {
        private readonly ControllerSubscriptions m_Subscriptions = new("OnOpen");
        public Session Session { get; private set; }
        protected object ViewData { get; private set; }

        public IArchitecture GetArchitecture() =>
            Session != null && !Session.IsDisposed
                ? Session : throw new InvalidOperationException("Open this form with an active SessionUIContext.");

        protected override void OnInit(object userData) {
            Bind(userData);
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData) {
            // 池中复用的界面必须重新绑定，不能继续使用上次登录的会话。
            Bind(userData);
            m_Subscriptions.Begin(GetArchitecture);
            base.OnOpen(userData);
        }

        protected override void OnClose(bool isShutdown, object userData) {
            try {
                m_Subscriptions.Clear();
            } finally {
                try {
                    base.OnClose(isShutdown, userData);
                } finally {
                    ClearBinding();
                }
            }
        }

        protected virtual void OnDestroy() {
            try {
                m_Subscriptions.Clear();
            } finally {
                ClearBinding();
            }
        }

        private void ClearBinding() {
            foreach (var controller in GetComponentsInChildren<SessionBehaviour>(true)) {
                controller.UnbindSession();
            }

            Session = null;
            ViewData = null;
        }

        private void Bind(object data) {
            if (!(data is SessionUIContext context) || context.Session.IsDisposed) {
                throw new ArgumentException("SessionUIForm requires an active SessionUIContext.", nameof(data));
            }

            m_Subscriptions.Clear();
            Session = context.Session;
            ViewData = context.Data;

            foreach (var controller in GetComponentsInChildren<SessionBehaviour>(true)) {
                controller.BindSession(Session, this);
            }
        }

        // 在 OnOpen 中照常注册；关闭/销毁时自动解除，池中复用后重新注册。
        public IUnregister RegisterEvent<T>(Action<T> handler) => m_Subscriptions.RegisterEvent(handler);
        public void UnregisterEvent<T>(Action<T> handler) => m_Subscriptions.UnregisterEvent(handler);

        /// <summary>属性或外部事件订阅同时跟随窗口关闭和会话结束。</summary>
        protected IUnregister Track(IUnregister subscription) => m_Subscriptions.Track(subscription);
    }
}