using System;
using UnityEngine;

namespace ZArch.Unity {
    public abstract class ArchitectureController : MonoBehaviour, IController {
        private ArchitectureScope m_Scope;

        public void BindScope(ArchitectureScope scope) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (scope.State != EScopeState.Active) {
                throw new InvalidOperationException(
                    $"Scope '{scope.Name}' must be Active before binding a controller; current state is {scope.State}."
                );
            }

            if (ReferenceEquals(m_Scope, scope)) {
                return;
            }

            if (m_Scope != null) {
                throw new InvalidOperationException($"{GetType().Name} is already bound to scope '{m_Scope.Name}'.");
            }

            m_Scope = scope;
        }

        public ArchitectureScope GetScope() {
            if (m_Scope == null) {
                throw new InvalidOperationException($"{GetType().Name} has not been bound to a scope.");
            }

            if (m_Scope.IsDisposed) {
                throw new ObjectDisposedException(m_Scope.Name);
            }

            return m_Scope;
        }

        protected ArchitectureScope GetBoundScopeOrNull() => m_Scope;

        protected void ClearScopeBinding(ArchitectureScope expectedScope) {
            if (expectedScope == null) {
                throw new ArgumentNullException(nameof(expectedScope));
            }

            if (!ReferenceEquals(m_Scope, expectedScope)) {
                throw new InvalidOperationException(
                    $"{GetType().Name} is not bound to scope '{expectedScope.Name}'."
                );
            }

            m_Scope = null;
        }
    }

    public abstract class ReusableArchitectureController : ArchitectureController, IUnregisterList {
        public System.Collections.Generic.List<IUnregister> UnregisterList { get; } = new();

        public void UnbindScope() {
            var scope = GetBoundScopeOrNull();

            if (scope == null) {
                return;
            }

            Exception unregisterException = null;

            try {
                this.UnregisterAll();
            } catch (Exception exception) {
                unregisterException = exception;
            } finally {
                ClearScopeBinding(scope);
            }

            if (unregisterException != null) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(unregisterException).Throw();
            }
        }
    }
}
