using System;
using UnityEngine;

namespace QFramework {
    /// <summary>游戏子组件的会话入口。会话由加载器或界面打开参数绑定，不使用静态游戏架构。</summary>
    public abstract class SessionBehaviour : MonoBehaviour, IController {
        public Session Session { get; private set; }

        public IArchitecture GetArchitecture() =>
            Session is { IsDisposed: false }
                ? Session : throw new InvalidOperationException(
                    "Bind an active Session before using business services.");

        public void BindSession(Session session) {
            if (session == null || session.IsDisposed) {
                throw new ArgumentException("An active Session is required.", nameof(session));
            }

            Session = session;
        }

        public void UnbindSession() => Session = null;
    }
}