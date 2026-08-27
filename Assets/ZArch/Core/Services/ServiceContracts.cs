using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ZArch {
    public enum EServiceLifetime {
        Scoped,
        Transient
    }

    public interface IServiceResolver {
        object Resolve(Type serviceType);
        bool TryResolve(Type serviceType, out object instance);
        T Resolve<T>() where T : class;
        bool TryResolve<T>(out T instance) where T : class;
    }

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
