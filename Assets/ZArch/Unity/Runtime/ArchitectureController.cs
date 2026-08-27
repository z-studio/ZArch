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
    }
}
