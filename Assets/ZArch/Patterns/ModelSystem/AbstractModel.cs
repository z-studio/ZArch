using System;
using System.Collections.Generic;

namespace ZArch {
    public abstract class AbstractModel : IModel, IUnregisterList {
        private ArchitectureScope m_Scope;

        public List<IUnregister> UnregisterList { get; } = new();
        public ArchitectureScope GetScope() =>
            m_Scope ?? throw new InvalidOperationException($"{GetType().Name} has not been bound to a scope.");

        public void SetScope(ArchitectureScope scope) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (ReferenceEquals(m_Scope, scope)) {
                return;
            }

            if (m_Scope != null) {
                throw new InvalidOperationException(
                    $"{GetType().Name} is already bound to scope '{m_Scope.Name}'."
                );
            }

            if (scope.State is EScopeState.Disposing or EScopeState.Disposed or EScopeState.Faulted) {
                throw new ObjectDisposedException(scope.Name, $"Scope '{scope.Name}' is in state {scope.State}.");
            }

            m_Scope = scope;
        }

        public void Initialize() => OnInit();

        public void Deinitialize() {
            DeinitializeSafely(OnDeinit);
        }

        private void DeinitializeSafely(Action onDeinit) {
            Exception unregisterException = null;

            try {
                this.UnregisterAll();
            } catch (Exception exception) {
                unregisterException = exception;
            }

            try {
                onDeinit();
            } catch (Exception exception) {
                if (unregisterException != null) {
                    throw new AggregateException(unregisterException, exception);
                }

                throw;
            }

            if (unregisterException != null) {
                throw unregisterException;
            }
        }

        protected abstract void OnInit();
        protected virtual void OnDeinit() { }
    }
}
