using System;
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
