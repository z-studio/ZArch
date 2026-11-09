using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ZArch {
    public enum EServiceLifetime {
        Scoped,
        Transient
    }

    // 基础设施角色，例如存储、网络、时钟；Model/System 不应实现此接口。
    public interface IService { }

    public interface IServiceResolver {
        object Resolve(Type serviceType);
        bool TryResolve(Type serviceType, out object instance);
        T Resolve<T>() where T : class;
        bool TryResolve<T>(out T instance) where T : class;
    }

    internal sealed class ServiceDebugInfo {
        public Type ServiceType { get; internal set; }
        public Type ImplementationType { get; internal set; }
        public EServiceLifetime Lifetime { get; internal set; }
        public bool IsCreated { get; internal set; }
        public bool IsInitialized { get; internal set; }
        public bool IsOwned { get; internal set; }
        public bool IsBinding { get; internal set; }
    }

    internal sealed class ServiceRegistration {
        public Type serviceType;
        public Func<IServiceResolver, object> factory;
        public EServiceLifetime lifetime;
        public bool owned;
        public int initializationOrder;
        public int registrationOrder;
        public object instance;
        public bool isCreating;
        public bool isInitialized;
        public bool attachContext;
        public bool isBinding;
    }

    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}