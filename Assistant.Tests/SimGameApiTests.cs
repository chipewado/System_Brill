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
    }
}
