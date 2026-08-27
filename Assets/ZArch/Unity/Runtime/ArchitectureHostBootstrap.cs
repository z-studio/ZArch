using System;
using UnityEngine;

namespace ZArch.Unity {
    public abstract class ArchitectureHostBootstrap : MonoBehaviour {
        public Architecture Architecture { get; private set; }
        public ArchitectureScope RootScope { get; private set; }

        protected virtual bool DontDestroy => true;
        protected virtual string RootScopeName => "App";

        protected abstract Architecture CreateArchitecture();
        protected abstract void ConfigureRoot(ArchitectureScope scope);

        protected virtual void Awake() {
            if (DontDestroy) {
                DontDestroyOnLoad(gameObject);
            }

            Architecture = CreateArchitecture()
                           ?? throw new InvalidOperationException("CreateArchitecture returned null.");

            try {
                Architecture.Start();
                Architecture.ExceptionHandler = Debug.LogException;
                RootScope = Architecture.CreateRootScope(RootScopeName, ConfigureRoot);
            } catch {
                Architecture.Shutdown();
                Architecture = null;
                throw;
            }
        }

        protected virtual void OnDestroy() {
            Architecture?.Shutdown();
            Architecture = null;
            RootScope = null;
        }
    }
}