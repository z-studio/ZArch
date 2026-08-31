using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ZArch.Tests.Editor {
    [TestFixture]
    public sealed class ArchitectureTests {
        private Architecture m_Architecture;

        [SetUp]
        public void SetUp() {
            m_Architecture = new Architecture();
            m_Architecture.Start();
        }

        [TearDown]
        public void TearDown() => m_Architecture.Dispose();

        [Test]
        public void Resolve_StartsAtRequestingScopeAndFallsBackToParent() {
            var parentModel = new TestModel("parent");
            var childModel = new TestModel("child");
            var root = m_Architecture.CreateRootScope("Root", scope => scope.Register(parentModel));
            var child = root.CreateChild("Child", scope => scope.Register(childModel));
            var sibling = root.CreateChild("Sibling", _ => { });

            Assert.That(root.Resolve<TestModel>(), Is.SameAs(parentModel));
            Assert.That(child.Resolve<TestModel>(), Is.SameAs(childModel));
            Assert.That(sibling.Resolve<TestModel>(), Is.SameAs(parentModel));
        }

        [Test]
        public void OnStart_CanUseArchitectureApis() {
            var architecture = new ScopeCreatingArchitecture();

            try {
                architecture.Start();

                Assert.That(architecture.StartedScope, Is.Not.Null);
                Assert.That(architecture.StartedScope.IsActive, Is.True);
                Assert.That(architecture.RootScopes, Is.EqualTo(new[] { architecture.StartedScope }));
            } finally {
                architecture.Dispose();
            }
        }

        [Test]
        public void Start_WhenStartupAndRollbackFail_PreservesBothExceptions() {
            var architecture = new FailingStartArchitecture();

            var exception = Assert.Throws<AggregateException>(architecture.Start);

            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(exception.InnerExceptions[0].Message, Is.EqualTo("startup failed"));
            Assert.That(exception.InnerExceptions[1].Message, Is.EqualTo("rollback failed"));
            Assert.That(architecture.IsStarted, Is.False);
        }

        [Test]
        public void SystemInitialization_CanResolveInitializedModelInSameScope() {
            ProbeSystem system = null;
            var model = new TestModel("model");

            m_Architecture.CreateRootScope("Root", scope => {
                scope.Register(model);
                scope.Register(system = new ProbeSystem());
            });

            Assert.That(system.ResolvedModel, Is.SameAs(model));
        }

        [Test]
        public void ActivationFailure_RemovesScopeAndRollsBackInitializedServices() {
            var initialized = new TrackingSystem();

            Assert.Throws<InvalidOperationException>(() =>
                m_Architecture.CreateRootScope("Broken", scope => {
                    scope.Register(initialized);
                    scope.Register(new FailingSystem());
                })
            );

            Assert.That(initialized.DeinitializeCount, Is.EqualTo(1));
            Assert.That(m_Architecture.RootScopes, Is.Empty);
        }

        [Test]
        public void CircularFactoryDependency_IsRejectedAndRolledBack() {
            Assert.Throws<InvalidOperationException>(() =>
                m_Architecture.CreateRootScope("Circular", scope => {
                    scope.RegisterScopedFactory<ServiceA>(resolver => new ServiceA(resolver.Resolve<ServiceB>()));
                    scope.RegisterScopedFactory<ServiceB>(resolver => new ServiceB(resolver.Resolve<ServiceA>()));
                })
            );

            Assert.That(m_Architecture.RootScopes, Is.Empty);
        }

        [Test]
        public void TransientFactory_DoesNotOwnCreatedInstancesByDefault() {
            var created = new System.Collections.Generic.List<DisposableService>();
            var scope = m_Architecture.CreateRootScope("Transient", configured =>
                configured.RegisterTransient<DisposableService>(_ => {
                    var service = new DisposableService();
                    created.Add(service);
                    return service;
                })
            );

            var first = scope.Resolve<DisposableService>();
            var second = scope.Resolve<DisposableService>();
            scope.Dispose();

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created, Has.All.Matches<DisposableService>(service => !service.IsDisposed));
        }

        [Test]
        public void RegisterExternal_DoesNotBindInitializeOrDisposeExternalInstance() {
            var external = new ExternalLifecycleService();
            var scope = m_Architecture.CreateRootScope(
                "External",
                configured => configured.RegisterExternal(external)
            );

            Assert.That(scope.Resolve<ExternalLifecycleService>(), Is.SameAs(external));
            Assert.That(external.SetScopeCount, Is.Zero);
            Assert.That(external.InitializeCount, Is.Zero);

            scope.Dispose();

            Assert.That(external.DeinitializeCount, Is.Zero);
            Assert.That(external.DisposeCount, Is.Zero);
        }

        [Test]
        public void OwnedTransientFactory_DisposesEveryCreatedInstance() {
            var created = new System.Collections.Generic.List<DisposableService>();
            var scope = m_Architecture.CreateRootScope("OwnedTransient", configured =>
                configured.RegisterOwnedTransient<DisposableService>(_ => {
                    var service = new DisposableService();
                    created.Add(service);
                    return service;
                })
            );

            scope.Resolve<DisposableService>();
            scope.Resolve<DisposableService>();
            scope.Dispose();

            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created, Has.All.Matches<DisposableService>(service => service.IsDisposed));
        }

        [Test]
        public void Configuration_CanInspectRegistrationsButCannotResolveServices() {
            Assert.Throws<InvalidOperationException>(() =>
                m_Architecture.CreateRootScope("InvalidConfiguration", scope => {
                    scope.Register(new PlainService("configured"));
                    Assert.That(scope.IsRegisteredLocally<PlainService>(), Is.True);
                    scope.Resolve<PlainService>();
                })
            );

            Assert.That(m_Architecture.RootScopes, Is.Empty);
            Assert.That(m_Architecture.Scopes, Is.Empty);
        }

        [Test]
        public async Task AsyncInitialization_CompletesBeforeScopeBecomesActive() {
            var service = new AsyncService();
            var scope = await m_Architecture.CreateRootScopeAsync(
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
        public void MultipleArchitectures_DoNotShareServicesScopesOrEvents() {
            using var secondArchitecture = new Architecture();
            secondArchitecture.Start();

            var firstService = new PlainService("first");
            var secondService = new PlainService("second");
            var firstEventCount = 0;
            var secondEventCount = 0;

            var firstRoot = m_Architecture.CreateRootScope("Root", scope => scope.Register(firstService));
            var secondRoot = secondArchitecture.CreateRootScope("Root", scope => scope.Register(secondService));
            m_Architecture.Subscribe<ProbeEvent>(_ => firstEventCount++);
            secondArchitecture.Subscribe<ProbeEvent>(_ => secondEventCount++);

            m_Architecture.Publish(new ProbeEvent());

            Assert.That(firstRoot.Resolve<PlainService>(), Is.SameAs(firstService));
            Assert.That(secondRoot.Resolve<PlainService>(), Is.SameAs(secondService));
            Assert.That(firstEventCount, Is.EqualTo(1));
            Assert.That(secondEventCount, Is.Zero);
        }

        [Test]
        public void ScopePublish_IsLocalAndBubblePropagationDoesNotReachArchitecture() {
            var architectureCount = 0;
            var rootCount = 0;
            var childCount = 0;
            var root = m_Architecture.CreateRootScope("Root", _ => { });
            var child = root.CreateChild("Child", _ => { });
            m_Architecture.Subscribe<ProbeEvent>(_ => architectureCount++);
            root.Subscribe<ProbeEvent>(_ => rootCount++);
            child.Subscribe<ProbeEvent>(_ => childCount++);

            child.Publish(new ProbeEvent());

            Assert.That(childCount, Is.EqualTo(1));
            Assert.That(rootCount, Is.Zero);
            Assert.That(architectureCount, Is.Zero);

            child.Publish(new ProbeEvent(), EEventPropagation.Bubble);

            Assert.That(childCount, Is.EqualTo(2));
            Assert.That(rootCount, Is.EqualTo(1));
            Assert.That(architectureCount, Is.Zero);
        }

        [Test]
        public void ScopeUnsubscribe_RemovesTrackedSubscription() {
            var calls = 0;
            var root = m_Architecture.CreateRootScope("Root", _ => { });
            Action<ProbeEvent> handler = _ => calls++;

            root.Subscribe(handler);
            root.Unsubscribe(handler);
            root.Publish(new ProbeEvent());

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void ArchitectureEvent_InvokesAllSubscribersBeforeThrowingAggregate() {
            var calls = new System.Collections.Generic.List<string>();
            m_Architecture.Subscribe<ProbeEvent>(_ => calls.Add("first"));
            m_Architecture.Subscribe<ProbeEvent>(_ => throw new InvalidOperationException("subscriber failed"));
            m_Architecture.Subscribe<ProbeEvent>(_ => calls.Add("last"));

            var exception = Assert.Throws<AggregateException>(() =>
                m_Architecture.Publish(new ProbeEvent())
            );

            Assert.That(calls, Is.EqualTo(new[] { "first", "last" }));
            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(exception.InnerExceptions[0].Message, Is.EqualTo("subscriber failed"));
        }

        [Test]
        public void ScopePublish_ContinuesToParentsAfterSubscriberFailure() {
            var rootCalls = 0;
            var childCalls = 0;
            var root = m_Architecture.CreateRootScope("Root", _ => { });
            var child = root.CreateChild("Child", _ => { });
            child.Subscribe<ProbeEvent>(_ => throw new InvalidOperationException("child failed"));
            child.Subscribe<ProbeEvent>(_ => childCalls++);
            root.Subscribe<ProbeEvent>(_ => rootCalls++);

            var exception = Assert.Throws<AggregateException>(() =>
                child.Publish(new ProbeEvent(), EEventPropagation.Bubble)
            );

            Assert.That(childCalls, Is.EqualTo(1));
            Assert.That(rootCalls, Is.EqualTo(1));
            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
        }

        [Test]
        public void PatternPublishEvent_ReachesArchitectureOnly() {
            var architectureCount = 0;
            var localCount = 0;
            EventSenderSystem sender = null;
            var root = m_Architecture.CreateRootScope("Root", scope => scope.Register(sender = new EventSenderSystem()));
            m_Architecture.Subscribe<ProbeEvent>(_ => architectureCount++);
            root.Subscribe<ProbeEvent>(_ => localCount++);

            sender.Raise();

            Assert.That(architectureCount, Is.EqualTo(1));
            Assert.That(localCount, Is.Zero);
        }

        [Test]
        public void PatternEventSubscription_IsRemovedWhenOwningScopeIsDisposed() {
            ArchitectureEventListenerSystem listener = null;
            var scope = m_Architecture.CreateRootScope(
                "Game",
                configured => configured.Register(listener = new ArchitectureEventListenerSystem())
            );

            m_Architecture.Publish(new ProbeEvent());
            Assert.That(listener.CallCount, Is.EqualTo(1));

            scope.Dispose();
            m_Architecture.Publish(new ProbeEvent());

            Assert.That(listener.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void Shutdown_WithScopeTrackedArchitectureSubscription_CleansUpWithoutError() {
            var scope = m_Architecture.CreateRootScope(
                "Game",
                configured => configured.Register(new ArchitectureEventListenerSystem())
            );

            Assert.DoesNotThrow(m_Architecture.Shutdown);
            Assert.That(scope.IsDisposed, Is.True);
        }

        [Test]
        public void UnsubscribeEvent_DoesNotRemoveMatchingScopedEventSubscription() {
            EventSpaceListenerSystem listener = null;
            var scope = m_Architecture.CreateRootScope(
                "Game",
                configured => configured.Register(listener = new EventSpaceListenerSystem())
            );

            listener.UnsubscribeDefault();
            m_Architecture.Publish(new ProbeEvent());
            scope.Publish(new ProbeEvent());

            Assert.That(listener.CallCount, Is.EqualTo(1));
        }

        [TestCase(nameof(ArchitectureCapabilityExtensions.SubscribeScopedEvent), typeof(ICanSubscribeEvent))]
        [TestCase(nameof(ArchitectureCapabilityExtensions.UnsubscribeScopedEvent), typeof(ICanSubscribeEvent))]
        [TestCase(nameof(ArchitectureCapabilityExtensions.PublishScopedEvent), typeof(ICanPublishEvent))]
        public void ScopedEventExtensions_RespectEventCapabilities(string methodName, Type receiverType) {
            var method = Array.Find(
                typeof(ArchitectureCapabilityExtensions).GetMethods(),
                candidate => candidate.Name == methodName
            );

            Assert.That(method, Is.Not.Null);
            Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(receiverType));
        }

        [Test]
        public void DeinitializableOnlyService_IsDeinitializedWithItsScope() {
            var service = new DeinitializableOnlyService();
            var scope = m_Architecture.CreateRootScope("Root", configured => configured.Register(service));

            scope.Dispose();

            Assert.That(service.DeinitializeCount, Is.EqualTo(1));
        }

        [Test]
        public void ManualUnregister_RemovesHandleFromUnregisterList() {
            var owner = new UnregisterOwner();
            var unregisterCount = 0;
            var unregister = new CustomUnregister(() => unregisterCount++)
                .AddToUnregisterList(owner);

            Assert.That(owner.UnregisterList, Has.Count.EqualTo(1));

            unregister.Unregister();

            Assert.That(owner.UnregisterList, Is.Empty);
            Assert.That(unregisterCount, Is.EqualTo(1));

            owner.UnregisterAll();
            Assert.That(unregisterCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposingParent_DisposesChildrenBeforeParentServices() {
            var order = new System.Collections.Generic.List<string>();
            var root = m_Architecture.CreateRootScope("Root", scope => scope.Register(new OrderedService("root", order)));
            var child = root.CreateChild("Child", scope => scope.Register(new OrderedService("child", order)));

            root.Dispose();

            Assert.That(child.IsDisposed, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "child", "root" }));
        }

        [Test]
        public void Shutdown_RejectsScopeCreationFromDeinitialize() {
            Exception creationException = null;

            m_Architecture.CreateRootScope("Root", scope =>
                scope.Register(new CallbackService(() => {
                    try {
                        m_Architecture.CreateRootScope("Leaked", _ => { });
                    } catch (Exception exception) {
                        creationException = exception;
                    }
                }))
            );

            m_Architecture.Shutdown();

            Assert.That(creationException, Is.TypeOf<InvalidOperationException>());
            Assert.That(m_Architecture.RootScopes, Is.Empty);
            Assert.That(m_Architecture.Scopes, Is.Empty);
        }

        [Test]
        public void DisposingScope_RejectsChildCreationFromDeinitialize() {
            ArchitectureScope root = null;
            Exception creationException = null;

            root = m_Architecture.CreateRootScope("Root", scope =>
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
            Assert.That(m_Architecture.Scopes, Is.Empty);
        }

        [Test]
        public void ThrowingUnhandledExceptionHandler_DoesNotInterruptScopeCleanup() {
            var cleanupCount = 0;
            var cleaned = new CallbackService(() => cleanupCount++);
            m_Architecture.UnhandledExceptionHandler = _ => throw new InvalidOperationException("Reporter failed.");

            var scope = m_Architecture.CreateRootScope("Root", configured => {
                configured.Register(cleaned);
                configured.Register<IDeinitializable>(
                    new CallbackService(() => throw new InvalidOperationException("Cleanup failed."))
                );
            });

            Assert.DoesNotThrow(scope.Dispose);
            Assert.That(cleanupCount, Is.EqualTo(1));
            Assert.That(scope.IsDisposed, Is.True);
        }

        [Test]
        public void CleanupFailure_ThrowsAfterRemainingServicesAreCleaned() {
            var cleanupCount = 0;
            var scope = m_Architecture.CreateRootScope("Root", configured => {
                configured.Register(new CallbackService(() => cleanupCount++));
                configured.Register<IDeinitializable>(
                    new CallbackService(() => throw new InvalidOperationException("Cleanup failed."))
                );
            });

            var exception = Assert.Throws<InvalidOperationException>(scope.Dispose);

            Assert.That(exception.Message, Is.EqualTo("Cleanup failed."));
            Assert.That(cleanupCount, Is.EqualTo(1));
            Assert.That(scope.IsDisposed, Is.True);
        }

        [Test]
        public async Task ShutdownAsync_AwaitsAsyncDeinitialization() {
            var service = new BlockingAsyncDeinitializableService();
            m_Architecture.CreateRootScope("AsyncCleanup", scope => scope.Register(service));

            var shutdown = m_Architecture.ShutdownAsync();
            await service.Started.Task;

            Assert.That(shutdown.IsCompleted, Is.False);
            service.Release.SetResult(true);
            await shutdown;

            Assert.That(service.IsDeinitialized, Is.True);
            Assert.That(m_Architecture.RootScopes, Is.Empty);
        }

        [Test]
        public void SynchronousDispose_RejectsAsyncOnlyDeinitialization() {
            var scope = m_Architecture.CreateRootScope(
                "AsyncCleanup",
                configured => configured.Register(new AsyncOnlyDeinitializableService())
            );

            var exception = Assert.Throws<InvalidOperationException>(scope.Dispose);

            Assert.That(exception.Message, Does.Contain("DisposeAsync"));
            Assert.That(scope.IsDisposed, Is.True);
        }

        [Test]
        public void ChildAlias_DoesNotRebindParentServiceContext() {
            var system = new AliasSystem();
            var root = m_Architecture.CreateRootScope("Root", scope => scope.Register(system));
            var child = root.CreateChild("Child", scope => scope.RegisterAlias<IAliasSystem, AliasSystem>());

            Assert.That(child.Resolve<IAliasSystem>(), Is.SameAs(system));
            Assert.That(system.GetScope(), Is.SameAs(root));

            child.Dispose();

            Assert.That(system.GetScope(), Is.SameAs(root));
        }

        [Test]
        public void AbstractModelAndSystem_GetScopeBeforeBinding_ThrowClearErrors() {
            var model = new TestModel("unbound");
            var system = new EventSenderSystem();

            var modelException = Assert.Throws<InvalidOperationException>(() => model.GetScope());
            var systemException = Assert.Throws<InvalidOperationException>(() => system.GetScope());

            Assert.That(modelException.Message, Does.Contain("has not been bound"));
            Assert.That(systemException.Message, Does.Contain("has not been bound"));
        }

        [Test]
        public void AbstractSystem_CannotBeRegisteredInAnotherScope() {
            var system = new EventSenderSystem();
            var root = m_Architecture.CreateRootScope("Root", configured => configured.Register(system));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                m_Architecture.CreateRootScope("Other", configured => configured.Register(system))
            );

            Assert.That(exception.Message, Does.Contain("already bound"));
            Assert.That(system.GetScope(), Is.SameAs(root));
            Assert.That(m_Architecture.RootScopes, Is.EqualTo(new[] { root }));
        }

        [Test]
        public void AbstractModel_CannotBeRegisteredInAnotherScope() {
            var model = new TestModel("shared");
            var root = m_Architecture.CreateRootScope("Root", configured => configured.Register(model));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                m_Architecture.CreateRootScope("Other", configured => configured.Register(model))
            );

            Assert.That(exception.Message, Does.Contain("already bound"));
            Assert.That(model.GetScope(), Is.SameAs(root));
            Assert.That(m_Architecture.RootScopes, Is.EqualTo(new[] { root }));
        }

        [Test]
        public void InvalidAsyncTimeout_DoesNotLeaveUnconfiguredScope() {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await m_Architecture.CreateRootScopeAsync(
                    "InvalidTimeout",
                    (_, _) => Task.CompletedTask,
                    timeout: TimeSpan.FromMilliseconds(-2)
                )
            );

            Assert.That(m_Architecture.RootScopes, Is.Empty);
            Assert.That(m_Architecture.Scopes, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AsyncSetup_CanObserveTimeoutCancellation() {
            var creating = m_Architecture.CreateRootScopeAsync(
                "Timeout",
                async (_, token) => await Task.Delay(Timeout.InfiniteTimeSpan, token),
                timeout: TimeSpan.FromMilliseconds(20)
            );

            yield return WaitForTask(creating);
            Assert.Catch<TaskCanceledException>(() => creating.GetAwaiter().GetResult());

            Assert.That(m_Architecture.RootScopes, Is.Empty);
            Assert.That(m_Architecture.Scopes, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AsyncScope_IsNotPublishedUntilActivationCompletes() {
            var service = new BlockingAsyncService();
            var creating = m_Architecture.CreateRootScopeAsync(
                "Pending",
                scope => {
                    scope.Register(service);
                    return Task.CompletedTask;
                }
            );

            yield return WaitForTask(service.Started.Task);

            Assert.That(m_Architecture.RootScopes, Is.Empty);
            Assert.That(m_Architecture.Scopes, Is.Empty);

            service.Release.SetResult(true);
            yield return WaitForTask(creating);
            var scope = creating.GetAwaiter().GetResult();

            Assert.That(scope.State, Is.EqualTo(EScopeState.Active));
            Assert.That(m_Architecture.RootScopes, Is.EqualTo(new[] { scope }));
            Assert.That(m_Architecture.Scopes, Is.EqualTo(new[] { scope }));
        }

        [UnityTest]
        public IEnumerator Shutdown_DuringAsyncInitialization_CannotReactivateDisposedScope() {
            var service = new BlockingAsyncService();
            ArchitectureScope pendingScope = null;
            var creating = m_Architecture.CreateRootScopeAsync(
                "Pending",
                scope => {
                    pendingScope = scope;
                    scope.Register(service);
                    return Task.CompletedTask;
                }
            );

            yield return WaitForTask(service.Started.Task);
            m_Architecture.Shutdown();

            Assert.That(pendingScope.State, Is.EqualTo(EScopeState.Disposed));
            Assert.That(m_Architecture.RootScopes, Is.Empty);
            Assert.That(m_Architecture.Scopes, Is.Empty);

            service.Release.SetResult(true);

            yield return WaitForTask(creating);
            Assert.Catch<OperationCanceledException>(() => creating.GetAwaiter().GetResult());
            Assert.That(pendingScope.State, Is.EqualTo(EScopeState.Disposed));
        }

        [UnityTest]
        public IEnumerator DisposingParent_CancelsPendingChildActivation() {
            var parent = m_Architecture.CreateRootScope("Parent", _ => { });
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

            yield return WaitForTask(service.Started.Task);
            Assert.That(parent.Children, Is.Empty);
            Assert.That(m_Architecture.Scopes, Is.EqualTo(new[] { parent }));

            parent.Dispose();
            service.Release.SetResult(true);

            yield return WaitForTask(creating);
            Assert.Catch<OperationCanceledException>(() => creating.GetAwaiter().GetResult());
            Assert.That(pendingChild.State, Is.EqualTo(EScopeState.Disposed));
            Assert.That(m_Architecture.Scopes, Is.Empty);
        }

        [Test]
        public void Shutdown_MakesArchitectureInstanceOneShot() {
            m_Architecture.Shutdown();

            var exception = Assert.Throws<InvalidOperationException>(m_Architecture.Start);

            Assert.That(exception.Message, Does.Contain("cannot be restarted"));
        }

        [Test]
        public void BindablePropertyComparer_RejectsNull() {
            Assert.Throws<ArgumentNullException>(() => BindableProperty<int>.Comparer = null);
            Assert.That(BindableProperty<int>.Comparer, Is.Not.Null);
        }

        private sealed class ScopeCreatingArchitecture : Architecture {
            public ArchitectureScope StartedScope { get; private set; }

            protected override void OnStart() =>
                StartedScope = CreateRootScope("Started", _ => { });
        }

        private sealed class FailingStartArchitecture : Architecture {
            protected override void OnStart() => throw new InvalidOperationException("startup failed");
            protected override void OnShutdown() => throw new InvalidOperationException("rollback failed");
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

        private sealed class AsyncOnlyDeinitializableService : IAsyncDeinitializable {
            public Task DeinitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class BlockingAsyncDeinitializableService : IAsyncDeinitializable {
            public TaskCompletionSource<bool> Started { get; } = new();
            public TaskCompletionSource<bool> Release { get; } = new();
            public bool IsDeinitialized { get; private set; }

            public async Task DeinitializeAsync(CancellationToken cancellationToken) {
                Started.TrySetResult(true);
                await Release.Task;
                IsDeinitialized = true;
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

        private static IEnumerator WaitForTask(Task task) {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

            while (!task.IsCompleted) {
                if (DateTime.UtcNow >= deadline) {
                    Assert.Fail("Timed out waiting for the asynchronous scope operation.");
                }

                yield return null;
            }
        }

        private sealed class PlainService {
            public string Value { get; }
            public PlainService(string value) => Value = value;
        }

        private sealed class DisposableService : IDisposable {
            public bool IsDisposed { get; private set; }
            public void Dispose() => IsDisposed = true;
        }

        private sealed class ExternalLifecycleService :
            ICanSetScope,
            IInitializable,
            IDeinitializable,
            IDisposable {
            public int SetScopeCount { get; private set; }
            public int InitializeCount { get; private set; }
            public int DeinitializeCount { get; private set; }
            public int DisposeCount { get; private set; }

            public void SetScope(ArchitectureScope scope) => SetScopeCount++;
            public void Initialize() => InitializeCount++;
            public void Deinitialize() => DeinitializeCount++;
            public void Dispose() => DisposeCount++;
        }

        private readonly struct ProbeEvent { }

        private sealed class EventSenderSystem : AbstractSystem {
            protected override void OnInit() { }
            public void Raise() => this.PublishEvent(new ProbeEvent());
        }

        private sealed class ArchitectureEventListenerSystem : AbstractSystem {
            public int CallCount { get; private set; }

            protected override void OnInit() =>
                this.SubscribeEvent<ProbeEvent>(_ => CallCount++);
        }

        private sealed class EventSpaceListenerSystem : AbstractSystem {
            private Action<ProbeEvent> m_Handler;

            public int CallCount { get; private set; }

            protected override void OnInit() {
                m_Handler = _ => CallCount++;
                this.SubscribeEvent(m_Handler);
                this.SubscribeScopedEvent(m_Handler);
            }

            public void UnsubscribeDefault() => this.UnsubscribeEvent(m_Handler);
        }

        private sealed class DeinitializableOnlyService : IDeinitializable {
            public int DeinitializeCount { get; private set; }
            public void Deinitialize() => DeinitializeCount++;
        }

        private sealed class UnregisterOwner : IUnregisterList {
            public System.Collections.Generic.List<IUnregister> UnregisterList { get; } = new();
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
