using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZArch.GameModules;

namespace ZArch.Tests.Editor {
    [TestFixture]
    public sealed class GameModuleTests {
        private ArchitectureHost m_Host;
        private ArchitectureScope m_AppRoot;
        private FakeContentLoader m_ContentLoader;

        [SetUp]
        public void SetUp() {
            m_Host = new ArchitectureHost();
            m_Host.Start();
            m_AppRoot = m_Host.CreateRootScope("App", _ => { });
            m_ContentLoader = new FakeContentLoader();
        }

        [TearDown]
        public void TearDown() => m_Host.Dispose();

        [Test]
        public async Task EnterAsync_CreatesChildScope_ConfiguresModuleAndLoadsContent() {
            var service = new ModuleService("A");
            var module = new FakeModule("game-a", (scope, _) => scope.Register(service));
            var launcher = CreateLauncher(module);

            var session = await launcher.EnterAsync("game-a");

            Assert.That(launcher.Current, Is.SameAs(session));
            Assert.That(session.Scope.Parent, Is.SameAs(m_AppRoot));
            Assert.That(session.Scope.Resolve<ModuleService>(), Is.SameAs(service));
            Assert.That(m_ContentLoader.LoadedScopes, Is.EqualTo(new[] { session.Scope }));
        }

        [Test]
        public async Task EnterAsync_WhenGameIsActive_RequiresExplicitExit() {
            var gameA = new FakeModule("game-a");
            var gameB = new FakeModule("game-b");
            var launcher = CreateLauncher(gameA, gameB);
            var first = await launcher.EnterAsync("game-a");

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await launcher.EnterAsync("game-b")
            );

            Assert.That(exception.Message, Does.Contain("Call ExitAsync"));
            Assert.That(launcher.Current, Is.SameAs(first));
            Assert.That(first.Scope.IsDisposed, Is.False);
            Assert.That(m_AppRoot.Children, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExitThenEnter_DisposesPreviousSessionBeforeLoadingNext() {
            var launcher = CreateLauncher(new FakeModule("game-a"), new FakeModule("game-b"));
            var first = await launcher.EnterAsync("game-a");

            await launcher.ExitAsync();
            var second = await launcher.EnterAsync("game-b");

            Assert.That(launcher.Current, Is.SameAs(second));
            Assert.That(first.Scope.IsDisposed, Is.True);
            Assert.That(m_ContentLoader.UnloadedIds, Is.EqualTo(new[] { "game-a" }));
            Assert.That(m_AppRoot.Children, Has.Count.EqualTo(1));
        }

        [Test]
        public void EnterAsync_WhenCancelledBeforeCreation_DoesNotCreateSession() {
            var launcher = CreateLauncher(new FakeModule("game-a"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await launcher.EnterAsync("game-a", cancellationToken: cancellation.Token)
            );

            Assert.That(launcher.Current, Is.Null);
            Assert.That(m_AppRoot.Children, Is.Empty);
        }

        [Test]
        public async Task TransitionInProgress_RejectsAnotherTransition() {
            var launcher = CreateLauncher(new FakeModule("game-a"));
            m_ContentLoader.LoadGate = new TaskCompletionSource<bool>();

            var entering = launcher.EnterAsync("game-a");

            Assert.That(launcher.IsTransitioning, Is.True);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await launcher.ExitAsync());

            m_ContentLoader.LoadGate.SetResult(true);
            await entering;

            Assert.That(launcher.IsTransitioning, Is.False);
        }

        [Test]
        public async Task ExitAsync_WhenContentUnloadFails_StillDisposesScopeAndClearsCurrent() {
            var launcher = CreateLauncher(new FakeModule("game-a"));
            var session = await launcher.EnterAsync("game-a");
            m_ContentLoader.FailUnloading = true;

            Assert.ThrowsAsync<InvalidOperationException>(async () => await launcher.ExitAsync());

            Assert.That(launcher.Current, Is.Null);
            Assert.That(session.Scope.IsDisposed, Is.True);
        }

        [Test]
        public void Constructor_RejectsDuplicateModuleIds() {
            Assert.Throws<ArgumentException>(() => CreateLauncher(
                new FakeModule("duplicate"),
                new FakeModule("duplicate")
            ));
        }

        [Test]
        public void GameLaunchContext_ProvidesTypedArguments() {
            var arguments = new ModuleService("args");
            var context = new GameLaunchContext(arguments);

            Assert.That(context.GetArguments<ModuleService>(), Is.SameAs(arguments));
            Assert.That(context.TryGetArguments<string>(out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => context.GetArguments<string>());
        }

        [Test]
        public async Task ShutdownAsync_UnloadsCurrentContentAndMakesLauncherUnusable() {
            var launcher = CreateLauncher(new FakeModule("game-a"));
            var session = await launcher.EnterAsync("game-a");

            await launcher.ShutdownAsync();

            Assert.That(launcher.Current, Is.Null);
            Assert.That(session.Scope.IsDisposed, Is.True);
            Assert.That(m_ContentLoader.UnloadedIds, Is.EqualTo(new[] { "game-a" }));
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await launcher.EnterAsync("game-a")
            );
        }

        [Test]
        public async Task ShutdownAsync_CancelsAndWaitsForEnteringSession() {
            var launcher = CreateLauncher(new FakeModule("game-a"));
            m_ContentLoader.LoadGate = new TaskCompletionSource<bool>();
            var entering = launcher.EnterAsync("game-a");

            var shutdown = launcher.ShutdownAsync();

            Assert.That(shutdown.IsCompleted, Is.False);
            m_ContentLoader.LoadGate.SetResult(true);

            Assert.CatchAsync<OperationCanceledException>(async () => await entering);
            await shutdown;

            Assert.That(launcher.Current, Is.Null);
            Assert.That(m_AppRoot.Children, Is.Empty);
        }

        private GameLauncher CreateLauncher(params IGameModule[] modules) =>
            new(
                new GameScopeFactory(m_AppRoot),
                m_ContentLoader,
                modules
            );

        private sealed class FakeModule : IGameModule {
            private readonly Action<ArchitectureScope, GameLaunchContext> m_Configure;

            public string Id { get; }

            public FakeModule(
                string id,
                Action<ArchitectureScope, GameLaunchContext> configure = null
            ) {
                Id = id;
                m_Configure = configure;
            }

            public void Configure(ArchitectureScope scope, GameLaunchContext context) =>
                m_Configure?.Invoke(scope, context);
        }

        private sealed class FakeContentHandle : IGameContentHandle {
            public string GameId { get; }

            public FakeContentHandle(string gameId) {
                GameId = gameId;
            }
        }

        private sealed class FakeContentLoader : IGameContentLoader {
            public readonly HashSet<string> FailLoadingIds = new(StringComparer.Ordinal);
            public readonly List<ArchitectureScope> LoadedScopes = new();
            public readonly List<string> UnloadedIds = new();

            public TaskCompletionSource<bool> LoadGate { get; set; }
            public bool FailUnloading { get; set; }

            public async Task<IGameContentHandle> LoadAsync(
                IGameModule module,
                ArchitectureScope scope,
                GameLaunchContext context,
                CancellationToken cancellationToken
            ) {
                LoadedScopes.Add(scope);

                if (LoadGate != null) {
                    await LoadGate.Task;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (FailLoadingIds.Contains(module.Id)) {
                    throw new InvalidOperationException($"Failed to load {module.Id}.");
                }

                return new FakeContentHandle(module.Id);
            }

            public Task UnloadAsync(IGameContentHandle content) {
                var handle = (FakeContentHandle)content;
                UnloadedIds.Add(handle.GameId);

                if (FailUnloading) {
                    throw new InvalidOperationException($"Failed to unload {handle.GameId}.");
                }

                return Task.CompletedTask;
            }
        }

        private sealed class ModuleService {
            public string Value { get; }

            public ModuleService(string value) {
                Value = value;
            }
        }
    }
}
