using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    public partial class Architecture {
        private ArchitectureScope m_DefaultScope;

        /// <summary>应用的根环境。独立运行环境请创建另一个 Architecture。</summary>
        public ArchitectureScope RootScope {
            get {
                EnsureStarted();
                return m_DefaultScope ??= CreateRootScope("App", scope => m_DefaultScope = scope);
            }
        }

        public void Start(Action<ArchitectureScope> configure, string rootScopeName = "App") {
            if (configure == null) {
                throw new ArgumentNullException(nameof(configure));
            }

            Start();

            try {
                m_DefaultScope = CreateRootScope(rootScopeName, scope => {
                    m_DefaultScope = scope;
                    configure(scope);
                });
            } catch (Exception startupError) {
                try {
                    Shutdown();
                } catch (Exception cleanupError) {
                    throw new AggregateException(startupError, cleanupError);
                }

                throw;
            }
        }

        public async Task StartAsync(Action<ArchitectureScope> configure,
            CancellationToken cancellationToken = default) {
            if (configure == null) {
                throw new ArgumentNullException(nameof(configure));
            }

            Start();

            try {
                m_DefaultScope = await CreateRootScopeAsync("App", (scope, token) => {
                    token.ThrowIfCancellationRequested();
                    m_DefaultScope = scope;
                    configure(scope);
                    return Task.CompletedTask;
                }, cancellationToken: cancellationToken);
            } catch (Exception startupError) {
                try {
                    await ShutdownAsync();
                } catch (Exception cleanupError) {
                    throw new AggregateException(startupError, cleanupError);
                }

                throw;
            }
        }

        public T Get<T>() where T : class => RootScope.Resolve<T>();
        public T GetModel<T>() where T : class, IModel => Get<T>();
        public T GetSystem<T>() where T : class, ISystem => Get<T>();
    }
}