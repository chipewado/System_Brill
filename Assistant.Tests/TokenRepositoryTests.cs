using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Assistant.Core.Data;
using Assistant.Core.Discovery;

namespace Assistant.Tests
{
    [TestFixture]
    public class TokenRepositoryTests
    {
        private TokenRepository _repo = null!;

        [SetUp]
        public void SetUp()
        {
            _repo = new TokenRepository(spatialCellSize: 50f);
        }

        [Test]
        public void AddAndGet_ShouldStoreAndRetrieveToken()
        {
            var token = new TokenLocation
            {
                Id = "t1",
                Position = new Vec3(100f, 0, 200f),
                Rarity = TokenRarity.Rare,
                EstimatedValue = 50f
            };

            _repo.Add(token);
            var retrieved = _repo.Get("t1");

            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.Id, Is.EqualTo("t1"));
            Assert.That(retrieved.Position, Is.EqualTo(new Vec3(100f, 0, 200f)));
            Assert.That(retrieved.Rarity, Is.EqualTo(TokenRarity.Rare));
            Assert.That(retrieved.EstimatedValue, Is.EqualTo(50f));
            Assert.That(_repo.TotalCount, Is.EqualTo(1));
        }

        [Test]
        public void Add_DuplicateId_ThrowsArgumentException()
        {
            _repo.Add(new TokenLocation { Id = "t1", Position = new Vec3(10, 0, 10) });
            Assert.Throws<ArgumentException>(() =>
            {
                _repo.Add(new TokenLocation { Id = "t1", Position = new Vec3(20, 0, 20) });
            });
        }

        [Test]
        public void Update_ShouldModifyTokenProperties()
        {
            var token = new TokenLocation
            {
                Id = "t1",
                Position = new Vec3(10f, 0, 10f),
                Rarity = TokenRarity.Common,
                EstimatedValue = 10f
            };
            _repo.Add(token);

            token.Position = new Vec3(80f, 0, 90f);
            token.EstimatedValue = 25f;
            _repo.Update(token);

            var updated = _repo.Get("t1");
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.Position, Is.EqualTo(new Vec3(80f, 0, 90f)));
            Assert.That(updated.EstimatedValue, Is.EqualTo(25f));
        }

        [Test]
        public void Update_NonExistentToken_ThrowsKeyNotFoundException()
        {
            var missing = new TokenLocation { Id = "nonexistent", Position = new Vec3(10, 0, 10) };
            Assert.Throws<KeyNotFoundException>(() =>
            {
                _repo.Update(missing);
            });
        }

        [Test]
        public void Update_MovesTokenInSpatialIndex()
        {
            _repo.Add(new TokenLocation { Id = "t1", Position = new Vec3(10, 0, 10) });
            _repo.Add(new TokenLocation { Id = "far", Position = new Vec3(5000, 0, 5000) });
            var t = _repo.Get("t1")!;
            t.Position = new Vec3(3000, 0, 3000);
            _repo.Update(t);
            var near = _repo.GetNearest(new Vec3(3000, 0, 3000), 1);
            Assert.That(near.Count, Is.EqualTo(1));
            Assert.That(near[0].Id, Is.EqualTo("t1"));
        }

        [Test]
        public void Remove_ShouldDeleteToken()
        {
            var token = new TokenLocation { Id = "t1", Position = new Vec3(10f, 0, 10f) };
            _repo.Add(token);

            bool removed = _repo.Remove("t1");
            Assert.That(removed, Is.True);
            Assert.That(_repo.Get("t1"), Is.Null);
            Assert.That(_repo.TotalCount, Is.EqualTo(0));
        }

        [Test]
        public void Remove_NonExistent_ReturnsFalse()
        {
            Assert.That(_repo.Remove("nope"), Is.False);
        }

        [Test]
        public void ReturnedTokens_AreDefensiveCopies()
        {
            _repo.Add(new TokenLocation { Id = "t1", Position = new Vec3(1, 0, 1), EstimatedValue = 5 });
            _repo.Get("t1")!.EstimatedValue = 999;
            Assert.That(_repo.Get("t1")!.EstimatedValue, Is.EqualTo(5));
        }

        [Test]
        public void GetAvailable_ShouldExcludeCollectedTokens()
        {
            _repo.Add(new TokenLocation { Id = "t1", Position = new Vec3(10, 0, 10), IsCollected = false });
            _repo.Add(new TokenLocation { Id = "t2", Position = new Vec3(20, 0, 20), IsCollected = true });
            _repo.Add(new TokenLocation { Id = "t3", Position = new Vec3(30, 0, 30), IsCollected = false });

            var available = _repo.GetAvailable();

            Assert.That(available.Count, Is.EqualTo(2));
            Assert.That(available.Any(t => t.Id == "t1"), Is.True);
            Assert.That(available.Any(t => t.Id == "t3"), Is.True);
            Assert.That(available.Any(t => t.Id == "t2"), Is.False);
        }

        [Test]
        public void GetByRarity_ShouldReturnOnlySpecifiedRarity()
        {
            _repo.Add(new TokenLocation { Id = "t1", Position = new Vec3(10, 0, 10), Rarity = TokenRarity.Common });
            _repo.Add(new TokenLocation { Id = "t2", Position = new Vec3(20, 0, 20), Rarity = TokenRarity.Legendary });
            _repo.Add(new TokenLocation { Id = "t3", Position = new Vec3(30, 0, 30), Rarity = TokenRarity.Legendary });

            var legendaries = _repo.GetByRarity(TokenRarity.Legendary);

            Assert.That(legendaries.Count, Is.EqualTo(2));
            Assert.That(legendaries.All(t => t.Rarity == TokenRarity.Legendary), Is.True);
        }

        [Test]
        public void GetNearest_ShouldReturnTokensSortedByDistance()
        {
            // Player at (0, 0, 0)
            var origin = new Vec3(0, 0, 0);

            // Tokens at distances: t1=100, t2=20, t3=50, t4=10
            _repo.Add(new TokenLocation { Id = "t1", Position = new Vec3(100, 0, 0) });
            _repo.Add(new TokenLocation { Id = "t2", Position = new Vec3(20, 0, 0) });
            _repo.Add(new TokenLocation { Id = "t3", Position = new Vec3(50, 0, 0) });
            _repo.Add(new TokenLocation { Id = "t4", Position = new Vec3(10, 0, 0) });

            var nearest = _repo.GetNearest(origin, count: 3);

            Assert.That(nearest.Count, Is.EqualTo(3));
            Assert.That(nearest[0].Id, Is.EqualTo("t4")); // dist = 10
            Assert.That(nearest[1].Id, Is.EqualTo("t2")); // dist = 20
            Assert.That(nearest[2].Id, Is.EqualTo("t3")); // dist = 50
        }

        [Test]
        public void GetNearest_MatchesBruteForce_OnRandomData()
        {
            var rng = new Random(777);
            var all = new List<(string id, float x, float z)>();
            for (int i = 0; i < 1500; i++)
            {
                float x = (float)(rng.NextDouble() * 6000 - 3000), z = (float)(rng.NextDouble() * 6000 - 3000);
                _repo.Add(new TokenLocation { Id = "t" + i, Position = new Vec3(x, 0, z) });
                all.Add(("t" + i, x, z));
            }

            for (int q = 0; q < 100; q++)
            {
                float cx = (float)(rng.NextDouble() * 6000 - 3000), cz = (float)(rng.NextDouble() * 6000 - 3000);
                int k = 1 + rng.Next(20);
                var expected = all.OrderBy(a => (a.x - cx) * (a.x - cx) + (a.z - cz) * (a.z - cz))
                                  .Take(k).Select(a => a.id).ToList();
                var actual = _repo.GetNearest(new Vec3(cx, 0, cz), k).Select(t => t.Id).ToList();
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        [Test]
        public void GetNearest_EdgeCases()
        {
            Assert.That(_repo.GetNearest(new Vec3(0, 0, 0), 5), Is.Empty); // repo empty
            _repo.Add(new TokenLocation { Id = "a", Position = new Vec3(1, 0, 1) });
            Assert.That(_repo.GetNearest(new Vec3(0, 0, 0), 0), Is.Empty); // count = 0
            Assert.That(_repo.GetNearest(new Vec3(0, 0, 0), 99).Count, Is.EqualTo(1)); // count > total
        }

        [Test]
        public void ClearCollected_ShouldRemoveAllCollectedTokens()
        {
            _repo.Add(new TokenLocation { Id = "t1", Position = new Vec3(10, 0, 10), IsCollected = false });
            _repo.Add(new TokenLocation { Id = "t2", Position = new Vec3(20, 0, 20), IsCollected = true });
            _repo.Add(new TokenLocation { Id = "t3", Position = new Vec3(30, 0, 30), IsCollected = true });

            _repo.ClearCollected();

            Assert.That(_repo.TotalCount, Is.EqualTo(1));
            Assert.That(_repo.Get("t1"), Is.Not.Null);
            Assert.That(_repo.Get("t2"), Is.Null);
            Assert.That(_repo.Get("t3"), Is.Null);
        }
    }
}
