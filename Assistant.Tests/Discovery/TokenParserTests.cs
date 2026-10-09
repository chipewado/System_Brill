using System.Collections.Generic;
using Assistant.Core.Data;
using Assistant.Core.Discovery;
using NUnit.Framework;

namespace Assistant.Tests.Discovery
{
    [TestFixture]
    public class TokenParserTests
    {
        private DefaultTokenParser _parser = null!;

        [SetUp]
        public void SetUp()
        {
            _parser = new DefaultTokenParser();
        }

        [Test]
        public void TryParseToken_ValidToken_SuccessfullyParsesAllProperties()
        {
            var raw = new RawMapObject
            {
                Id = "token-1",
                ObjectType = "CollectibleToken",
                Position = new Vec3(10f, 2f, 30f),
                Properties = new Dictionary<string, object>
                {
                    { "Rarity", "Legendary" },
                    { "EstimatedValue", 150.5f },
                    { "IsCollected", false }
                }
            };

            bool success = _parser.TryParseToken(raw, out var token, out string? failureReason);

            Assert.That(success, Is.True);
            Assert.That(failureReason, Is.Null);
            Assert.That(token, Is.Not.Null);
            Assert.That(token!.Id, Is.EqualTo("token-1"));
            Assert.That(token.Position, Is.EqualTo(new Vec3(10f, 2f, 30f)));
            Assert.That(token.Rarity, Is.EqualTo(TokenRarity.Legendary));
            Assert.That(token.EstimatedValue, Is.EqualTo(150.5f));
            Assert.That(token.IsCollected, Is.False);
        }

        [Test]
        public void TryParseToken_NumericRarityAndStringValue_ParsesCorrectly()
        {
            var raw = new RawMapObject
            {
                Id = "token-numeric",
                Position = new Vec3(5f, 0f, 5f),
                Properties = new Dictionary<string, object>
                {
                    { "Rarity", (int)TokenRarity.Epic },
                    { "EstimatedValue", "99.9" },
                    { "IsCollected", "true" }
                }
            };

            bool success = _parser.TryParseToken(raw, out var token, out _);

            Assert.That(success, Is.True);
            Assert.That(token!.Rarity, Is.EqualTo(TokenRarity.Epic));
            Assert.That(token.EstimatedValue, Is.EqualTo(99.9f).Within(0.001f));
            Assert.That(token.IsCollected, Is.True);
        }

        [Test]
        public void TryParseToken_NullOrWhitespaceId_FailsWithDiagnostic()
        {
            var rawNullId = new RawMapObject { Id = null!, Position = Vec3.Zero };
            var rawEmptyId = new RawMapObject { Id = "   ", Position = Vec3.Zero };

            Assert.That(_parser.TryParseToken(rawNullId, out _, out string? reason1), Is.False);
            Assert.That(reason1, Does.Contain("null or whitespace"));

            Assert.That(_parser.TryParseToken(rawEmptyId, out _, out string? reason2), Is.False);
            Assert.That(reason2, Does.Contain("null or whitespace"));
        }

        [Test]
        public void TryParseToken_NonFinitePosition_FailsValidation()
        {
            var raw = new RawMapObject
            {
                Id = "token-nan",
                Position = new Vec3(float.NaN, 0, 10f)
            };

            bool success = _parser.TryParseToken(raw, out _, out string? reason);

            Assert.That(success, Is.False);
            Assert.That(reason, Does.Contain("non-finite"));
        }

        [Test]
        public void TryParseToken_ObstacleOrDecoration_RejectedAsNonCollectible()
        {
            var rawObstacle = new RawMapObject
            {
                Id = "obs-1",
                ObjectType = "Obstacle",
                Position = new Vec3(1, 2, 3)
            };
            var rawDeco = new RawMapObject
            {
                Id = "deco-1",
                ObjectType = "Decoration",
                Position = new Vec3(1, 2, 3)
            };

            Assert.That(_parser.TryParseToken(rawObstacle, out _, out string? r1), Is.False);
            Assert.That(r1, Does.Contain("not a collectible token"));

            Assert.That(_parser.TryParseToken(rawDeco, out _, out string? r2), Is.False);
            Assert.That(r2, Does.Contain("not a collectible token"));
        }

        [Test]
        public void TryParseToken_NegativeValue_FailsValidation()
        {
            var raw = new RawMapObject
            {
                Id = "token-neg",
                Position = Vec3.Zero,
                Properties = new Dictionary<string, object>
                {
                    { "EstimatedValue", -10f }
                }
            };

            bool success = _parser.TryParseToken(raw, out _, out string? reason);

            Assert.That(success, Is.False);
            Assert.That(reason, Does.Contain("negative"));
        }

        [Test]
        public void ParseMany_DuplicateIdsInSameBatch_FirstOccurrenceWins()
        {
            var rawObjects = new List<RawMapObject>
            {
                new RawMapObject
                {
                    Id = "dup-1",
                    Position = new Vec3(10, 0, 10),
                    Properties = new Dictionary<string, object> { { "EstimatedValue", 100f } }
                },
                new RawMapObject
                {
                    Id = "dup-1", // duplicate with different value
                    Position = new Vec3(20, 0, 20),
                    Properties = new Dictionary<string, object> { { "EstimatedValue", 200f } }
                },
                new RawMapObject
                {
                    Id = "unique-2",
                    Position = new Vec3(30, 0, 30),
                    Properties = new Dictionary<string, object> { { "EstimatedValue", 50f } }
                }
            };

            var parsed = _parser.ParseMany(rawObjects);

            Assert.That(parsed.Count, Is.EqualTo(2));
            Assert.That(parsed[0].Id, Is.EqualTo("dup-1"));
            Assert.That(parsed[0].EstimatedValue, Is.EqualTo(100f)); // first occurrence value preserved
            Assert.That(parsed[1].Id, Is.EqualTo("unique-2"));
        }
    }
}
