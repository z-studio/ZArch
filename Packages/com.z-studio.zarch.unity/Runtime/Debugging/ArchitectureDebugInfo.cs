using System;
using System.Collections.Generic;

namespace ZArch.Unity {
    public sealed class EventHandlerDebugInfo {
        public string declaringType;
        public string targetType;
        public string methodName;
        public bool isStatic;
    }

    public sealed class EventSubscriptionDebugInfo {
        public string eventType;
        public EventHandlerDebugInfo[] subscribers = Array.Empty<EventHandlerDebugInfo>();
    }

    public sealed class ServiceRegistrationDebugInfo {
        public string serviceType;
        public string implementationType;
        public EServiceLifetime lifetime;
        public bool isCreated;
        public bool isInitialized;
        public bool isOwned;
    }

    public sealed class ScopeDebugInfo {
        public string name;
        public ScopeState state;
        public string tag;
        public string boundSceneName;
        public EventSubscriptionDebugInfo[] events = Array.Empty<EventSubscriptionDebugInfo>();
        public ServiceRegistrationDebugInfo[] services = Array.Empty<ServiceRegistrationDebugInfo>();
        public ServiceRegistrationDebugInfo[] bindings = Array.Empty<ServiceRegistrationDebugInfo>();
        public ScopeDebugInfo[] children = Array.Empty<ScopeDebugInfo>();
    }

    public sealed class ArchitectureDebugInfo {
        public bool isStarted;
        public string architectureType;
        public EventSubscriptionDebugInfo[] events = Array.Empty<EventSubscriptionDebugInfo>();
        public ScopeDebugInfo[] roots = Array.Empty<ScopeDebugInfo>();
    }

    public static class ArchitectureDebug {
        public static ArchitectureDebugInfo Capture(Architecture architecture) {
            var info = new ArchitectureDebugInfo { isStarted = architecture is { IsStarted: true } };

            if (!info.isStarted) {
                return info;
            }

            info.architectureType = architecture.GetType().FullName;
            info.events = CaptureEvents(architecture.GetEventDebugInfo());
            var roots = new List<ScopeDebugInfo>();

            foreach (var root in architecture.RootScopes) {
                if (root is { IsDisposed: false }) {
                    roots.Add(CaptureScope(root));
                }
            }

            info.roots = roots.ToArray();
            return info;
        }

        private static ScopeDebugInfo CaptureScope(ArchitectureScope scope) {
            var services = new List<ServiceRegistrationDebugInfo>();
            var bindings = new List<ServiceRegistrationDebugInfo>();

            foreach (var service in scope.GetServiceDebugInfo()) {
                var debugInfo = new ServiceRegistrationDebugInfo {
                    serviceType = service.ServiceType?.FullName,
                    implementationType = service.ImplementationType?.FullName,
                    lifetime = service.Lifetime,
                    isCreated = service.IsCreated,
                    isInitialized = service.IsInitialized,
                    isOwned = service.IsOwned
                };

                (service.IsBinding ? bindings : services).Add(debugInfo);
            }

            var children = new List<ScopeDebugInfo>();

            foreach (var child in scope.Children) {
                if (child is { IsDisposed: false }) {
                    children.Add(CaptureScope(child));
                }
            }

            return new ScopeDebugInfo {
                name = scope.Name,
                state = scope.State,
                tag = scope.Tag?.ToString(),
                boundSceneName = (scope.Tag as GameDefinition)?.SceneLocation,
                events = CaptureEvents(scope.GetEventDebugInfo()),
                services = services.ToArray(),
                bindings = bindings.ToArray(),
                children = children.ToArray()
            };
        }

        private static EventSubscriptionDebugInfo[] CaptureEvents(
            IReadOnlyList<ZArch.EventSubscriptionDebugInfo> registrations) {
            var events = new EventSubscriptionDebugInfo[registrations.Count];

            for (var eventIndex = 0; eventIndex < registrations.Count; eventIndex++) {
                var registration = registrations[eventIndex];
                var subscribers = new EventHandlerDebugInfo[registration.Subscribers.Length];

                for (var subscriberIndex = 0; subscriberIndex < registration.Subscribers.Length; subscriberIndex++) {
                    var subscriber = registration.Subscribers[subscriberIndex];
                    subscribers[subscriberIndex] = new EventHandlerDebugInfo {
                        declaringType = subscriber.Method.DeclaringType?.FullName,
                        targetType = subscriber.Target?.GetType().FullName,
                        methodName = subscriber.Method.Name,
                        isStatic = subscriber.Method.IsStatic
                    };
                }

                events[eventIndex] = new EventSubscriptionDebugInfo {
                    eventType = registration.EventType?.FullName, subscribers = subscribers
                };
            }

            return events;
        }
    }
}