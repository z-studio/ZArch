using System;
using UnityEngine;

namespace ZArch.GameModules.Unity {
    public abstract class GameSceneEntry : MonoBehaviour {
        public ArchitectureScope Scope { get; private set; }

        public void BindScope(ArchitectureScope scope) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (scope.IsDisposed) {
                throw new ObjectDisposedException(scope.Name);
            }

            if (ReferenceEquals(Scope, scope)) {
                return;
            }

            if (Scope != null) {
                throw new InvalidOperationException($"{GetType().Name} is already bound to scope '{Scope.Name}'.");
            }

            Scope = scope;
            OnBindScope(scope);
        }

        protected abstract void OnBindScope(ArchitectureScope scope);
    }
}
