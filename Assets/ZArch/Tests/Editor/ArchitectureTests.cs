using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZArch.Tests.Editor {
    [TestFixture]
    public sealed class ArchitectureTests {
        private ArchitectureHost m_Host;

        [SetUp]
        public void SetUp() {
            m_Host = new ArchitectureHost();
            m_Host.Start();
        }

        [TearDown]
        public void TearDown() => m_Host.Dispose();

        [Test]
        public void Resolve_StartsAtRequestingScopeAndFallsBackToParent() {
            var parentModel = new TestModel("parent");
            var childModel = new TestModel("child");
            var root = m_Host.CreateRootScope("Root", scope => scope.Register(parentModel));
            var child = root.CreateChild("Child", scope => scope.Register(childModel));
            var sibling = root.CreateChild("Sibling", _ => { });

            Assert.That(root.Resolve<TestModel>(), Is.SameAs(parentModel));
            Assert.That(child.Resolve<TestModel>(), Is.SameAs(childModel));
            Assert.That(sibling.Resolve<TestModel>(), Is.SameAs(parentModel));
        }

        [Test]
        public void SystemInitialization_CanResolveInitializedModelInSameScope() {
            ProbeSystem system = null;
            var model = new TestModel("model");

            m_Host.CreateRootScope("Root", scope => {
                scope.Register(model);
                scope.Register(system = new ProbeSystem());
            });

            Assert.That(system.ResolvedModel, Is.SameAs(model));
        }

        [Test]
        public void ActivationFailure_RemovesScopeAndRollsBackInitializedServices() {
            var initialized = new TrackingSystem();

            Assert.Throws<InvalidOperationException>(() =>
                m_Host.CreateRootScope("Broken", scope => {
                    scope.Register(initialized);
                    scope.Register(new FailingSystem());
                })
            );

            Assert.That(initialized.DeinitializeCount, Is.EqualTo(1));
            Assert.That(m_Host.RootScopes, Is.Empty);
        }

        [Test]
        public void CircularFactoryDependency_IsRejectedAndRolledBack() {
            Assert.Throws<InvalidOperationException>(() =>
                m_Host.CreateRootScope("Circular", scope => {
                    scope.RegisterFactory<ServiceA>(resolver => new ServiceA(resolver.Resolve<ServiceB>()));
                    scope.RegisterFactory<ServiceB>(resolver => new ServiceB(resolver.Resolve<ServiceA>()));
                })
            );

            Assert.That(m_Host.RootScopes, Is.Empty);
        }

        [Test]
        public async Task AsyncInitialization_CompletesBeforeScopeBecomesActive() {
            var service = new AsyncService();
            var scope = await m_Host.CreateRootScopeAsync(
                "Async",
                configured => {
                    configured.Register(service);
                    return Task.CompletedTask;
                }
            );

            Assert.That(scope.State, Is.EqualTo(EScopeState.Active));
            Assert.That(service.Initialized, Is.True);
        }

        [Test]
        public void MultipleHosts_DoNotShareServicesScopesOrEvents() {
            using var secondHost = new ArchitectureHost();
            secondHost.Start();

            var firstService = new PlainService("first");
            var secondService = new PlainService("second");
            var firstEventCount = 0;
            var secondEventCount = 0;

            var firstRoot = m_Host.CreateRootScope("Root", scope => scope.Register(firstService));
            var secondRoot = secondHost.CreateRootScope("Root", scope => scope.Register(secondService));
            m_Host.RegisterEvent<ProbeEvent>(_ => firstEventCount++);
            secondHost.RegisterEvent<ProbeEvent>(_ => secondEventCount++);

            m_Host.SendEvent(new ProbeEvent());

            Assert.That(firstRoot.Resolve<PlainService>(), Is.SameAs(firstService));
            Assert.That(secondRoot.Resolve<PlainService>(), Is.SameAs(secondService));
            Assert.That(firstEventCount, Is.EqualTo(1));
            Assert.That(secondEventCount, Is.Zero);
        }

        [Test]
        public void ScopePublish_IsLocalAndParentsPropagationDoesNotReachArchitecture() {
            var architectureCount = 0;
            var rootCount = 0;
            var childCount = 0;
            var root = m_Host.CreateRootScope("Root", _ => { });
            var child = root.CreateChild("Child", _ => { });
            m_Host.RegisterEvent<ProbeEvent>(_ => architectureCount++);
            root.RegisterEvent<ProbeEvent>(_ => rootCount++);
            child.RegisterEvent<ProbeEvent>(_ => childCount++);

            child.Publish(new ProbeEvent());

            Assert.That(childCount, Is.EqualTo(1));
            Assert.That(rootCount, Is.Zero);
            Assert.That(architectureCount, Is.Zero);

            child.Publish(new ProbeEvent(), EEventPropagation.Parents);

            Assert.That(childCount, Is.EqualTo(2));
            Assert.That(rootCount, Is.EqualTo(1));
            Assert.That(architectureCount, Is.Zero);
        }

        [Test]
        public void ArchitectureEvent_InvokesAllSubscribersBeforeThrowingAggregate() {
            var calls = new System.Collections.Generic.List<string>();
            m_Host.RegisterEvent<ProbeEvent>(_ => calls.Add("first"));
            m_Host.RegisterEvent<ProbeEvent>(_ => throw new InvalidOperationException("subscriber failed"));
            m_Host.RegisterEvent<ProbeEvent>(_ => calls.Add("last"));

            var exception = Assert.Throws<AggregateException>(() =>
                m_Host.SendEvent(new ProbeEvent())
            );

            Assert.That(calls, Is.EqualTo(new[] { "first", "last" }));
            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(exception.InnerExceptions[0].Message, Is.EqualTo("subscriber failed"));
        }

        [Test]
        public void ScopePublish_ContinuesToParentsAfterSubscriberFailure() {
            var rootCalls = 0;
            var childCalls = 0;
            var root = m_Host.CreateRootScope("Root", _ => { });
            var child = root.CreateChild("Child", _ => { });
            child.RegisterEvent<ProbeEvent>(_ => throw new InvalidOperationException("child failed"));
            child.RegisterEvent<ProbeEvent>(_ => childCalls++);
            root.RegisterEvent<ProbeEvent>(_ => rootCalls++);

            var exception = Assert.Throws<AggregateException>(() =>
                child.Publish(new ProbeEvent(), EEventPropagation.Parents)
            );

            Assert.That(childCalls, Is.EqualTo(1));
            Assert.That(rootCalls, Is.EqualTo(1));
            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
        }

        [Test]
        public void PatternSendEvent_ReachesArchitectureOnly() {
            var architectureCount = 0;
            var localCount = 0;
            EventSenderSystem sender = null;
            var root = m_Host.CreateRootScope("Root", scope => scope.Register(sender = new EventSenderSystem()));
            m_Host.RegisterEvent<ProbeEvent>(_ => architectureCount++);
            root.RegisterEvent<ProbeEvent>(_ => localCount++);

            sender.Raise();

            Assert.That(architectureCount, Is.EqualTo(1));
            Assert.That(localCount, Is.Zero);
        }

        [Test]
        public void DeinitializableOnlyService_IsDeinitializedWithItsScope() {
            var service = new DeinitializableOnlyService();
            var scope = m_Host.CreateRootScope("Root", configured => configured.Register(service));

            scope.Dispose();

            Assert.That(service.DeinitializeCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposingParent_DisposesChildrenBeforeParentServices() {
            var order = new System.Collections.Generic.List<string>();
            var root = m_Host.CreateRootScope("Root", scope => scope.Register(new OrderedService("root", order)));
            var child = root.CreateChild("Child", scope => scope.Register(new OrderedService("child", order)));

            root.Dispose();

            Assert.That(child.IsDisposed, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "child", "root" }));
        }

        [Test]
        public void Shutdown_RejectsScopeCreationFromDeinitialize() {
            Exception creationException = null;

            m_Host.CreateRootScope("Root", scope =>
                scope.Register(new CallbackService(() => {
                    try {
                        m_Host.CreateRootScope("Leaked", _ => { });
                    } catch (Exception exception) {
                        creationException = exception;
                    }
                }))
            );

            m_Host.Shutdown();

            Assert.That(creationException, Is.TypeOf<InvalidOperationException>());
            Assert.That(m_Host.RootScopes, Is.Empty);
            Assert.That(m_Host.Scopes, Is.Empty);
        }

        [Test]
        public void DisposingScope_RejectsChildCreationFromDeinitialize() {
            ArchitectureScope root = null;
            Exception creationException = null;

            root = m_Host.CreateRootScope("Root", scope =>
                scope.Register(new CallbackService(() => {
                    try {
                        root.CreateChild("Leaked", _ => { });
                    } catch (Exception exception) {
                        creationException = exception;
                    }
                }))
            );

            root.Dispose();

            Assert.That(creationException, Is.TypeOf<ObjectDisposedException>());
            Assert.That(m_Host.Scopes, Is.Empty);
        }

        [Test]
        public void ThrowingExceptionHandler_DoesNotInterruptScopeCleanup() {
            var cleanupCount = 0;
            var cleaned = new CallbackService(() => cleanupCount++);
            m_Host.ExceptionHandler = _ => throw new InvalidOperationException("Reporter failed.");

            var scope = m_Host.CreateRootScope("Root", configured => {
                configured.Register(cleaned);
                configured.Register(new CallbackService(() => throw new InvalidOperationException("Cleanup failed.")));
            });

            Assert.DoesNotThrow(scope.Dispose);
            Assert.That(cleanupCount, Is.EqualTo(1));
            Assert.That(scope.IsDisposed, Is.True);
        }

        [Test]
        public void ChildAlias_DoesNotRebindParentServiceContext() {
            var system = new AliasSystem();
            var root = m_Host.CreateRootScope("Root", scope => scope.Register(system));
            var child = root.CreateChild("Child", scope => scope.RegisterAlias<IAliasSystem, AliasSystem>());

            Assert.That(child.Resolve<IAliasSystem>(), Is.SameAs(system));
            Assert.That(system.GetScope(), Is.SameAs(root));

            child.Dispose();

            Assert.That(system.GetScope(), Is.SameAs(root));
        }

        [Test]
        public void InvalidAsyncTimeout_DoesNotLeaveUnconfiguredScope() {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await m_Host.CreateRootScopeAsync(
                    "InvalidTimeout",
                    (_, _) => Task.CompletedTask,
                    timeout: TimeSpan.FromMilliseconds(-2)
                )
            );

            Assert.That(m_Host.RootScopes, Is.Empty);
            Assert.That(m_Host.Scopes, Is.Empty);
        }

        [Test]
        public void AsyncSetup_CanObserveTimeoutCancellation() {
            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await m_Host.CreateRootScopeAsync(
                    "Timeout",
                    async (_, token) => await Task.Delay(Timeout.InfiniteTimeSpan, token),
                    timeout: TimeSpan.FromMilliseconds(20)
                )
            );

            Assert.That(m_Host.RootScopes, Is.Empty);
            Assert.That(m_Host.Scopes, Is.Empty);
        }

        [Test]
        public async Task AsyncScope_IsNotPublishedUntilActivationCompletes() {
            var service = new BlockingAsyncService();
            var creating = m_Host.CreateRootScopeAsync(
                "Pending",
                scope => {
                    scope.Register(service);
                    return Task.CompletedTask;
                }
            );

            await service.Started.Task;

            Assert.That(m_Host.RootScopes, Is.Empty);
            Assert.That(m_Host.Scopes, Is.Empty);

            service.Release.SetResult(true);
            var scope = await creating;

            Assert.That(scope.State, Is.EqualTo(EScopeState.Active));
            Assert.That(m_Host.RootScopes, Is.EqualTo(new[] { scope }));
            Assert.That(m_Host.Scopes, Is.EqualTo(new[] { scope }));
        }

        [Test]
        public async Task Shutdown_DuringAsyncInitialization_CannotReactivateDisposedScope() {
            var service = new BlockingAsyncService();
            ArchitectureScope pendingScope = null;
            var creating = m_Host.CreateRootScopeAsync(
                "Pending",
                scope => {
                    pendingScope = scope;
                    scope.Register(service);
                    return Task.CompletedTask;
                }
            );

            await service.Started.Task;
            m_Host.Shutdown();

            Assert.That(pendingScope.State, Is.EqualTo(EScopeState.Disposed));
            Assert.That(m_Host.RootScopes, Is.Empty);
            Assert.That(m_Host.Scopes, Is.Empty);

            service.Release.SetResult(true);

            Assert.CatchAsync<OperationCanceledException>(async () => await creating);
            Assert.That(pendingScope.State, Is.EqualTo(EScopeState.Disposed));
        }

        [Test]
        public async Task DisposingParent_CancelsPendingChildActivation() {
            var parent = m_Host.CreateRootScope("Parent", _ => { });
            var service = new BlockingAsyncService();
            ArchitectureScope pendingChild = null;
            var creating = parent.CreateChildAsync(
                "PendingChild",
                scope => {
                    pendingChild = scope;
                    scope.Register(service);
                    return Task.CompletedTask;
                }
            );

            await service.Started.Task;
            Assert.That(parent.Children, Is.Empty);
            Assert.That(m_Host.Scopes, Is.EqualTo(new[] { parent }));

            parent.Dispose();
            service.Release.SetResult(true);

            Assert.CatchAsync<OperationCanceledException>(async () => await creating);
            Assert.That(pendingChild.State, Is.EqualTo(EScopeState.Disposed));
            Assert.That(m_Host.Scopes, Is.Empty);
        }

        [Test]
        public void Shutdown_MakesArchitectureInstanceOneShot() {
            m_Host.Shutdown();

            var exception = Assert.Throws<InvalidOperationException>(m_Host.Start);

            Assert.That(exception.Message, Does.Contain("cannot be restarted"));
        }

        private sealed class TestModel : AbstractModel {
            public string Value { get; }

            public TestModel(string value) => Value = value;

            protected override void OnInit() { }
        }

        private sealed class ProbeSystem : AbstractSystem {
            public TestModel ResolvedModel { get; private set; }

            protected override void OnInit() => ResolvedModel = this.GetModel<TestModel>();
        }

        private sealed class TrackingSystem : AbstractSystem {
            public int DeinitializeCount { get; private set; }

            protected override void OnInit() { }
            protected override void OnDeinit() => DeinitializeCount++;
        }

        private sealed class FailingSystem : AbstractSystem {
            protected override void OnInit() => throw new InvalidOperationException("Expected failure.");
        }

        private sealed class ServiceA {
            public ServiceA(ServiceB dependency) { }
        }

        private sealed class ServiceB {
            public ServiceB(ServiceA dependency) { }
        }

        private sealed class AsyncService : IAsyncInitializable {
            public bool Initialized { get; private set; }

            public Task InitializeAsync(CancellationToken cancellationToken) {
                Initialized = true;
                return Task.CompletedTask;
            }
        }

        private sealed class BlockingAsyncService : IAsyncInitializable {
            public TaskCompletionSource<bool> Started { get; } = new();
            public TaskCompletionSource<bool> Release { get; } = new();

            public async Task InitializeAsync(CancellationToken cancellationToken) {
                Started.TrySetResult(true);
                await Release.Task;
            }
        }

        private sealed class PlainService {
            public string Value { get; }
            public PlainService(string value) => Value = value;
        }

        private readonly struct ProbeEvent { }

        private sealed class EventSenderSystem : AbstractSystem {
            protected override void OnInit() { }
            public void Raise() => this.SendEvent(new ProbeEvent());
        }

        private sealed class DeinitializableOnlyService : IDeinitializable {
            public int DeinitializeCount { get; private set; }
            public void Deinitialize() => DeinitializeCount++;
        }

        private sealed class OrderedService : IDeinitializable {
            private readonly string m_Name;
            private readonly System.Collections.Generic.List<string> m_Order;

            public OrderedService(string name, System.Collections.Generic.List<string> order) {
                m_Name = name;
                m_Order = order;
            }

            public void Deinitialize() => m_Order.Add(m_Name);
        }

        private sealed class CallbackService : IDeinitializable {
            private readonly Action m_Callback;

            public CallbackService(Action callback) => m_Callback = callback;
            public void Deinitialize() => m_Callback();
        }

        private interface IAliasSystem : ISystem { }

        private sealed class AliasSystem : AbstractSystem, IAliasSystem {
            protected override void OnInit() { }
        }
    }
}
