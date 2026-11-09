using System.Collections.Generic;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        internal IReadOnlyList<EventSubscriptionDebugInfo> GetEventDebugInfo() => m_Events.GetDebugInfo();

        internal IReadOnlyList<ServiceDebugInfo> GetServiceDebugInfo() {
            var result = new List<ServiceDebugInfo>(m_RegistrationOrder.Count);

            foreach (var registration in m_RegistrationOrder) {
                result.Add(
                    new ServiceDebugInfo {
                        ServiceType = registration.serviceType,
                        ImplementationType = registration.instance?.GetType(),
                        Lifetime = registration.lifetime,
                        IsCreated = registration.instance != null,
                        IsInitialized = registration.isInitialized,
                        IsOwned = registration.owned,
                        IsBinding = registration.isBinding
                    }
                );
            }

            return result;
        }
    }
}
