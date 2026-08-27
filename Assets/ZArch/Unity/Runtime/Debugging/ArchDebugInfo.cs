using System;
using System.Collections.Generic;

namespace ZArch.Unity {
    public sealed class ServiceRegistrationDebugInfo {
        public string ServiceType;
        public string ImplementationType;
        public EServiceLifetime Lifetime;
        public bool IsCreated;
        public bool IsInitialized;
        public bool IsOwned;
    }

    public sealed class ScopeDebugInfo {
        public string Name;
        public EScopeState State;
        public string Tag;
        public string BoundSceneName;
        public ServiceRegistrationDebugInfo[] Services = Array.Empty<ServiceRegistrationDebugInfo>();
        public ScopeDebugInfo[] Children = Array.Empty<ScopeDebugInfo>();
    }

    public sealed class ArchDebugInfo {
        public bool IsStarted;
        public string ArchitectureType;
        public ScopeDebugInfo[] Roots = Array.Empty<ScopeDebugInfo>();
    }

    public static class ArchDebug {
        public static ArchDebugInfo Capture(Architecture architecture) {
            var info = new ArchDebugInfo { IsStarted = architecture != null && architecture.IsStarted };

            if (!info.IsStarted) {
                return info;
            }

            info.ArchitectureType = architecture.GetType().FullName;
            var roots = new List<ScopeDebugInfo>();

            foreach (var root in architecture.RootScopes) {
                if (root != null && !root.IsDisposed) {
                    roots.Add(CaptureScope(root));
                }
            }

            info.Roots = roots.ToArray();
            return info;
        }

        private static ScopeDebugInfo CaptureScope(ArchitectureScope scope) {
            var services = new List<ServiceRegistrationDebugInfo>();

            foreach (var service in scope.GetServiceDebugInfo()) {
                services.Add(
                    new ServiceRegistrationDebugInfo {
                        ServiceType = service.ServiceType?.FullName,
                        ImplementationType = service.ImplementationType?.FullName,
                        Lifetime = service.Lifetime,
                        IsCreated = service.IsCreated,
                        IsInitialized = service.IsInitialized,
                        IsOwned = service.IsOwned
                    }
                );
            }

            var children = new List<ScopeDebugInfo>();

            foreach (var child in scope.Children) {
                if (child != null && !child.IsDisposed) {
                    children.Add(CaptureScope(child));
                }
            }

            return new ScopeDebugInfo {
                Name = scope.Name,
                State = scope.State,
                Tag = scope.Tag?.ToString(),
                BoundSceneName = (scope.Tag as SceneScopeTag)?.SceneName,
                Services = services.ToArray(),
                Children = children.ToArray()
            };
        }
    }
}
