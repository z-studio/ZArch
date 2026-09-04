using System;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        /// <summary>
        /// 注册由 Scope 管理生命周期的服务实例。仅可在 Scope 配置阶段调用。
        /// </summary>
        public void Register<TService>(TService instance, int initializationOrder = 0)
            where TService : class {
            if (instance == null) {
                throw new ArgumentNullException(nameof(instance));
            }

            RegisterDescriptor(
                typeof(TService),
                _ => instance,
                EServiceLifetime.Scoped,
                true,
                initializationOrder,
                instance,
                true
            );
        }

        /// <summary>
        /// 将调用方持有生命周期的外部对象绑定到当前 Scope。
        /// 绑定不参与初始化和释放，可在配置期或 Active 状态下创建，并通过返回值解除。
        /// </summary>
        public IDisposable Bind<TService>(TService instance) where TService : class {
            if (instance == null) {
                throw new ArgumentNullException(nameof(instance));
            }

            EnsureBindable();
            Type serviceType = typeof(TService);

            if (m_Registrations.ContainsKey(serviceType)) {
                throw new InvalidOperationException(
                    $"Type {serviceType.FullName} is already registered or bound in scope '{Name}'."
                );
            }

            var registration = new ServiceRegistration {
                ServiceType = serviceType,
                Factory = _ => instance,
                Lifetime = EServiceLifetime.Scoped,
                Owned = false,
                InitializationOrder = 0,
                RegistrationOrder = m_NextRegistrationOrder++,
                Instance = instance,
                AttachContext = false,
                IsBinding = true
            };

            m_Registrations.Add(serviceType, registration);
            m_RegistrationOrder.Add(registration);
            return new ExternalBinding(this, registration);
        }

        public void Register<TService, TImplementation>(int initializationOrder = 0)
            where TService : class
            where TImplementation : class, TService, new() {
            RegisterScopedFactory<TService>(_ => new TImplementation(), initializationOrder);
        }

        public void RegisterScopedFactory<TService>(
            Func<IServiceResolver, TService> factory,
            int initializationOrder = 0
        ) where TService : class {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterDescriptor(
                typeof(TService),
                factory,
                EServiceLifetime.Scoped,
                true,
                initializationOrder,
                null,
                true
            );
        }

        public void RegisterTransient<TService>(Func<IServiceResolver, TService> factory)
            where TService : class {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterDescriptor(
                typeof(TService),
                factory,
                EServiceLifetime.Transient,
                false,
                0,
                null,
                true
            );
        }

        public void RegisterOwnedTransient<TService>(Func<IServiceResolver, TService> factory)
            where TService : class {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterDescriptor(
                typeof(TService),
                factory,
                EServiceLifetime.Transient,
                true,
                0,
                null,
                true
            );
        }

        public void RegisterAlias<TAlias, TService>()
            where TAlias : class
            where TService : class, TAlias {
            RegisterDescriptor(
                typeof(TAlias),
                resolver => resolver.Resolve<TService>(),
                EServiceLifetime.Scoped,
                false,
                0,
                null,
                false
            );
        }

        private void RegisterDescriptor(
            Type serviceType,
            Func<IServiceResolver, object> factory,
            EServiceLifetime lifetime,
            bool owned,
            int initializationOrder,
            object instance,
            bool attachContext
        ) {
            EnsureConfigurable();

            if (lifetime is not EServiceLifetime.Scoped and not EServiceLifetime.Transient) {
                throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unknown service lifetime.");
            }

            if (m_Registrations.ContainsKey(serviceType)) {
                throw new InvalidOperationException(
                    $"Type {serviceType.FullName} is already registered or bound in scope '{Name}'."
                );
            }

            var registration = new ServiceRegistration {
                ServiceType = serviceType,
                Factory = factory,
                Lifetime = lifetime,
                Owned = owned,
                InitializationOrder = initializationOrder,
                RegistrationOrder = m_NextRegistrationOrder++,
                Instance = instance,
                AttachContext = attachContext
            };

            m_Registrations.Add(serviceType, registration);
            m_RegistrationOrder.Add(registration);

            if (instance != null) {
                if (attachContext) {
                    AttachContext(instance);
                }

                if (owned) {
                    AddOwned(instance);
                }
            }
        }

        private void RemoveBinding(ServiceRegistration registration) {
            if (registration == null || !registration.IsBinding) {
                return;
            }

            if (m_Registrations.TryGetValue(registration.ServiceType, out var current)
                && ReferenceEquals(current, registration)) {
                m_Registrations.Remove(registration.ServiceType);
                m_RegistrationOrder.Remove(registration);
            }
        }

        private sealed class ExternalBinding : IDisposable {
            private ArchitectureScope m_Scope;
            private ServiceRegistration m_Registration;

            public ExternalBinding(ArchitectureScope scope, ServiceRegistration registration) {
                m_Scope = scope;
                m_Registration = registration;
            }

            public void Dispose() {
                ArchitectureScope scope = m_Scope;
                ServiceRegistration registration = m_Registration;
                m_Scope = null;
                m_Registration = null;
                scope?.RemoveBinding(registration);
            }
        }
    }
}
