using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assistant.Core.Data;
using Assistant.Core.Discovery;
using Assistant.Core.Visualization;
using Assistant.Persistence;
using Assistant.Sim;
using NUnit.Framework;
using SysPath = System.IO.Path;

namespace Assistant.Tests.Integration
{
    [TestFixture]
    public class DiscoveryToPersistenceIntegrationTests
    {
        private string _tempDir = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = SysPath.Combine(SysPath.GetTempPath(), "SystemBrill_Integration_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }

        [Test]
        public void FullPipeline_DiscoveryToRepositoryToDisplayFilteringToPersistence_DeterministicAndNonMutating()
        {
            // 1. Setup Discovery Pipeline
            var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var repository = new TokenRepository(50f);
            var parser = new DefaultTokenParser();
            var validator = new TokenValidator(clock, 60f);
            var gameApi = new SimGameApi(clock: clock);

            using var discoveryService = new TokenDiscoveryService(
                mapProvider: gameApi,
                repository: repository,
                parser: parser,
                validator: validator,
                clock: clock,
                gameApi: gameApi,
                ttlSeconds: 60f,
                scanRadius: 100f);

            // Spawn tokens into game world
            gameApi.Player.Teleport(new Vec3(0f, 0f, 0f));
            gameApi.SpawnToken(new TokenLocation { Id = "tok_common", Position = new Vec3(10f, 0f, 10f), Rarity = TokenRarity.Common, EstimatedValue = 10f });
            gameApi.SpawnToken(new TokenLocation { Id = "tok_rare", Position = new Vec3(20f, 0f, 20f), Rarity = TokenRarity.Rare, EstimatedValue = 50f });
            gameApi.SpawnToken(new TokenLocation { Id = "tok_legendary", Position = new Vec3(30f, 0f, 30f), Rarity = TokenRarity.Legendary, EstimatedValue = 200f });

            // 2. Scan world through TokenDiscoveryService
            int scanned = discoveryService.ScanRegion(new Region(new Vec3(0f, 0f, 0f), 100f));
            Assert.That(scanned, Is.EqualTo(3));
            Assert.That(repository.GetAvailable().Count, Is.EqualTo(3));

            // Verify domain tokens in repository
            var availableTokens = repository.GetAvailable();
            Assert.That(availableTokens.Count, Is.EqualTo(3));

            // 3. Setup Display Mode Manager with Rare+ template
            var configStore = new JsonConfigStore(_tempDir);
            var displayManager = new DisplayModeManager();
            displayManager.ApplyTemplate("Rare+");
            displayManager.UpdateConfig(new OverlayConfig
            {
                MaxVisibleMarkers = 10,
                MaxTracerCount = 5,
                TracerMaxDistance = 150f
            });

            // 4. Presentation Filtering: evaluate HUD markers and tracers
            var playerPos = gameApi.GetPlayerState().Position;
            var visibleMarkers = displayManager.FilterTokens(availableTokens, playerPos);
            var tracers = displayManager.GetTracerTokens(availableTokens, playerPos);

            // Only Rare and Legendary pass Rare+ filter; Common is excluded
            Assert.That(visibleMarkers.Count, Is.EqualTo(2));
            Assert.That(visibleMarkers[0].Id, Is.EqualTo("tok_legendary"), "Legendary must rank before Rare");
            Assert.That(visibleMarkers[1].Id, Is.EqualTo("tok_rare"));

            Assert.That(tracers.Count, Is.EqualTo(2));
            Assert.That(tracers[0].Id, Is.EqualTo("tok_legendary"));

            // Check dominant mode color and tracer property
            var dominantLeg = displayManager.GetDominantMode(visibleMarkers[0], playerPos);
            Assert.That(dominantLeg, Is.Not.Null);
            Assert.That(dominantLeg!.Color, Is.EqualTo(ColorRgba.Cyan));
            Assert.That(dominantLeg.ShowTracer, Is.True);

            // 5. CRITICAL ARCHITECTURAL CONTRACT: Verify domain repository was NOT mutated by presentation filtering
            Assert.That(repository.GetAvailable().Count, Is.EqualTo(3), "Domain repository must not have lost any tokens");
            Assert.That(repository.Get("tok_common"), Is.Not.Null, "Common token must remain active in domain repository");
            Assert.That(repository.Get("tok_rare"), Is.Not.Null);
            Assert.That(repository.Get("tok_legendary"), Is.Not.Null);

            // 6. Persistence: Save display modes & config
            displayManager.Save(configStore);
            Assert.That(configStore.Exists("display-modes"), Is.True);
            Assert.That(configStore.Exists("overlay-config"), Is.True);

            // 7. Restart simulation: Load into a brand new DisplayModeManager
            var restartedManager = new DisplayModeManager();
            restartedManager.Load(configStore);

            Assert.That(restartedManager.Modes.Count, Is.EqualTo(1));
            Assert.That(restartedManager.Modes[0].Id, Is.EqualTo(BuiltInTemplates.RarePlusId));
            Assert.That(restartedManager.Config.MaxVisibleMarkers, Is.EqualTo(10));

            // Re-filter after restart -> must yield 100% deterministic identical output
            var reloadedMarkers = restartedManager.FilterTokens(availableTokens, playerPos);
            Assert.That(reloadedMarkers.Select(t => t.Id), Is.EqualTo(visibleMarkers.Select(t => t.Id)));
        }
    }
}
