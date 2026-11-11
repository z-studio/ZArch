using QFramework;
using ProcedureOwner = ZFramework.Fsm.IFsm<ZFramework.Procedure.IProcedureManager>;

namespace Demo.App {
    // Inspector 的 ProcedureComponent 注册这四个类，入口为 ProcedureLaunch。
    // Procedure 不创建/销毁会话，不在 OnLeave 卸载游戏；Flow 统一处理。
    public abstract class DemoProcedure : AppProcedure {
        protected override AppFlow Flow => AppEntry.Instance.Flow;

        protected override void OnUpdate(ProcedureOwner owner, float elapsed, float realElapsed) {
            base.OnUpdate(owner, elapsed, realElapsed);

            if (AppEntry.Instance == null || Flow == null) {
                return;
            }

            AppEntry.Instance.RenderView();
            FollowFlow<ProcedureLogin, ProcedureLobby, ProcedureGame>(owner);
        }
    }

    [UnityEngine.Scripting.Preserve]
    public sealed class ProcedureLaunch : DemoProcedure {
        protected override void OnEnter(ProcedureOwner owner) {
            base.OnEnter(owner);
            AppEntry.Instance.StartApp();
        }
    }

    [UnityEngine.Scripting.Preserve]
    public sealed class ProcedureLogin : DemoProcedure {
    }

    [UnityEngine.Scripting.Preserve]
    public sealed class ProcedureLobby : DemoProcedure {
    }

    [UnityEngine.Scripting.Preserve]
    public sealed class ProcedureGame : DemoProcedure {
    }

    // 同一资源包可进入不同业务房间。参数在 App 程序集定义，App 无需引用游戏类型。
    public sealed class GameLaunchData {
        public string RoomId { get; }
        public GameLaunchData(string roomId) => RoomId = roomId;
    }
}