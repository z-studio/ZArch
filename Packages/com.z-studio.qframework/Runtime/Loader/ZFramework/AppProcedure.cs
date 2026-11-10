using System;
using System.Threading;
using System.Threading.Tasks;
using ZFramework.Procedure;
using ZFramework.Unity;
using ProcedureOwner = ZFramework.Fsm.IFsm<ZFramework.Procedure.IProcedureManager>;

namespace QFramework {
    /// <summary>Procedure 管显示与项目步骤，AppFlow 管账号/游戏生命周期。OnLeave 不重复释放会话。</summary>
    public abstract class AppProcedure : ProcedureBase, IController {
        protected abstract AppFlow Flow { get; }
        private CancellationTokenSource m_Visible;

        /// <summary>仅取消当前界面的异步工作；不要用它取消账号或游戏会话。</summary>
        protected CancellationToken VisibleToken => m_Visible.Token;

        public IArchitecture GetArchitecture() => Flow.UserSession ?? Flow.AppArchitecture;

        protected override void OnEnter(ProcedureOwner owner) {
            base.OnEnter(owner);
            m_Visible = new CancellationTokenSource();
        }

        protected override void OnLeave(ProcedureOwner owner, bool isShutdown) {
            m_Visible.Cancel();
            m_Visible.Dispose();
            m_Visible = null;
            base.OnLeave(owner, isShutdown);
        }

        /// <summary>仅在稳定状态切换显示流程。加载、注销和失败期间留在原流程显示进度/重试。</summary>
        protected void FollowFlow<TLogin, TLobby, TGame>(ProcedureOwner owner)
            where TLogin : ProcedureBase where TLobby : ProcedureBase where TGame : ProcedureBase {
            switch (Flow.State) {
                case AppState.Login:
                    if (this is not TLogin) {
                        ChangeState<TLogin>(owner);
                    }

                    break;
                case AppState.Lobby:
                    if (this is not TLobby) {
                        ChangeState<TLobby>(owner);
                    }

                    break;
                case AppState.Game:
                    if (this is not TGame) {
                        ChangeState<TGame>(owner);
                    }

                    break;
            }
        }

        // Unity/ZFramework 的入口不返回 Task，统一观察异常；业务本身继续返回 Task。
        protected async void Observe(Task task) {
            try {
                await task;
            } catch (OperationCanceledException) {
            } catch (Exception error) {
                Log.Error(error.ToString());
            }
        }
    }
}