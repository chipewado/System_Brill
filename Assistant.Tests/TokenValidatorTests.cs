using System;
using NUnit.Framework;
using Assistant.Core.Data;
using Assistant.Core.Discovery;

namespace Assistant.Tests
{
    [TestFixture]
    public class TokenValidatorTests
    {
        private TokenValidator _validator = null!;
        private MapData _map = null!;

        [SetUp]
        public void SetUp()
        {
            _validator = new TokenValidator(defaultTtlSeconds: 30f);
            _map = new MapData("Map1", new MapBounds(new Vec3(0, 0, 0), new Vec3(1000, 0, 1000)));
        }

        [Test]
        public void Validate_ValidToken_ReturnsSuccess()
        {
            var token = new TokenLocation
            {
                Id = "t_valid",
                Position = new Vec3(100, 0, 200),
                EstimatedValue = 50f,
                IsCollected = false,
                LastConfirmedAt = DateTime.UtcNow
            };

            var result = _validator.Validate(token, _map);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.None));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Validate_InvalidId_Fails(string? id)
        {
            var token = new TokenLocation
            {
                Id = id!,
                Position = new Vec3(100, 0, 200),
                LastConfirmedAt = DateTime.UtcNow
            };

            var result = _validator.Validate(token, _map);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.InvalidId));
        }

        [Test]
        public void Validate_NonFinitePosition_Fails()
        {
            var token = new TokenLocation
            {
                Id = "t_nan",
                Position = new Vec3(float.NaN, 0, 100),
                LastConfirmedAt = DateTime.UtcNow
            };

            var result = _validator.Validate(token, _map);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.InvalidPosition));
        }

        [Test]
        public void Validate_OutOfBoundsPosition_Fails()
        {
            var token = new TokenLocation
            {
                Id = "t_oob",
                Position = new Vec3(1500, 0, 200), // Map max is 1000
                LastConfirmedAt = DateTime.UtcNow
            };

            var result = _validator.Validate(token, _map);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.OutOfBounds));
        }

        [Test]
        public void Validate_AlreadyCollected_Fails()
        {
            var token = new TokenLocation
            {
                Id = "t_collected",
                Position = new Vec3(100, 0, 200),
                IsCollected = true,
                LastConfirmedAt = DateTime.UtcNow
            };

            var result = _validator.Validate(token, _map);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.AlreadyCollected));
        }

        [Test]
        public void Validate_NegativeValue_Fails()
        {
            var token = new TokenLocation
            {
                Id = "t_neg",
                Position = new Vec3(100, 0, 200),
                EstimatedValue = -5f,
                LastConfirmedAt = DateTime.UtcNow
            };

            var result = _validator.Validate(token, _map);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.NegativeValue));
        }

        [Test]
        public void Validate_StaleToken_FailsWhenAgeExceedsTtl()
        {
            var fixedNow = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            var token = new TokenLocation
            {
                Id = "t_stale",
                Position = new Vec3(100, 0, 200),
                LastConfirmedAt = fixedNow.AddSeconds(-45) // 45s ago, TTL is 30s
            };

            var result = _validator.Validate(token, _map, currentTime: fixedNow, ttlSeconds: 30f);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.StaleToken));
        }

        [Test]
        public void Validate_FreshToken_SucceedsWithinTtl()
        {
            var fixedNow = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            var token = new TokenLocation
            {
                Id = "t_fresh",
                Position = new Vec3(100, 0, 200),
                LastConfirmedAt = fixedNow.AddSeconds(-10) // 10s ago, TTL is 30s
            };

            var result = _validator.Validate(token, _map, currentTime: fixedNow, ttlSeconds: 30f);
            Assert.That(result.IsValid, Is.True);
        }
    }
}
