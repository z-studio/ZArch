using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZArch.Unity {
    public abstract class AsyncArchitectureBootstrap : MonoBehaviour {
        private CancellationTokenSource m_LifetimeCts;
        private bool m_WasExplicitlyShutdown;

        public Architecture Architecture { get; private set; }
        public ArchitectureScope RootScope { get; private set; }
        public Task Initialization { get; private set; } = Task.CompletedTask;

        protected virtual bool PersistAcrossScenes => true;
        protected virtual bool RequiresExplicitShutdown => true;
        protected virtual string RootScopeName => "App";

        protected virtual Architecture CreateArchitecture() => new();

        protected abstract Task ConfigureRootAsync(
            ArchitectureScope scope,
            CancellationToken cancellationToken
        );

        protected virtual void Awake() {
            if (PersistAcrossScenes) {
                DontDestroyOnLoad(gameObject);
            }

            m_LifetimeCts = new CancellationTokenSource();
            Architecture = CreateArchitecture()
                           ?? throw new InvalidOperationException("CreateArchitecture returned null.");
            Architecture.Start();
            Architecture.UnhandledExceptionHandler = Debug.LogException;
            Initialization = InitializeAsync(m_LifetimeCts.Token);
        }

        private async Task InitializeAsync(CancellationToken cancellationToken) {
            var architecture = Architecture;

            try {
                RootScope = await architecture.CreateRootScopeAsync(
                    RootScopeName,
                    ConfigureRootAsync,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(true);
            } catch {
                await architecture.ShutdownAsync(CancellationToken.None).ConfigureAwait(true);
                throw;
            }
        }

        protected async Task ShutdownArchitectureAsync(CancellationToken cancellationToken = default) {
            if (Architecture == null) {
                return;
            }

            m_WasExplicitlyShutdown = true;
            m_LifetimeCts?.Cancel();
            Exception initializationException = null;

            try {
                await Initialization.ConfigureAwait(true);
            } catch (OperationCanceledException) when (m_LifetimeCts?.IsCancellationRequested == true) {
                // Expected when shutdown interrupts initialization.
            } catch (Exception exception) {
                initializationException = exception;
            }

            try {
                await Architecture.ShutdownAsync(cancellationToken).ConfigureAwait(true);
            } finally {
                ClearArchitecture();
            }

            if (initializationException != null) {
                ExceptionDispatchInfo.Capture(initializationException).Throw();
            }
        }

        protected virtual void OnDestroy() {
            m_LifetimeCts?.Cancel();

            if (RequiresExplicitShutdown
                && !m_WasExplicitlyShutdown
                && Architecture != null
                && Architecture.IsStarted) {
                Debug.LogError(
                    $"{GetType().Name} was destroyed before its asynchronous shutdown completed. "
                    + "Await ShutdownArchitectureAsync before destroying the Bootstrap.",
                    this
                );
            }

            Architecture?.Shutdown();
            ClearArchitecture();
        }

        private void ClearArchitecture() {
            Architecture = null;
            RootScope = null;
            m_LifetimeCts?.Dispose();
            m_LifetimeCts = null;
        }
    }
}
