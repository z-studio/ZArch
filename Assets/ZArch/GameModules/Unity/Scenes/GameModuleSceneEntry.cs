using System;
using UnityEngine;

namespace ZArch.GameModules.Unity {
    public abstract class GameModuleSceneEntry : MonoBehaviour {
        public ArchitectureScope Scope { get; private set; }

        public void BindScope(ArchitectureScope scope) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (scope.State != EScopeState.Active) {
                throw new InvalidOperationException(
                    $"Scope '{scope.Name}' must be Active before binding a game scene; current state is {scope.State}."
                );
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
