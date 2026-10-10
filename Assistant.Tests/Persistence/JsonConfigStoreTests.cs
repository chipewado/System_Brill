using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Assistant.Core.Data;
using Assistant.Core.Visualization;
using Assistant.Persistence;
using NUnit.Framework;
using SysPath = System.IO.Path;

namespace Assistant.Tests.Persistence
{
    [TestFixture]
    public class JsonConfigStoreTests
    {
        private string _testDir = null!;
        private JsonConfigStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _testDir = SysPath.Combine(SysPath.GetTempPath(), "SystemBrill_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _store = new JsonConfigStore(_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch
            {
                // Best effort test cleanup
            }
        }

        #region Contract Tests (Section 6.4)

        [Test]
        public void SaveAndLoad_DisplayModeList_RoundTripsAccurately()
        {
            var modes = new List<DisplayMode>
            {
                new DisplayMode("m1")
                {
                    Name = "Test Mode 1",
                    Color = new ColorRgba(10, 20, 30, 200),
                    KeyBinding = "F2",
                    Filter = FilterMode.Inclusive,
                    Rarities = new HashSet<TokenRarity> { TokenRarity.Rare, TokenRarity.Epic },
                    MinValue = 25f,
                    MaxDistance = 150f,
                    ShowTracer = true,
                    Enabled = true,
                    Priority = 5
                },
                new DisplayMode("m2")
                {
                    Name = "Unlimited Distance Mode",
                    Color = ColorRgba.Cyan,
                    MaxDistance = null // unlimited
                }
            };

            _store.Save("modes_key", modes);
            var loaded = _store.Load<List<DisplayMode>>("modes_key");

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Count, Is.EqualTo(2));

            var loadedM1 = loaded[0];
            Assert.That(loadedM1.Id, Is.EqualTo("m1"));
            Assert.That(loadedM1.Name, Is.EqualTo("Test Mode 1"));
            Assert.That(loadedM1.Color, Is.EqualTo(new ColorRgba(10, 20, 30, 200)));
            Assert.That(loadedM1.KeyBinding, Is.EqualTo("F2"));
            Assert.That(loadedM1.Filter, Is.EqualTo(FilterMode.Inclusive));
            Assert.That(loadedM1.Rarities.Contains(TokenRarity.Rare), Is.True);
            Assert.That(loadedM1.Rarities.Contains(TokenRarity.Epic), Is.True);
            Assert.That(loadedM1.MinValue, Is.EqualTo(25f));
            Assert.That(loadedM1.MaxDistance, Is.EqualTo(150f));
            Assert.That(loadedM1.ShowTracer, Is.True);
            Assert.That(loadedM1.Enabled, Is.True);
            Assert.That(loadedM1.Priority, Is.EqualTo(5));

            var loadedM2 = loaded[1];
            Assert.That(loadedM2.Id, Is.EqualTo("m2"));
            Assert.That(loadedM2.MaxDistance, Is.Null, "Nullable MaxDistance must round-trip null correctly");
        }

        [Test]
        public void SaveAndLoad_ColorRgba_SerializesAsHexAndPreservesExactByteValues()
        {
            var color = new ColorRgba(128, 64, 32, 240);
            var mode = new DisplayMode("color_test") { Color = color };

            _store.Save("color_mode", mode);

            string fileJson = File.ReadAllText(SysPath.Combine(_testDir, "color_mode.json"));
            Assert.That(fileJson.Contains("#804020F0"), Is.True, "JSON must contain serialized hex code #804020F0");

            var loaded = _store.Load<DisplayMode>("color_mode");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Color.R, Is.EqualTo((byte)128));
            Assert.That(loaded.Color.G, Is.EqualTo((byte)64));
            Assert.That(loaded.Color.B, Is.EqualTo((byte)32));
            Assert.That(loaded.Color.A, Is.EqualTo((byte)240));
        }

        [Test]
        public void Save_WrapsPayloadWithSchemaVersionEnvelope()
        {
            _store.Save("test_env", new OverlayConfig { MaxVisibleMarkers = 42 });

            string rawJson = File.ReadAllText(SysPath.Combine(_testDir, "test_env.json"));
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            Assert.That(root.TryGetProperty("schemaVersion", out var versionElement), Is.True);
            Assert.That(versionElement.GetInt32(), Is.EqualTo(1));
            Assert.That(root.TryGetProperty("payload", out _), Is.True);
        }

        [Test]
        public void Load_NonExistentOrCorruptFile_ReturnsNullWithoutThrowing()
        {
            Assert.That(_store.Load<OverlayConfig>("non_existent_key"), Is.Null);

            // Corrupt file with invalid JSON
            File.WriteAllText(SysPath.Combine(_testDir, "corrupt.json"), "{ invalid: json... !!");
            Assert.DoesNotThrow(() =>
            {
                var result = _store.Load<OverlayConfig>("corrupt");
                Assert.That(result, Is.Null);
            });
        }

        [Test]
        public void Save_AtomicWrite_WritesToTempAndMovesOverwritingTarget()
        {
            _store.Save("atomic_key", new OverlayConfig { MaxVisibleMarkers = 10 });
            Assert.That(File.Exists(SysPath.Combine(_testDir, "atomic_key.json")), Is.True);

            // Update configuration: replaces atomically
            _store.Save("atomic_key", new OverlayConfig { MaxVisibleMarkers = 20 });
            var loaded = _store.Load<OverlayConfig>("atomic_key");
            Assert.That(loaded!.MaxVisibleMarkers, Is.EqualTo(20));

            // Verify no stray .tmp files left in test directory
            var tmpFiles = Directory.GetFiles(_testDir, "*.tmp");
            Assert.That(tmpFiles.Length, Is.EqualTo(0), "No temporary files should remain after successful save");
        }

        [Test]
        public void ValidateKey_PathSeparatorsOrDirectoryTraversal_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _store.Save("sub/folder", new OverlayConfig()));
            Assert.Throws<ArgumentException>(() => _store.Save("sub\\folder", new OverlayConfig()));
            Assert.Throws<ArgumentException>(() => _store.Save("../parent", new OverlayConfig()));
            Assert.Throws<ArgumentException>(() => _store.Save("..", new OverlayConfig()));
            Assert.Throws<ArgumentException>(() => _store.Save("", new OverlayConfig()));
            Assert.Throws<ArgumentException>(() => _store.Save("   ", new OverlayConfig()));
            Assert.Throws<ArgumentException>(() => _store.Save("key:invalid", new OverlayConfig()));
        }

        #endregion

        #region Additional Hardening & Boundary Tests

        [Test]
        public void Load_NewerOrUnsupportedSchemaVersion_ReturnsNullWithoutOverwriting()
        {
            string futureJson = "{\n  \"schemaVersion\": 99,\n  \"payload\": { \"MaxVisibleMarkers\": 100 }\n}";
            string filePath = SysPath.Combine(_testDir, "future_version.json");
            File.WriteAllText(filePath, futureJson);

            var loaded = _store.Load<OverlayConfig>("future_version");
            Assert.That(loaded, Is.Null, "Unsupported schemaVersion must return null");

            // File must remain intact and not overwritten
            Assert.That(File.ReadAllText(filePath), Is.EqualTo(futureJson));
        }

        [Test]
        public void ColorRgba_FromHex_SupportsVariousFormatsAndFallback()
        {
            // 6 chars uppercase & lowercase
            var c1 = ColorRgba.FromHex("#FF00AA");
            Assert.That(c1.R, Is.EqualTo(255));
            Assert.That(c1.G, Is.EqualTo(0));
            Assert.That(c1.B, Is.EqualTo(170));
            Assert.That(c1.A, Is.EqualTo(255));

            var c2 = ColorRgba.FromHex("ff00aa");
            Assert.That(c2, Is.EqualTo(c1));

            // 8 chars with alpha
            var c3 = ColorRgba.FromHex("#12345678");
            Assert.That(c3.A, Is.EqualTo(0x78));

            // Invalid format returns White fallback
            var cFallback = ColorRgba.FromHex("not_a_hex");
            Assert.That(cFallback, Is.EqualTo(ColorRgba.White));

            var cEmpty = ColorRgba.FromHex(null);
            Assert.That(cEmpty, Is.EqualTo(ColorRgba.White));
        }

        [Test]
        public void Save_FailedSerializationOrWriting_DoesNotCorruptExistingValidConfiguration()
        {
            // Save initial valid config
            _store.Save("valid_key", new OverlayConfig { MaxVisibleMarkers = 33 });
            string filePath = SysPath.Combine(_testDir, "valid_key.json");
            string originalContent = File.ReadAllText(filePath);

            // Attempt saving null throws ArgumentNullException
            Assert.Throws<ArgumentNullException>(() => _store.Save<OverlayConfig>("valid_key", null!));

            // Verify file was not touched
            Assert.That(File.ReadAllText(filePath), Is.EqualTo(originalContent));

            // Force atomic write failure during Save by locking target file with FileAccess.ReadWrite, FileShare.None (Windows-semantics lock test)
            using (var lockStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(() => _store.Save("valid_key", new OverlayConfig { MaxVisibleMarkers = 99 }), Throws.Exception);
            }

            // Assert existing file is intact and no temporary .tmp files remain
            Assert.That(File.ReadAllText(filePath), Is.EqualTo(originalContent));
            var tmpFiles = Directory.GetFiles(_testDir, "*.tmp");
            Assert.That(tmpFiles.Length, Is.EqualTo(0), "All temporary files must be cleaned up on write failure");
        }

        private sealed class ThrowingPayload
        {
            public string FirstProperty { get; set; } = "safe_start";
            public string FailingProperty => throw new InvalidOperationException("Simulated failure during serialization");
        }

        [Test]
        public void Save_FailureDuringSerialization_PreservesExistingFileAndCleansUpTemp()
        {
            // Note: The FileShare.None lock test in Save_FailedSerializationOrWriting relies on Windows file-locking semantics.
            // This test forces a failure DURING serialization to guarantee atomic write behavior across all platforms.
            string key = "atomic_serialization_key";
            _store.Save(key, new OverlayConfig { MaxVisibleMarkers = 42 });
            string filePath = SysPath.Combine(_testDir, $"{key}.json");
            byte[] originalBytes = File.ReadAllBytes(filePath);

            Assert.Throws<InvalidOperationException>(() => _store.Save(key, new ThrowingPayload()));

            byte[] currentBytes = File.ReadAllBytes(filePath);
            Assert.That(currentBytes, Is.EqualTo(originalBytes), "Existing file content must remain byte-identical after failed serialization");

            var tmpFiles = Directory.GetFiles(_testDir, "*.tmp");
            Assert.That(tmpFiles, Is.Empty, "No temporary .tmp files must remain after serialization failure");
        }

        [Test]
        public void SaveAndLoad_GoldenJsonComparison_ContainsNoInfinityOrNaN()
        {
            var templates = new List<DisplayMode>
            {
                BuiltInTemplates.CreateAllTokens(),
                BuiltInTemplates.CreateRarePlus(),
                BuiltInTemplates.CreateLegendaryOnly(),
                BuiltInTemplates.CreateNearby(150f),
                BuiltInTemplates.CreateBestValue(75f)
            };

            _store.Save("golden_modes", templates);

            string json = File.ReadAllText(SysPath.Combine(_testDir, "golden_modes.json"));

            Assert.That(json.Contains("Infinity"), Is.False, "Persistent JSON must never contain Infinity");
            Assert.That(json.Contains("NaN"), Is.False, "Persistent JSON must never contain NaN");

            var loaded = _store.Load<List<DisplayMode>>("golden_modes");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Count, Is.EqualTo(5));
        }

        [Test]
        public void Delete_ExistingAndNonExistentKey_BehavesCorrectly()
        {
            _store.Save("delete_me", new OverlayConfig());
            Assert.That(_store.Exists("delete_me"), Is.True);

            bool deleted = _store.Delete("delete_me");
            Assert.That(deleted, Is.True);
            Assert.That(_store.Exists("delete_me"), Is.False);

            bool deleteAgain = _store.Delete("delete_me");
            Assert.That(deleteAgain, Is.False);
        }

        #endregion
    }
}
