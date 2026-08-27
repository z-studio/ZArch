using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZArch.GameModules;
using ZArch.GameModules.Unity;

namespace ZArch.Tests.Editor {
    [TestFixture]
    public sealed class GameModuleCatalogTests {
        private readonly List<UnityEngine.Object> m_Assets = new();

        [TearDown]
        public void TearDown() {
            foreach (var asset in m_Assets) {
                UnityEngine.Object.DestroyImmediate(asset);
            }

            m_Assets.Clear();
        }

        [Test]
        public void Validate_AcceptsDistinctConfiguredModules() {
            var first = CreateModule("game-1", "Game1");
            var second = CreateModule("game-2", "Assets/Games/Game2.unity");
            var catalog = CreateCatalog(first, second);

            Assert.DoesNotThrow(catalog.Validate);
            Assert.That(catalog.Modules, Is.EqualTo(new[] { first, second }));
            Assert.That(catalog.Cast<IGameModule>(), Is.EqualTo(new IGameModule[] { first, second }));
        }

        [Test]
        public void Validate_RejectsDuplicateModuleIds() {
            var catalog = CreateCatalog(
                CreateModule("duplicate", "Game1"),
                CreateModule("duplicate", "Game2")
            );

            var exception = Assert.Throws<InvalidOperationException>(catalog.Validate);

            Assert.That(exception.Message, Does.Contain("duplicate module ID 'duplicate'"));
        }

        [Test]
        public void Validate_RejectsMissingModuleReference() {
            var catalog = CreateCatalog(CreateModule("game-1", "Game1"), null);

            var exception = Assert.Throws<InvalidOperationException>(catalog.Validate);

            Assert.That(exception.Message, Does.Contain("index 1"));
        }

        [Test]
        public void Enumeration_ValidatesCatalogAutomatically() {
            var catalog = CreateCatalog(CreateModule("duplicate", "Game1"), CreateModule("duplicate", "Game2"));

            Assert.Throws<InvalidOperationException>(() => catalog.ToArray());
        }

        [Test]
        public void ModuleAsset_UsesConfiguredSceneProviderAndLocation() {
            var module = CreateModule("game-1", "game-1-address", "addressables");

            Assert.That(module.SceneProviderId, Is.EqualTo("addressables"));
            Assert.That(module.SceneLocation, Is.EqualTo("game-1-address"));
        }

        [Test]
        public void Validate_RejectsEmptySceneProviderId() {
            var module = CreateModule("game-1", "Game1", "");
            var catalog = CreateCatalog(module);

            var exception = Assert.Throws<InvalidOperationException>(catalog.Validate);

            Assert.That(exception.Message, Does.Contain("empty scene provider ID"));
        }

        [Test]
        public void ContentLoader_WithoutProviders_RegistersBuildSettingsProvider() {
            var loader = new UnityGameContentLoader();

            Assert.That(loader.Providers.Keys, Is.EqualTo(new[] { GameSceneProviderIds.kBuildSettings }));
            Assert.That(loader.Providers[GameSceneProviderIds.kBuildSettings],
                Is.TypeOf<BuildSettingsGameSceneProvider>());
        }

        [Test]
        public void ContentLoader_RejectsDuplicateProviderIds() {
            Assert.Throws<ArgumentException>(() => new UnityGameContentLoader(
                new FakeSceneProvider("custom"),
                new FakeSceneProvider("custom")
            ));
        }

        [Test]
        public void ContentLoader_RoutesLocationAndRollsBackInvalidProviderResult() {
            var module = CreateModule("game-1", "game-1-address", "custom");
            var provider = new FakeSceneProvider("custom");
            var loader = new UnityGameContentLoader(provider);
            var host = new ArchitectureHost();
            host.Start();

            try {
                var scope = host.CreateRootScope("Test", _ => { });

                var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await loader.LoadAsync(
                        module,
                        scope,
                        GameLaunchContext.Empty,
                        CancellationToken.None
                    )
                );

                Assert.That(exception.Message, Does.Contain("invalid or unloaded scene"));
                Assert.That(provider.LoadedLocations, Is.EqualTo(new[] { "game-1-address" }));
                Assert.That(provider.UnloadCount, Is.EqualTo(1));
            } finally {
                host.Dispose();
            }
        }

        [TestCase("", "Game1", "empty ID")]
        [TestCase("game-1", "", "empty scene location")]
        public void Validate_RejectsIncompleteModule(string id, string scene, string expectedMessage) {
            var catalog = CreateCatalog(CreateModule(id, scene));

            var exception = Assert.Throws<InvalidOperationException>(catalog.Validate);

            Assert.That(exception.Message, Does.Contain(expectedMessage));
        }

        private CatalogTestGameModuleAsset CreateModule(
            string id,
            string scene,
            string providerId = GameSceneProviderIds.kBuildSettings
        ) {
            var module = ScriptableObject.CreateInstance<CatalogTestGameModuleAsset>();
            module.name = string.IsNullOrEmpty(id) ? "IncompleteModule" : id;
            SetField(typeof(UnityGameModuleAsset), module, "m_Id", id);
            SetField(typeof(UnityGameModuleAsset), module, "m_SceneProviderId", providerId);
            SetField(typeof(UnityGameModuleAsset), module, "m_SceneLocation", scene);
            m_Assets.Add(module);
            return module;
        }

        private GameModuleCatalog CreateCatalog(params UnityGameModuleAsset[] modules) {
            var catalog = ScriptableObject.CreateInstance<GameModuleCatalog>();
            catalog.name = "TestCatalog";
            SetField(typeof(GameModuleCatalog), catalog, "m_Modules", modules);
            m_Assets.Add(catalog);
            return catalog;
        }

        private static void SetField(Type owner, object target, string fieldName, object value) {
            var field = owner.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingFieldException(owner.FullName, fieldName);
            field.SetValue(target, value);
        }

        private sealed class FakeSceneProvider : IGameSceneProvider {
            private sealed class FakeSceneHandle : IGameSceneHandle {
                public Scene Scene => default;
            }

            public string Id { get; }
            public List<string> LoadedLocations { get; } = new();
            public int UnloadCount { get; private set; }

            public FakeSceneProvider(string id) {
                Id = id;
            }

            public Task<IGameSceneHandle> LoadAsync(
                string location,
                CancellationToken cancellationToken
            ) {
                LoadedLocations.Add(location);
                return Task.FromResult<IGameSceneHandle>(new FakeSceneHandle());
            }

            public Task UnloadAsync(
                IGameSceneHandle handle,
                CancellationToken cancellationToken
            ) {
                UnloadCount++;
                return Task.CompletedTask;
            }
        }
    }

    public sealed class CatalogTestGameModuleAsset : UnityGameModuleAsset {
        public override void Configure(ArchitectureScope scope, GameLaunchContext context) { }
    }
}
