using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace QFramework {
    /// <summary>放在游戏根 Prefab 上。业务初始化写在 OnEnterAsync，清理写在 OnExitAsync。</summary>
    public abstract class GameEntry : MonoBehaviour, IController {
        public Session Session { get; private set; }

        public IArchitecture GetArchitecture() =>
            Session ?? throw new InvalidOperationException("Game has not entered.");

        internal async Task EnterAsync(Session session, CancellationToken cancellation) {
            Session = session;
            await OnEnterAsync(cancellation);
        }

        internal Task ExitAsync(CancellationToken cancellation) => OnExitAsync(cancellation);
        protected abstract Task OnEnterAsync(CancellationToken cancellation);
        protected virtual Task OnExitAsync(CancellationToken cancellation) => Task.CompletedTask;
    }
}