using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Assistant.Core.Data;
using Assistant.Sim;

namespace Assistant.Tests
{
    [TestFixture]
    public class SimGameApiTests
    {
        private SimGameApi _api = null!;

        [SetUp]
        public void SetUp()
        {
            _api = new SimGameApi(new SimGridMap(50, 50, 10f), new SimPlayer(new Vec3(100f, 0, 100f)), seed: 123);
        }

        [Test]
        public void SimGameApi_TokenSpawned_FiresEvent()
        {
            TokenLocation? spawnedToken = null;
            _api.TokenSpawned += t => spawnedToken = t;

            var token = new TokenLocation
            {
                Id = "spawn_1",
                Position = new Vec3(50, 0, 50),
                Rarity = TokenRarity.Epic,
                EstimatedValue = 100f
            };

            _api.SpawnToken(token);

            Assert.That(spawnedToken, Is.Not.Null);
            Assert.That(spawnedToken!.Id, Is.EqualTo("spawn_1"));
            Assert.That(spawnedToken.Rarity, Is.EqualTo(TokenRarity.Epic));
        }

        [Test]
        public void SimGameApi_TokenCollected_FiresEventAndMarksCollected()
        {
            string? collectedId = null;
            _api.TokenCollected += id => collectedId = id;

            var token = new TokenLocation
            {
                Id = "collect_1",
                Position = new Vec3(50, 0, 50),
                IsCollected = false
            };
            _api.SpawnToken(token);

            bool success = _api.CollectToken("collect_1");

            Assert.That(success, Is.True);
            Assert.That(collectedId, Is.EqualTo("collect_1"));
        }

        [Test]
        public void SimGameApi_CollectToken_DuplicateCalls_FiresEventOnlyOnce()
        {
            int eventCount = 0;
            string? lastCollectedId = null;
            _api.TokenCollected += id =>
            {
                eventCount++;
                lastCollectedId = id;
            };

            var token = new TokenLocation
            {
                Id = "collect_idem",
                Position = new Vec3(50, 0, 50),
                IsCollected = false
            };
            _api.SpawnToken(token);

            bool firstCall = _api.CollectToken("collect_idem");
            bool secondCall = _api.CollectToken("collect_idem");
            bool thirdCall = _api.CollectToken("collect_idem");

            Assert.That(firstCall, Is.True);
            Assert.That(secondCall, Is.False);
            Assert.That(thirdCall, Is.False);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(lastCollectedId, Is.EqualTo("collect_idem"));
        }

        [Test]
        public void SimGameApi_TokenRemoved_FiresEvent()
        {
            string? removedId = null;
            _api.TokenRemoved += id => removedId = id;

            var token = new TokenLocation { Id = "rem_1", Position = new Vec3(10, 0, 10) };
            _api.SpawnToken(token);

            bool success = _api.RemoveToken("rem_1");

            Assert.That(success, Is.True);
            Assert.That(removedId, Is.EqualTo("rem_1"));
        }

        [Test]
        public void SimGameApi_RemoveToken_DuplicateCalls_FiresEventOnlyOnce()
        {
            int eventCount = 0;
            string? lastRemovedId = null;
            _api.TokenRemoved += id =>
            {
                eventCount++;
                lastRemovedId = id;
            };

            var token = new TokenLocation
            {
                Id = "remove_idem",
                Position = new Vec3(50, 0, 50)
            };
            _api.SpawnToken(token);

            bool firstCall = _api.RemoveToken("remove_idem");
            bool secondCall = _api.RemoveToken("remove_idem");
            bool thirdCall = _api.RemoveToken("remove_idem");

            Assert.That(firstCall, Is.True);
            Assert.That(secondCall, Is.False);
            Assert.That(thirdCall, Is.False);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(lastRemovedId, Is.EqualTo("remove_idem"));
        }

        [Test]
        public void SimGameApi_MapChanged_FiresEvent()
        {
            bool mapChangedFired = false;
            _api.MapChanged += () => mapChangedFired = true;

            _api.TriggerMapChanged();

            Assert.That(mapChangedFired, Is.True);
        }

        [Test]
        public void SimGameApi_GetObjects_FiltersByRegion()
        {
            _api.SpawnToken(new TokenLocation { Id = "in_region", Position = new Vec3(100, 0, 100) });
            _api.SpawnToken(new TokenLocation { Id = "out_region", Position = new Vec3(450, 0, 450) });

            var region = new Region(new Vec3(100, 0, 100), radius: 50f);
            var objects = _api.GetObjects(region).ToList();

            Assert.That(objects.Count, Is.EqualTo(1));
            Assert.That(objects[0].Id, Is.EqualTo("in_region"));
        }

        [Test]
        public void SimTokenSpawner_SpawnsPresetPhase1Tokens()
        {
            var spawner = new SimTokenSpawner();
            var presets = spawner.CreateSpecPhase1Presets();

            Assert.That(presets.Count, Is.EqualTo(3));
            Assert.That(presets[0].Position, Is.EqualTo(new Vec3(100f, 0, 250f)));
            Assert.That(presets[1].Position, Is.EqualTo(new Vec3(420f, 0, 180f)));
            Assert.That(presets[2].Position, Is.EqualTo(new Vec3(700f, 0, 500f)));
        }

        [Test]
        public void SimGridMap_FeaturesAndObstacles_AreSet()
        {
            var map = new SimGridMap(20, 20, 10f);
            map.GenerateProceduralFeatures(seed: 99);

            bool hasObstacle = false;
            bool hasRough = false;

            for (int x = 0; x < map.GridWidth; x++)
            {
                for (int z = 0; z < map.GridHeight; z++)
                {
                    if (map.GetCell(x, z) == SimCellType.Obstacle) hasObstacle = true;
                    if (map.GetCell(x, z) == SimCellType.RoughTerrain) hasRough = true;
                }
            }

            Assert.That(hasObstacle, Is.True);
            Assert.That(hasRough, Is.True);
        }

        [Test]
        public void ProceduralTokens_NeverSpawnOnObstacleCells()
        {
            var map = new SimGridMap(30, 30, 10f);
            map.GenerateProceduralFeatures(seed: 42);

            // Force cell (0, 0) to be an obstacle to ensure corner-cases don't default there
            map.SetCell(0, 0, SimCellType.Obstacle);

            var spawner = new SimTokenSpawner();
            var rng = new Random(101);
            var tokens = spawner.SpawnBatch(300, map, rng);

            foreach (var token in tokens)
            {
                var (gx, gz) = map.WorldToGrid(token.Position);
                var cellType = map.GetCell(gx, gz);

                Assert.That(cellType, Is.Not.EqualTo(SimCellType.Obstacle),
                    $"Token {token.Id} spawned at {token.Position} which maps to obstacle grid cell ({gx}, {gz}).");
                Assert.That(map.IsWalkable(token.Position), Is.True,
                    $"Token {token.Id} at {token.Position} is not walkable.");
            }
        }

        [Test]
        public void Simulator_SameSeed_ProducesDeterministicOutput()
        {
            const int seed = 777;

            // Simulator 1
            var map1 = new SimGridMap(40, 40, 10f);
            map1.GenerateProceduralFeatures(seed: seed);
            var player1 = new SimPlayer(new Vec3(50, 0, 50));
            var api1 = new SimGameApi(map1, player1, seed: seed);

            // Simulator 2
            var map2 = new SimGridMap(40, 40, 10f);
            map2.GenerateProceduralFeatures(seed: seed);
            var player2 = new SimPlayer(new Vec3(50, 0, 50));
            var api2 = new SimGameApi(map2, player2, seed: seed);

            // 1. Verify exact terrain determinism
            for (int x = 0; x < map1.GridWidth; x++)
            {
                for (int z = 0; z < map1.GridHeight; z++)
                {
                    Assert.That(map2.GetCell(x, z), Is.EqualTo(map1.GetCell(x, z)),
                        $"Cell mismatch at ({x}, {z})");
                }
            }

            // 2. Verify exact procedural token generation determinism
            for (int i = 0; i < 50; i++)
            {
                var t1 = api1.SpawnRandomToken();
                var t2 = api2.SpawnRandomToken();

                Assert.That(t2.Id, Is.EqualTo(t1.Id));
                Assert.That(t2.Position, Is.EqualTo(t1.Position));
                Assert.That(t2.Rarity, Is.EqualTo(t1.Rarity));
                Assert.That(t2.EstimatedValue, Is.EqualTo(t1.EstimatedValue));
            }
        }

        [Test]
        public void Simulator_SameSeed_AndSameClock_ProducesIdenticalTemporalAndSpatialState()
        {
            const int seed = 12345;
            var fixedTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

            var clock1 = new FakeClock(fixedTime);
            var map1 = new SimGridMap(40, 40, 10f);
            map1.GenerateProceduralFeatures(seed: seed);
            var player1 = new SimPlayer(new Vec3(50, 0, 50), speed: 5f);
            var api1 = new SimGameApi(map1, player1, seed: seed, clock: clock1);

            var clock2 = new FakeClock(fixedTime);
            var map2 = new SimGridMap(40, 40, 10f);
            map2.GenerateProceduralFeatures(seed: seed);
            var player2 = new SimPlayer(new Vec3(50, 0, 50), speed: 5f);
            var api2 = new SimGameApi(map2, player2, seed: seed, clock: clock2);

            // Compare player state
            Assert.That(api2.Player.State.Position, Is.EqualTo(api1.Player.State.Position));
            Assert.That(api2.Player.State.Speed, Is.EqualTo(api1.Player.State.Speed));

            // Compare map obstacles
            for (int x = 0; x < map1.GridWidth; x++)
            {
                for (int z = 0; z < map1.GridHeight; z++)
                {
                    Assert.That(map2.GetCell(x, z), Is.EqualTo(map1.GetCell(x, z)));
                }
            }

            // Spawn presets and compare all properties including timestamps
            var presets1 = api1.Spawner.CreateSpecPhase1Presets();
            var presets2 = api2.Spawner.CreateSpecPhase1Presets();
            Assert.That(presets2.Count, Is.EqualTo(presets1.Count));
            for (int i = 0; i < presets1.Count; i++)
            {
                Assert.That(presets2[i].Id, Is.EqualTo(presets1[i].Id));
                Assert.That(presets2[i].Position, Is.EqualTo(presets1[i].Position));
                Assert.That(presets2[i].Rarity, Is.EqualTo(presets1[i].Rarity));
                Assert.That(presets2[i].EstimatedValue, Is.EqualTo(presets1[i].EstimatedValue));
                Assert.That(presets2[i].DetectedAt, Is.EqualTo(presets1[i].DetectedAt));
                Assert.That(presets2[i].LastConfirmedAt, Is.EqualTo(presets1[i].LastConfirmedAt));
            }

            // Spawn procedural tokens and advance time deterministically
            for (int i = 0; i < 30; i++)
            {
                clock1.AdvanceSeconds(2.5);
                clock2.AdvanceSeconds(2.5);

                var t1 = api1.SpawnRandomToken();
                var t2 = api2.SpawnRandomToken();

                Assert.That(t2.Id, Is.EqualTo(t1.Id));
                Assert.That(t2.Position, Is.EqualTo(t1.Position));
                Assert.That(t2.Rarity, Is.EqualTo(t1.Rarity));
                Assert.That(t2.EstimatedValue, Is.EqualTo(t1.EstimatedValue));
                Assert.That(t2.DetectedAt, Is.EqualTo(t1.DetectedAt));
                Assert.That(t2.LastConfirmedAt, Is.EqualTo(t1.LastConfirmedAt));
            }
        }
    }
}
