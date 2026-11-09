using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZArch.Unity {
    public class GameEntry : ArchitectureEntry {
        [SerializeField]
        private GameDefinition m_StandaloneGame;

        private Architecture m_Standalone;
        private bool m_IsManaged;
        private bool m_EnterStarted;
        private bool m_ExitCallbackCompleted;
        private bool m_Exited;
        private Task m_Exit;
        public GameDefinition StandaloneGame => m_StandaloneGame;

        internal void MarkManaged() => m_IsManaged = true;

        internal void BindEnvironment(ArchitectureScope scope) {
            if (Scope != null) {
                if (ReferenceEquals(Scope, scope)) {
                    return;
                }

                throw new InvalidOperationException($"{name}: game entry already belongs to another environment.");
            }

            if (scope?.IsActive != true) {
                throw new InvalidOperationException($"{name}: game environment is not active.");
            }

            ValidateViews();
            Scope = scope;
            scope.RegisterShutdown(ExitGameAsync, () => {
                if (m_EnterStarted && !m_Exited) {
                    throw new InvalidOperationException("Use ShutdownAsync to exit this game.");
                }

                DeactivateViews();
            });
        }

        // 场景加载期间只绑定环境。OnEnterAsync 完成后才激活所有 View。
        internal Task InitializeGameAsync(object arguments, CancellationToken cancellationToken) =>
            InitializeOnceAsync(async () => {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, LifetimeToken);
                m_EnterStarted = true;
                await OnEnterAsync(arguments, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                ActivateViews();
            });

        protected virtual void Start() {
            if (m_IsManaged || Scope != null) {
                return;
            }

            _ = ObserveInitializationAsync(InitializeOnceAsync(InitializeStandaloneAsync));
        }

        private async Task InitializeStandaloneAsync() {
            if (!m_StandaloneGame) {
                throw new InvalidOperationException($"{name}: assign Standalone Game to play this scene directly.");
            }

            m_StandaloneGame.Validate();
            m_Standalone = new Architecture { UnhandledExceptionHandler = Debug.LogException };

            try {
                await m_Standalone.StartAsync(ConfigureStandaloneServices, LifetimeToken);
                var gameScope = await m_Standalone.RootScope.CreateChildAsync("Game:" + m_StandaloneGame.Id,
                    (services, token) => {
                        ((IGameDefinition)m_StandaloneGame).Configure(services);
                        return Task.CompletedTask;
                    }, cancellationToken: LifetimeToken);
                BindEnvironment(gameScope);
                m_EnterStarted = true;
                await OnEnterAsync(null, LifetimeToken);
                LifetimeToken.ThrowIfCancellationRequested();
                ActivateViews();
            } catch (Exception startupError) {
                try {
                    await m_Standalone.ShutdownAsync();
                } catch (Exception cleanupError) {
                    throw new AggregateException(startupError, cleanupError);
                }

                throw;
            }
        }

        internal Task ExitGameAsync() {
            if (m_Exited) {
                return Task.CompletedTask;
            }

            if (m_Exit is { IsFaulted: false, IsCanceled: false }) {
                return m_Exit;
            }

            return m_Exit = ExitGameCoreAsync();
        }

        private async Task ExitGameCoreAsync() {
            if (m_EnterStarted && !m_ExitCallbackCompleted) {
                await OnExitAsync();
                m_ExitCallbackCompleted = true;
            }

            DeactivateViews();
            m_Exited = true;
        }

        protected virtual void ConfigureStandaloneServices(ArchitectureScope services) {
        }

        protected virtual Task OnEnterAsync(object arguments, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        protected virtual Task OnExitAsync() => Task.CompletedTask;

        protected override async Task ShutdownCoreAsync() {
            await ExitGameAsync();

            if (m_Standalone != null) {
                await m_Standalone.ShutdownAsync();
            }
        }
    }
}