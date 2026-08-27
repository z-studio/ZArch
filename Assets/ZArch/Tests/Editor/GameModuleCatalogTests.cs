using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
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

        [TestCase("", "Game1", "empty ID")]
        [TestCase("game-1", "", "empty scene name or path")]
        public void Validate_RejectsIncompleteModule(string id, string scene, string expectedMessage) {
            var catalog = CreateCatalog(CreateModule(id, scene));

            var exception = Assert.Throws<InvalidOperationException>(catalog.Validate);

            Assert.That(exception.Message, Does.Contain(expectedMessage));
        }

        private CatalogTestGameModuleAsset CreateModule(string id, string scene) {
            var module = ScriptableObject.CreateInstance<CatalogTestGameModuleAsset>();
            module.name = string.IsNullOrEmpty(id) ? "IncompleteModule" : id;
            SetField(typeof(UnityGameModuleAsset), module, "m_Id", id);
            SetField(typeof(UnityGameModuleAsset), module, "m_SceneNameOrPath", scene);
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
    }

    public sealed class CatalogTestGameModuleAsset : UnityGameModuleAsset {
        public override void Configure(ArchitectureScope scope, GameLaunchContext context) { }
    }
}
