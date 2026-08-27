using System;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        public void Register<TService>(TService instance, bool owned = true, int initializationOrder = 0)
            where TService : class {
            if (instance == null) {
                throw new ArgumentNullException(nameof(instance));
            }

            RegisterDescriptor(
                typeof(TService),
                _ => instance,
                EServiceLifetime.Scoped,
                owned,
                initializationOrder,
                instance,
                true
            );
        }

        public void Register<TService, TImplementation>(bool owned = true, int initializationOrder = 0)
            where TService : class
            where TImplementation : class, TService, new() {
            RegisterFactory<TService>(_ => new TImplementation(), EServiceLifetime.Scoped, owned, initializationOrder);
        }

        public void RegisterFactory<TService>(
            Func<IServiceResolver, TService> factory,
            EServiceLifetime lifetime = EServiceLifetime.Scoped,
            bool owned = true,
            int initializationOrder = 0
        ) where TService : class {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterDescriptor(typeof(TService), factory, lifetime, owned, initializationOrder, null, true);
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
                    $"Type {serviceType.FullName} is already registered in scope '{Name}'."
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
    }
}
