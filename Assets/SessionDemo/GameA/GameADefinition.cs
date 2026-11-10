using QFramework;
using UnityEngine;

namespace Demo.GameA {
    [CreateAssetMenu(menuName = "Session Demo/GameA")]
    public sealed class GameADefinition : GameDefinition {
        public override void Configure(Session session) {
            session.RegisterModel(new RoundModel());
        }
    }
}