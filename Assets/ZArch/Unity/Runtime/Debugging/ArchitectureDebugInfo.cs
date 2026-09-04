using System;
using System.Collections.Generic;

namespace ZArch.Unity {
    public sealed class EventHandlerDebugInfo {
        public string DeclaringType;
        public string TargetType;
        public string MethodName;
        public bool IsStatic;
    }

    public sealed class EventSubscriptionDebugInfo {
        public string EventType;
        public EventHandlerDebugInfo[] Subscribers = Array.Empty<EventHandlerDebugInfo>();
    }

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
        public EventSubscriptionDebugInfo[] Events = Array.Empty<EventSubscriptionDebugInfo>();
        public ServiceRegistrationDebugInfo[] Services = Array.Empty<ServiceRegistrationDebugInfo>();
        public ServiceRegistrationDebugInfo[] Bindings = Array.Empty<ServiceRegistrationDebugInfo>();
        public ScopeDebugInfo[] Children = Array.Empty<ScopeDebugInfo>();
    }

    public sealed class ArchitectureDebugInfo {
        public bool IsStarted;
        public string ArchitectureType;
        public EventSubscriptionDebugInfo[] Events = Array.Empty<EventSubscriptionDebugInfo>();
        public ScopeDebugInfo[] Roots = Array.Empty<ScopeDebugInfo>();
    }

    public static class ArchitectureDebug {
        public static ArchitectureDebugInfo Capture(Architecture architecture) {
            var info = new ArchitectureDebugInfo { IsStarted = architecture != null && architecture.IsStarted };

            if (!info.IsStarted) {
                return info;
            }

            info.ArchitectureType = architecture.GetType().FullName;
            info.Events = CaptureEvents(architecture.GetEventDebugInfo());
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
            var bindings = new List<ServiceRegistrationDebugInfo>();

            foreach (var service in scope.GetServiceDebugInfo()) {
                var debugInfo = new ServiceRegistrationDebugInfo {
                    ServiceType = service.ServiceType?.FullName,
                    ImplementationType = service.ImplementationType?.FullName,
                    Lifetime = service.Lifetime,
                    IsCreated = service.IsCreated,
                    IsInitialized = service.IsInitialized,
                    IsOwned = service.IsOwned
                };

                (service.IsBinding ? bindings : services).Add(debugInfo);
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
                Events = CaptureEvents(scope.GetEventDebugInfo()),
                Services = services.ToArray(),
                Bindings = bindings.ToArray(),
                Children = children.ToArray()
            };
        }

        private static EventSubscriptionDebugInfo[] CaptureEvents(
            IReadOnlyList<ZArch.EventSubscriptionDebugInfo> registrations
        ) {
            var events = new EventSubscriptionDebugInfo[registrations.Count];

            for (var eventIndex = 0; eventIndex < registrations.Count; eventIndex++) {
                var registration = registrations[eventIndex];
                var subscribers = new EventHandlerDebugInfo[registration.Subscribers.Length];

                for (var subscriberIndex = 0; subscriberIndex < registration.Subscribers.Length; subscriberIndex++) {
                    var subscriber = registration.Subscribers[subscriberIndex];
                    subscribers[subscriberIndex] = new EventHandlerDebugInfo {
                        DeclaringType = subscriber.Method.DeclaringType?.FullName,
                        TargetType = subscriber.Target?.GetType().FullName,
                        MethodName = subscriber.Method.Name,
                        IsStatic = subscriber.Method.IsStatic
                    };
                }

                events[eventIndex] = new EventSubscriptionDebugInfo {
                    EventType = registration.EventType?.FullName,
                    Subscribers = subscribers
                };
            }

            return events;
        }
    }
}
