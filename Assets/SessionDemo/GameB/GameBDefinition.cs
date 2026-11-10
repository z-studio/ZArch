using QFramework;
using UnityEngine;

namespace Demo.GameB {
    [CreateAssetMenu(menuName = "Session Demo/GameB")]
    public sealed class GameBDefinition : GameDefinition {
        public override void Configure(Session session) {
            session.RegisterModel(new RoundModel());
        }
    }
}