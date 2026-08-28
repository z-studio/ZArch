using System;
using UnityEngine;

namespace ZArch.Unity {
    public abstract class ArchitectureBootstrap : MonoBehaviour {
        private bool m_WasExplicitlyShutdown;

        public Architecture Architecture { get; private set; }
        public ArchitectureScope RootScope { get; private set; }

        protected virtual bool PersistAcrossScenes => true;
        protected virtual bool RequiresExplicitShutdown => false;
        protected virtual string RootScopeName => "App";

        protected virtual Architecture CreateArchitecture() => new();
        protected abstract void ConfigureRoot(ArchitectureScope scope);

        protected virtual void Awake() {
            if (PersistAcrossScenes) {
                DontDestroyOnLoad(gameObject);
            }

            Architecture = CreateArchitecture()
                           ?? throw new InvalidOperationException("CreateArchitecture returned null.");

            try {
                Architecture.Start();
                Architecture.UnhandledExceptionHandler = Debug.LogException;
                RootScope = Architecture.CreateRootScope(RootScopeName, ConfigureRoot);
            } catch {
                Architecture.Shutdown();
                Architecture = null;
                throw;
            }
        }

        protected void ShutdownArchitecture() {
            m_WasExplicitlyShutdown = true;
            ShutdownArchitectureCore();
        }

        protected virtual void OnDestroy() {
            if (RequiresExplicitShutdown
                && !m_WasExplicitlyShutdown
                && Architecture != null
                && Architecture.IsStarted) {
                Debug.LogError(
                    $"{GetType().Name} was destroyed before its explicit asynchronous shutdown completed. "
                    + "Await the project's shutdown method before destroying the Bootstrap.",
                    this
                );
            }

            ShutdownArchitectureCore();
        }

        private void ShutdownArchitectureCore() {
            Architecture?.Shutdown();
            Architecture = null;
            RootScope = null;
        }
    }
}
