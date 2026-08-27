using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZArch.GameModules.Unity {
    [CreateAssetMenu(fileName = "GameModuleCatalog", menuName = "ZArch/Game Module Catalog")]
    public sealed class GameModuleCatalog : ScriptableObject, IEnumerable<IGameModule> {
        [SerializeField]
        private UnityGameModuleAsset[] m_Modules = Array.Empty<UnityGameModuleAsset>();

        public IReadOnlyList<UnityGameModuleAsset> Modules => m_Modules;

        public void Validate() {
            if (m_Modules == null) {
                throw new InvalidOperationException($"{name} has a null module array.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < m_Modules.Length; i++) {
                var module = m_Modules[i];

                if (module == null) {
                    throw new InvalidOperationException($"{name} has a missing module at index {i}.");
                }

                if (string.IsNullOrWhiteSpace(module.Id)) {
                    throw new InvalidOperationException($"Module asset '{module.name}' has an empty ID.");
                }

                if (!ids.Add(module.Id)) {
                    throw new InvalidOperationException($"{name} contains duplicate module ID '{module.Id}'.");
                }

                if (string.IsNullOrWhiteSpace(module.SceneProviderId)) {
                    throw new InvalidOperationException(
                        $"Module asset '{module.name}' has an empty scene provider ID."
                    );
                }

                if (string.IsNullOrWhiteSpace(module.SceneLocation)) {
                    throw new InvalidOperationException(
                        $"Module asset '{module.name}' has an empty scene location."
                    );
                }
            }
        }

        public IEnumerator<IGameModule> GetEnumerator() {
            Validate();

            foreach (var module in m_Modules) {
                yield return module;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
