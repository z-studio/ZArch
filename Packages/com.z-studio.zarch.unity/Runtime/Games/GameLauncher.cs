using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZArch.Unity {
    public class GameLauncher : SceneArchitecture {
        [SerializeField]
        private GameDefinition[] m_Games = Array.Empty<GameDefinition>();

        public GameSwitcher Games { get; private set; }
        public System.Collections.Generic.IReadOnlyList<GameDefinition> Definitions => m_Games;
        protected override bool KeepAlive => true;

        internal override void RegisterFrameworkServices(ArchitectureScope services) {
            Games = new GameSwitcher(services, CreateContentLoader(), m_Games);
            services.Register(Games);
        }

        protected virtual IGameContentLoader CreateContentLoader() => new UnityGameContentLoader();

        public Task OpenAsync(string id, object arguments = null, CancellationToken cancellationToken = default) {
            if (!IsReady) {
                throw new InvalidOperationException($"{name}: await Initialization before opening a game.");
            }

            return Games.OpenAsync(id, arguments, cancellationToken);
        }

        public Task CloseAsync() => Games?.CloseAsync() ?? Task.CompletedTask;
    }
}