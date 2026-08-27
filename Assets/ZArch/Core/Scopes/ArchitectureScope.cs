using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace ZArch {
    public sealed partial class ArchitectureScope : IServiceResolver, IDisposable {
        private readonly List<ArchitectureScope> m_Children = new();
        private readonly ReadOnlyCollection<ArchitectureScope> m_ChildrenView;
        private readonly Dictionary<Type, ServiceRegistration> m_Registrations = new();
        private readonly List<ServiceRegistration> m_RegistrationOrder = new();
        private readonly List<object> m_OwnedInstances = new();
        private readonly HashSet<object> m_OwnedSet = new(ReferenceEqualityComparer.sInstance);
        private readonly List<object> m_InitializedInstances = new();
        private readonly HashSet<object> m_InitializedSet = new(ReferenceEqualityComparer.sInstance);
        private readonly TypeEventSystem m_Events = new();
        private readonly HashSet<TrackedEventUnregister> m_EventUnregisters = new();
        private readonly CancellationTokenSource m_LifetimeCts = new();
        private int m_NextRegistrationOrder;

        public string Name { get; }
        public object Tag { get; }
        public ArchitectureScope Parent { get; }
        public Architecture Architecture { get; }
        public EScopeState State { get; private set; } = EScopeState.Created;
        public bool IsDisposed => State == EScopeState.Disposed;
        public bool IsActivated => State == EScopeState.Active;
        public bool IsActivating => State is EScopeState.Configuring or EScopeState.Initializing;

        public IReadOnlyList<ArchitectureScope> Children => m_ChildrenView;
        internal IReadOnlyList<object> OwnedInstances => m_OwnedInstances;
        internal IReadOnlyList<ArchitectureScope> ChildScopes => m_Children;
        internal CancellationToken LifetimeToken => m_LifetimeCts.Token;

        internal ArchitectureScope(Architecture architecture, string name, ArchitectureScope parent, object tag) {
            Architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
            m_ChildrenView = m_Children.AsReadOnly();

            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Scope name is empty.", nameof(name))
                : name;

            Parent = parent;
            Tag = tag;
        }

        internal void AddChild(ArchitectureScope child) {
            if (child == null) {
                throw new ArgumentNullException(nameof(child));
            }

            if (!m_Children.Contains(child)) {
                m_Children.Add(child);
            }
        }

    }
}
