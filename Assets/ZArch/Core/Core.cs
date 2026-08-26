using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ZArch {
    public enum EScopeState {
        Created,
        Configuring,
        Initializing,
        Active,
        Disposing,
        Disposed,
        Faulted
    }

    public enum EServiceLifetime {
        Scoped,
        Transient
    }

    public enum EEventPropagation {
        Local,
        Parents
    }

    public interface IServiceResolver {
        object Resolve(Type serviceType);
        bool TryResolve(Type serviceType, out object instance);
        T Resolve<T>() where T : class;
        bool TryResolve<T>(out T instance) where T : class;
    }

    public interface IBelongToScope {
        ArchitectureScope GetScope();
    }

    public interface ICanSetScope {
        void SetScope(ArchitectureScope scope);
    }

    public interface IInitializable {
        void Initialize();
    }

    public interface IAsyncInitializable {
        Task InitializeAsync(CancellationToken cancellationToken);
    }

    public interface IDeinitializable {
        void Deinitialize();
    }

    public sealed class ArchitectureHost : Architecture { }

    public sealed class ServiceDebugInfo {
        public Type ServiceType { get; internal set; }
        public Type ImplementationType { get; internal set; }
        public EServiceLifetime Lifetime { get; internal set; }
        public bool IsCreated { get; internal set; }
        public bool IsInitialized { get; internal set; }
        public bool IsOwned { get; internal set; }
    }

    internal sealed class ServiceRegistration {
        public Type ServiceType;
        public Func<IServiceResolver, object> Factory;
        public EServiceLifetime Lifetime;
        public bool Owned;
        public int InitializationOrder;
        public int RegistrationOrder;
        public object Instance;
        public bool IsCreating;
        public bool IsInitialized;
        public bool AttachContext;
    }

    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
        public static readonly ReferenceEqualityComparer sInstance = new();

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
