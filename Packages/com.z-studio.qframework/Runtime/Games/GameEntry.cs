using System.Threading;
using System.Threading.Tasks;

namespace QFramework {
    /// <summary>
    /// 用户编写业务的 Unity 组件，放在根 Prefab 或场景的未激活根对象上。
    /// 加载器负责创建/激活/销毁对象；这里先绑定会话，再调用 OnEnterAsync / OnExitAsync。
    /// </summary>
    public abstract class GameEntry : SessionBehaviour {
        public IGameResources GameResources => this.GetUtility<IGameResources>();

        internal async Task EnterAsync(Session session, CancellationToken cancellation) {
            // 先绑定整棵业务子层级，避免 OnEnter 或随后 OnEnable 取得未绑定的架构。
            foreach (var controller in GetComponentsInChildren<SessionBehaviour>(true)) {
                controller.BindSession(session);
            }

            await OnEnterAsync(cancellation);
        }

        internal Task ExitAsync(CancellationToken cancellation) => OnExitAsync(cancellation);

        /// <summary>此时 Model/System 已初始化。等待必要业务就绪后返回，加载器才激活游戏根对象。</summary>
        protected abstract Task OnEnterAsync(CancellationToken cancellation);

        /// <summary>对象与会话此时仍存在。停止业务、等待外部连接/额外场景清理；允许部分进入后调用和重试。</summary>
        protected virtual Task OnExitAsync(CancellationToken cancellation) => Task.CompletedTask;
    }
}