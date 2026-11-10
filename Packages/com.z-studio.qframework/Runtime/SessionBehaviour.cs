using System;
using UnityEngine;

namespace QFramework {
    /// <summary>游戏子组件的会话入口。会话由加载器或界面打开参数绑定，不使用静态游戏架构。</summary>
    public abstract class SessionBehaviour : MonoBehaviour, IController, IEventRegistrationOwner {
        private IEventRegistrationOwner m_EventOwner;
        public Session Session { get; private set; }

        public IArchitecture GetArchitecture() =>
            Session is { IsDisposed: false }
                ? Session : throw new InvalidOperationException(
                    "Bind an active Session before using business services.");

        public void BindSession(Session session, IEventRegistrationOwner eventOwner = null) {
            if (session == null || session.IsDisposed) {
                throw new ArgumentException("An active Session is required.", nameof(session));
            }

            Session = session;
            m_EventOwner = eventOwner;
        }

        public IUnregister RegisterEvent<T>(Action<T> handler) =>
            m_EventOwner != null
                ? m_EventOwner.RegisterEvent(handler) : GetArchitecture().RegisterEvent(handler);

        public void UnregisterEvent<T>(Action<T> handler) {
            if (m_EventOwner != null) {
                m_EventOwner.UnregisterEvent(handler);
            } else if (Session is { IsDisposed: false }) {
                Session.UnregisterEvent(handler);
            }
        }

        public void UnbindSession() {
            Session = null;
            m_EventOwner = null;
        }
    }
}