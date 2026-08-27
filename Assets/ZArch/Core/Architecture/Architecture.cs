using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZArch {
    public abstract partial class Architecture : IDisposable {
        private readonly List<ArchitectureScope> m_RootScopes = new();
        private readonly List<ArchitectureScope> m_AllScopes = new();
        private readonly List<ArchitectureScope> m_PendingScopes = new();
        private readonly ReadOnlyCollection<ArchitectureScope> m_RootScopesView;
        private readonly ReadOnlyCollection<ArchitectureScope> m_AllScopesView;
        private readonly TypeEventSystem m_EventSystem = new();
        private bool m_IsShuttingDown;
        private bool m_HasStartedLifecycle;
        private bool m_IsTerminated;

        public bool IsStarted { get; private set; }
        public Action<Exception> ExceptionHandler { get; set; }
        public IReadOnlyList<ArchitectureScope> RootScopes => m_RootScopesView;
        public IReadOnlyList<ArchitectureScope> Scopes => m_AllScopesView;
        public event Action<ArchitectureScope> ScopeConfiguring;

        protected Architecture() {
            m_RootScopesView = m_RootScopes.AsReadOnly();
            m_AllScopesView = m_AllScopes.AsReadOnly();
        }

        protected virtual void OnStart() { }
        protected virtual void OnShutdown() { }
    }
}
