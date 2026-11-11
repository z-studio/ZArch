using QFramework;
using UnityEngine;

namespace Demo.GameB {
    [CreateAssetMenu(menuName = "Session Demo/GameB")]
    public sealed class GameBDefinition : GameDefinition {
        public override void Configure(Session session) {
            // 只注册游戏自己的数据，账号和 App 的数据从父级会话取得。
            session.RegisterModel(new RoundModel());
        }
    }
}