using System;
using System.Collections.Generic;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;
using Assistant.Core.Visualization;
using NUnit.Framework;

namespace Assistant.Tests.Visualization
{
    [TestFixture]
    public class OverlayConfigTests
    {
        private class InMemoryConfigStore : IConfigStore
        {
            private readonly Dictionary<string, object> _storage = new Dictionary<string, object>();

            public void Save<T>(string key, T data) => _storage[key] = data!;
            public T? Load<T>(string key) => _storage.TryGetValue(key, out var val) ? (T)val : default;
            public bool Exists(string key) => _storage.ContainsKey(key);
            public bool Delete(string key) => _storage.Remove(key);
        }

        private class FailingConfigStore : IConfigStore
        {
            public void Save<T>(string key, T data) => throw new InvalidOperationException("Store disk failure");
            public T? Load<T>(string key) => throw new FormatException("Store corrupt data");
            public bool Exists(string key) => false;
            public bool Delete(string key) => false;
        }

        [Test]
        public void OverlayConfig_Clone_ProducesIndependentCopy()
        {
            var config = new OverlayConfig
            {
                TracerMaxDistance = 150f,
                MaxTracerCount = 5,
                MaxVisibleMarkers = 25,
                MinimapZoom = 2.5f
            };

            var clone = config.Clone();

            Assert.That(clone.TracerMaxDistance, Is.EqualTo(config.TracerMaxDistance));
            Assert.That(clone.MaxTracerCount, Is.EqualTo(config.MaxTracerCount));
            Assert.That(clone.MaxVisibleMarkers, Is.EqualTo(config.MaxVisibleMarkers));
            Assert.That(clone.MinimapZoom, Is.EqualTo(config.MinimapZoom));

            clone.MinimapZoom = 5.0f;
            Assert.That(config.MinimapZoom, Is.EqualTo(2.5f), "Mutating clone must not affect original");
        }

        [Test]
        public void DisplayModeManager_SaveAndLoad_RoundTripsThroughConfigStore()
        {
            var store = new InMemoryConfigStore();
            var manager = new DisplayModeManager();
            manager.ApplyTemplate("Rare+");
            manager.UpdateConfig(new OverlayConfig { MaxVisibleMarkers = 15, MinimapZoom = 1.8f });

            manager.Save(store);

            var newManager = new DisplayModeManager();
            newManager.Load(store);

            Assert.That(newManager.Modes.Count, Is.EqualTo(1));
            Assert.That(newManager.Modes[0].Id, Is.EqualTo(BuiltInTemplates.RarePlusId));
            Assert.That(newManager.Config.MaxVisibleMarkers, Is.EqualTo(15));
            Assert.That(newManager.Config.MinimapZoom, Is.EqualTo(1.8f));
        }

        [Test]
        public void DisplayModeManager_Load_CorruptOrFailingStore_RestoresSafeDefaultsWithoutThrowing()
        {
            var failingStore = new FailingConfigStore();
            var manager = new DisplayModeManager();

            Assert.DoesNotThrow(() => manager.Load(failingStore));
            Assert.That(manager.Modes.Count, Is.EqualTo(1));
            Assert.That(manager.Modes[0].Id, Is.EqualTo(BuiltInTemplates.AllId), "Must restore All template on corrupt load");
        }

        [Test]
        public void DisplayModeManager_Load_ClampsInvalidCapsAndSettings()
        {
            var store = new InMemoryConfigStore();
            // Save bad values
            var badConfig = new OverlayConfig
            {
                MaxVisibleMarkers = -10,
                MaxTracerCount = -5,
                TracerMaxDistance = -50f,
                MinimapZoom = float.NaN
            };
            store.Save(DisplayModeManager.OverlayConfigKey, badConfig);

            var manager = new DisplayModeManager();
            manager.Load(store);

            Assert.That(manager.Config.MaxVisibleMarkers, Is.EqualTo(0));
            Assert.That(manager.Config.MaxTracerCount, Is.EqualTo(0));
            Assert.That(manager.Config.TracerMaxDistance, Is.EqualTo(300f)); // Reset to default
            Assert.That(manager.Config.MinimapZoom, Is.EqualTo(1.0f));       // Reset to default
        }
    }
}
