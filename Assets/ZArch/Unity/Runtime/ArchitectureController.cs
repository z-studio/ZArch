using System;
using UnityEngine;

namespace ZArch {
    public abstract class ArchitectureController : MonoBehaviour, IController {
        private ArchitectureScope m_Scope;

        public void BindScope(ArchitectureScope scope) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (scope.IsDisposed) {
                throw new ObjectDisposedException(scope.Name);
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