using System.Collections.Generic;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        internal IReadOnlyList<ServiceDebugInfo> GetServiceDebugInfo() {
            var result = new List<ServiceDebugInfo>(m_RegistrationOrder.Count);

            foreach (var registration in m_RegistrationOrder) {
                result.Add(
                    new ServiceDebugInfo {
                        ServiceType = registration.ServiceType,
                        ImplementationType = registration.Instance?.GetType(),
                        Lifetime = registration.Lifetime,
                        IsCreated = registration.Instance != null,
                        IsInitialized = registration.IsInitialized,
                        IsOwned = registration.Owned
                    }
                );
            }

            return result;
        }
    }
}
