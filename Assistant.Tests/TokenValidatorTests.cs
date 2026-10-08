using System;
using NUnit.Framework;
using Assistant.Core.Data;
using Assistant.Core.Discovery;

namespace Assistant.Tests
{
    [TestFixture]
    public class TokenValidatorTests
    {
        private FakeClock _clock = null!;
        private TokenValidator _validator = null!;
        private MapData _map = null!;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
            _validator = new TokenValidator(_clock, defaultTtlSeconds: 30f);
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
                LastConfirmedAt = _clock.UtcNow
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
                LastConfirmedAt = _clock.UtcNow
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
                LastConfirmedAt = _clock.UtcNow
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
                LastConfirmedAt = _clock.UtcNow
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
                LastConfirmedAt = _clock.UtcNow
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
                LastConfirmedAt = _clock.UtcNow
            };

            var result = _validator.Validate(token, _map);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.NegativeValue));
        }

        [Test]
        public void Validate_StaleToken_FailsWhenAgeExceedsTtl_UsingFakeClockAdvance()
        {
            var token = new TokenLocation
            {
                Id = "t_stale",
                Position = new Vec3(100, 0, 200),
                LastConfirmedAt = _clock.UtcNow
            };

            // Before advance, token is fresh
            Assert.That(_validator.Validate(token, _map).IsValid, Is.True);

            // Advance FakeClock past 30s TTL
            _clock.Advance(TimeSpan.FromSeconds(31));

            var result = _validator.Validate(token, _map, ttlSeconds: 30f);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.StaleToken));
        }

        [Test]
        public void Validate_FreshToken_SucceedsWithinTtl_UsingFakeClockAdvance()
        {
            var token = new TokenLocation
            {
                Id = "t_fresh",
                Position = new Vec3(100, 0, 200),
                LastConfirmedAt = _clock.UtcNow
            };

            // Advance clock by only 10s (TTL is 30s)
            _clock.Advance(TimeSpan.FromSeconds(10));

            var result = _validator.Validate(token, _map, ttlSeconds: 30f);
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validate_ExplicitCurrentTime_OverridesInjectedClock()
        {
            var token = new TokenLocation
            {
                Id = "t_explicit",
                Position = new Vec3(100, 0, 200),
                LastConfirmedAt = _clock.UtcNow
            };

            // Explicit time 40s in the future should mark it stale even if _clock is not advanced
            var futureTime = _clock.UtcNow.AddSeconds(40);
            var result = _validator.Validate(token, _map, currentTime: futureTime, ttlSeconds: 30f);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Reason, Is.EqualTo(ValidationFailureReason.StaleToken));
        }
    }
}
