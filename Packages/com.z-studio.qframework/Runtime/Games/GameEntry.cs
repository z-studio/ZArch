using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    /// <summary>放在游戏根 Prefab 上。业务初始化写在 OnEnterAsync，清理写在 OnExitAsync。</summary>
    public abstract class GameEntry : SessionBehaviour {
        public IGameResources GameResources => this.GetUtility<IGameResources>();

        internal async Task EnterAsync(Session session, CancellationToken cancellation) {
            foreach (var controller in GetComponentsInChildren<SessionBehaviour>(true)) {
                controller.BindSession(session);
            }

            await OnEnterAsync(cancellation);
        }

        internal Task ExitAsync(CancellationToken cancellation) => OnExitAsync(cancellation);
        protected abstract Task OnEnterAsync(CancellationToken cancellation);
        protected virtual Task OnExitAsync(CancellationToken cancellation) => Task.CompletedTask;
    }
}