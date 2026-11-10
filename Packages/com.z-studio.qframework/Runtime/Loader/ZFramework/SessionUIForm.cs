using System;
using System.Collections.Generic;
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

    public abstract class SessionUIForm : UGuiForm, IController {
        private readonly List<IUnregister> m_Listeners = new();
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
            base.OnOpen(userData);
        }

        protected override void OnClose(bool isShutdown, object userData) {
            ClearListeners();
            base.OnClose(isShutdown, userData);

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

            ClearListeners();
            Session = context.Session;
            ViewData = context.Data;

            foreach (var controller in GetComponentsInChildren<SessionBehaviour>(true)) {
                controller.BindSession(Session);
            }
        }

        /// <summary>界面关闭时取消订阅，账号注销也会取消；池中对象不把订阅带到下一次打开。</summary>
        protected void Listen<T>(Action<T> handler) => m_Listeners.Add(Session.RegisterEvent(handler));

        protected void Listen(IUnregister subscription) => m_Listeners.Add(Session.Track(subscription));

        private void ClearListeners() {
            foreach (var listener in m_Listeners) {
                listener.Unregister();
            }

            m_Listeners.Clear();
        }
    }
}