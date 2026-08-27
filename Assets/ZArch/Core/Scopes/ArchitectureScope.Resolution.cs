using System;
using System.Collections.Generic;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        public object Resolve(Type serviceType) {
            if (serviceType == null) {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (TryResolve(serviceType, out var instance)) {
                return instance;
            }

            throw new KeyNotFoundException(
                $"Service {serviceType.FullName} is not registered from scope '{Name}' to the root."
            );
        }

        public bool TryResolve(Type serviceType, out object instance) {
            if (serviceType == null) {
                throw new ArgumentNullException(nameof(serviceType));
            }

            EnsureResolvable();

            if (m_Registrations.TryGetValue(serviceType, out var registration)) {
                instance = GetOrCreate(registration);
                return true;
            }

            if (Parent != null) {
                return Parent.TryResolve(serviceType, out instance);
            }

            instance = null;
            return false;
        }

        public T Resolve<T>() where T : class => (T)Resolve(typeof(T));

        public bool TryResolve<T>(out T instance) where T : class {
            if (TryResolve(typeof(T), out var raw)) {
                instance = raw as T;
                return instance != null;
            }

            instance = null;
            return false;
        }

        public bool IsRegisteredLocally<TService>() where TService : class {
            EnsureNotDisposed();
            return m_Registrations.ContainsKey(typeof(TService));
        }

        private object GetOrCreate(ServiceRegistration registration) {
            if (registration.Lifetime == EServiceLifetime.Scoped && registration.Instance != null) {
                return registration.Instance;
            }

            if (registration.IsCreating) {
                throw new InvalidOperationException(
                    $"Circular dependency detected while creating {registration.ServiceType.FullName} in scope '{Name}'."
                );
            }

            registration.IsCreating = true;

            try {
                var created = registration.Factory(this)
                              ?? throw new InvalidOperationException(
                                  $"Factory for {registration.ServiceType.FullName} returned null in scope '{Name}'."
                              );

                if (!registration.ServiceType.IsInstanceOfType(created)) {
                    throw new InvalidOperationException(
                        $"Factory for {registration.ServiceType.FullName} returned incompatible type {created.GetType().FullName}."
                    );
                }

                if (registration.Lifetime == EServiceLifetime.Transient
                    && registration.Owned
                    && HasLifecycle(created)) {
                    var exception = new InvalidOperationException(
                        $"Transient service {registration.ServiceType.FullName} has a managed lifecycle. "
                        + "Use EServiceLifetime.Scoped so initialization is deterministic."
                    );

                    if (created is IDisposable disposable) {
                        try {
                            disposable.Dispose();
                        } catch (Exception disposeException) {
                            throw new AggregateException(exception, disposeException);
                        }
                    }

                    throw exception;
                }

                if (registration.AttachContext) {
                    AttachContext(created);
                }

                if (registration.Owned) {
                    AddOwned(created);
                }

                if (registration.Lifetime == EServiceLifetime.Scoped) {
                    registration.Instance = created;
                }

                return created;
            } finally {
                registration.IsCreating = false;
            }
        }
    }
}
