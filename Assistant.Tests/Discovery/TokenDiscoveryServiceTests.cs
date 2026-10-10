using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;
using Assistant.Core.Discovery;
using Assistant.Sim;
using NUnit.Framework;

namespace Assistant.Tests.Discovery
{
    [TestFixture]
    public class TokenDiscoveryServiceTests
    {
        private FakeClock _clock = null!;
        private TokenRepository _repository = null!;
        private DefaultTokenParser _parser = null!;
        private TokenValidator _validator = null!;
        private SimGameApi _gameApi = null!;
        private TokenDiscoveryService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            _repository = new TokenRepository(50f);
            _parser = new DefaultTokenParser();
            _validator = new TokenValidator(_clock, 30f);
            _gameApi = new SimGameApi(clock: _clock);
            _service = new TokenDiscoveryService(
                mapProvider: _gameApi,
                repository: _repository,
                parser: _parser,
                validator: _validator,
                clock: _clock,
                gameApi: _gameApi,
                ttlSeconds: 30f,
                scanRadius: 100f);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        #region Contract Tests (Section 6.1)

        [Test]
        public void ScanRegion_ValidRawObjects_SuccessfullyUpsertsToRepository()
        {
            var token = new TokenLocation
            {
                Id = "t1",
                Position = new Vec3(10f, 0, 10f),
                Rarity = TokenRarity.Common,
                EstimatedValue = 25f
            };
            _gameApi.SpawnToken(token);

            int scanned = _service.ScanRegion(new Region(Vec3.Zero, 50f));

            Assert.That(scanned, Is.EqualTo(1));
            var stored = _repository.Get("t1");
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Id, Is.EqualTo("t1"));
            Assert.That(stored.DetectedAt, Is.EqualTo(_clock.UtcNow));
            Assert.That(stored.LastConfirmedAt, Is.EqualTo(_clock.UtcNow));
        }

        [Test]
        public void ScanRegion_MalformedProperties_SkipsInvalidWithoutThrowing()
        {
            // Spawn one valid token in gameApi
            _gameApi.SpawnToken(new TokenLocation { Id = "valid-1", Position = new Vec3(10, 0, 10), EstimatedValue = 10 });

            // Create a custom map provider that yields both valid and malformed raw objects
            var customProvider = new MockMapProvider(new[]
            {
                new RawMapObject { Id = "invalid-nan", Position = new Vec3(float.NaN, 0, 0) },
                new RawMapObject { Id = "obs", ObjectType = "Obstacle", Position = new Vec3(5, 0, 5) },
                new RawMapObject { Id = "valid-2", Position = new Vec3(20, 0, 20), Properties = new Dictionary<string, object> { { "EstimatedValue", 15f } } }
            });

            var service = new TokenDiscoveryService(customProvider, _repository, _parser, _validator, _clock, ttlSeconds: 30f);

            Assert.DoesNotThrow(() => service.ScanRegion(new Region(Vec3.Zero, 100f)));
            Assert.That(_repository.Get("valid-2"), Is.Not.Null);
            Assert.That(_repository.Get("invalid-nan"), Is.Null);
            Assert.That(_repository.Get("obs"), Is.Null);
        }

        [Test]
        public void ScanRegion_DuplicateIdsInSameScan_FirstOccurrenceWinsAndSubsequentRejected()
        {
            var customProvider = new MockMapProvider(new[]
            {
                new RawMapObject
                {
                    Id = "dup-token",
                    Position = new Vec3(10, 0, 10),
                    Properties = new Dictionary<string, object> { { "EstimatedValue", 50f } }
                },
                new RawMapObject
                {
                    Id = "dup-token",
                    Position = new Vec3(20, 0, 20),
                    Properties = new Dictionary<string, object> { { "EstimatedValue", 999f } }
                }
            });

            var service = new TokenDiscoveryService(customProvider, _repository, _parser, _validator, _clock);
            service.ScanRegion(new Region(Vec3.Zero, 100f));

            var stored = _repository.Get("dup-token");
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Position, Is.EqualTo(new Vec3(10, 0, 10)));
            Assert.That(stored.EstimatedValue, Is.EqualTo(50f));
        }

        [Test]
        public void ScanRegion_NewToken_FiresTokensDiscovered()
        {
            _gameApi.SpawnToken(new TokenLocation { Id = "t-new", Position = new Vec3(5, 0, 5), EstimatedValue = 10 });

            IReadOnlyList<TokenLocation>? discovered = null;
            _service.TokensDiscovered += list => discovered = list;

            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.Count, Is.EqualTo(1));
            Assert.That(discovered[0].Id, Is.EqualTo("t-new"));
        }

        [Test]
        public void ScanRegion_UnchangedToken_UpdatesLastConfirmedAtWithoutFiringTokensDiscovered()
        {
            _gameApi.SpawnToken(new TokenLocation { Id = "t-unchanged", Position = new Vec3(5, 0, 5), EstimatedValue = 10 });

            // First scan discovers the token
            _service.ScanRegion(new Region(Vec3.Zero, 50f));
            var initialConfirmedAt = _repository.Get("t-unchanged")!.LastConfirmedAt;

            // Advance clock by 5 seconds
            _clock.AdvanceSeconds(5);

            bool eventFired = false;
            _service.TokensDiscovered += _ => eventFired = true;

            // Second scan confirms existing token without property changes
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            Assert.That(eventFired, Is.False, "TokensDiscovered must NOT fire for timestamp-only confirmations");
            var updated = _repository.Get("t-unchanged")!;
            Assert.That(updated.LastConfirmedAt, Is.EqualTo(_clock.UtcNow));
            Assert.That(updated.LastConfirmedAt, Is.GreaterThan(initialConfirmedAt));
        }

        [Test]
        public void ScanRegion_MutatedToken_UpdatesAndFiresTokensDiscovered()
        {
            _gameApi.SpawnToken(new TokenLocation { Id = "t-mut", Position = new Vec3(5, 0, 5), EstimatedValue = 10 });
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            // Move token beyond epsilon (> 0.001f)
            _gameApi.SpawnToken(new TokenLocation { Id = "t-mut", Position = new Vec3(8, 0, 8), EstimatedValue = 10 });

            IReadOnlyList<TokenLocation>? discovered = null;
            _service.TokensDiscovered += list => discovered = list;

            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.Count, Is.EqualTo(1));
            Assert.That(discovered[0].Id, Is.EqualTo("t-mut"));
            Assert.That(discovered[0].Position, Is.EqualTo(new Vec3(8, 0, 8)));
        }

        [Test]
        public void PruneStaleTokens_TokenOutsideCoverage_SurvivesPastTtl()
        {
            // Initial scan covers (0, 0)
            _gameApi.SpawnToken(new TokenLocation { Id = "t-remote", Position = new Vec3(500, 0, 500), EstimatedValue = 10 });

            // Add manually to repository (simulating past detection)
            var token = new TokenLocation
            {
                Id = "t-remote",
                Position = new Vec3(500, 0, 500),
                EstimatedValue = 10,
                DetectedAt = _clock.UtcNow,
                LastConfirmedAt = _clock.UtcNow
            };
            _repository.Add(token);

            // Advance clock way past TTL (100 seconds > 30s TTL)
            _clock.AdvanceSeconds(100);

            // Scan only around (0, 0), which does NOT cover (500, 500)
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            int pruned = _service.PruneStaleTokens();

            Assert.That(pruned, Is.EqualTo(0));
            Assert.That(_repository.Get("t-remote"), Is.Not.Null, "Tokens outside scan coverage must survive past TTL");
        }

        [Test]
        public void PruneStaleTokens_TokenInsideCoverage_ExpiresExactlyAtTtlThreshold()
        {
            // Initial token confirmed at T=0
            var token = new TokenLocation
            {
                Id = "t-cover",
                Position = new Vec3(10, 0, 10),
                EstimatedValue = 20,
                DetectedAt = _clock.UtcNow,
                LastConfirmedAt = _clock.UtcNow
            };
            _repository.Add(token);

            // Re-scan region at T = 1s without this token (token removed from world)
            _clock.AdvanceSeconds(1);
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            // Instant before threshold: T = 29.999s (elapsed = 29.999s < 30s)
            _clock.SetTime(token.LastConfirmedAt.AddSeconds(29.999));
            int prunedBefore = _service.PruneStaleTokens();
            Assert.That(prunedBefore, Is.EqualTo(0));
            Assert.That(_repository.Get("t-cover"), Is.Not.Null, "Must NOT expire before TTL threshold");

            // Exactly at threshold: T = 30.000s (elapsed = 30.000s >= 30s)
            _clock.SetTime(token.LastConfirmedAt.AddSeconds(30.000));
            IReadOnlyList<string>? expired = null;
            _service.TokensExpired += list => expired = list;

            int prunedExact = _service.PruneStaleTokens();
            Assert.That(prunedExact, Is.EqualTo(1));
            Assert.That(_repository.Get("t-cover"), Is.Null, "Must expire exactly at TTL threshold");
            Assert.That(expired, Is.Not.Null);
            Assert.That(expired!, Contains.Item("t-cover"));
        }

        [Test]
        public void HandleMapChanged_WithAffectedRegion_PurgesOnlyTokensInsideRegion()
        {
            _repository.Add(new TokenLocation { Id = "inside", Position = new Vec3(10, 0, 10), EstimatedValue = 5 });
            _repository.Add(new TokenLocation { Id = "outside", Position = new Vec3(200, 0, 200), EstimatedValue = 5 });

            var affectedRegion = new Region(new Vec3(10, 0, 10), 20f);
            _service.HandleMapChanged(MapChangedEventArgs.SubRegion(affectedRegion, "Cave Collapse", _clock.UtcNow));

            Assert.That(_repository.Get("inside"), Is.Null);
            Assert.That(_repository.Get("outside"), Is.Not.Null);
        }

        [Test]
        public void HandleMapChanged_EntireMap_ClearsAllRepositoryTokens()
        {
            _repository.Add(new TokenLocation { Id = "t1", Position = new Vec3(10, 0, 10), EstimatedValue = 5 });
            _repository.Add(new TokenLocation { Id = "t2", Position = new Vec3(200, 0, 200), EstimatedValue = 5 });

            _service.HandleMapChanged(MapChangedEventArgs.EntireMap("Server Reset", _clock.UtcNow));

            Assert.That(_repository.TotalCount, Is.EqualTo(0));
            Assert.That(_repository.Get("t1"), Is.Null);
            Assert.That(_repository.Get("t2"), Is.Null);
        }

        [Test]
        public void HandleMapChanged_MultipleEventsSameTick_MergesRegionsWithoutDropping()
        {
            _repository.Add(new TokenLocation { Id = "zoneA", Position = new Vec3(10, 0, 10), EstimatedValue = 5 });
            _repository.Add(new TokenLocation { Id = "zoneB", Position = new Vec3(100, 0, 100), EstimatedValue = 5 });
            _repository.Add(new TokenLocation { Id = "zoneSafe", Position = new Vec3(300, 0, 300), EstimatedValue = 5 });

            var now = _clock.UtcNow;
            // Two events at the exact same tick with different affected regions
            _gameApi.TriggerMapChanged(new Region(new Vec3(10, 0, 10), 20f), "Quake A");
            _gameApi.TriggerMapChanged(new Region(new Vec3(100, 0, 100), 20f), "Quake B");

            Assert.That(_repository.Get("zoneA"), Is.Null, "Zone A token must be purged");
            Assert.That(_repository.Get("zoneB"), Is.Null, "Zone B token must be purged");
            Assert.That(_repository.Get("zoneSafe"), Is.Not.Null, "Safe zone token must remain intact");
        }

        [Test]
        public void TokenCollected_RemovesFromSpatialGrid_CannotBeQueriedAsAvailable()
        {
            _repository.Add(new TokenLocation { Id = "t-collected", Position = new Vec3(10, 0, 10), EstimatedValue = 10 });
            _gameApi.SpawnToken(new TokenLocation { Id = "t-collected", Position = new Vec3(10, 0, 10), EstimatedValue = 10 });

            // Simulate TokenCollected event from game
            _gameApi.CollectToken("t-collected");

            var available = _repository.GetAvailable();
            Assert.That(available.Any(t => t.Id == "t-collected"), Is.False);
            var stored = _repository.Get("t-collected");
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.IsCollected, Is.True);
        }

        [Test]
        public void SubscriberException_DoesNotCorruptDiscoveryPipelineOrAbortOtherSubscribers()
        {
            _gameApi.SpawnToken(new TokenLocation { Id = "t-safe", Position = new Vec3(5, 0, 5), EstimatedValue = 10 });

            bool secondSubscriberInvoked = false;

            _service.TokensDiscovered += _ => throw new InvalidOperationException("Subscriber 1 boom!");
            _service.TokensDiscovered += list =>
            {
                if (list.Any(t => t.Id == "t-safe"))
                    secondSubscriberInvoked = true;
            };

            Assert.DoesNotThrow(() => _service.ScanRegion(new Region(Vec3.Zero, 50f)));
            Assert.That(secondSubscriberInvoked, Is.True, "Second subscriber must execute despite first subscriber exception");
        }

        #endregion

        #region Additional Requirement Tests

        [Test]
        public void TokenRemoved_RemovesTokenFromRepositoryImmediately()
        {
            _repository.Add(new TokenLocation { Id = "t-removed", Position = new Vec3(10, 0, 10), EstimatedValue = 10 });
            _gameApi.SpawnToken(new TokenLocation { Id = "t-removed", Position = new Vec3(10, 0, 10), EstimatedValue = 10 });

            // Trigger token removal
            _gameApi.RemoveToken("t-removed");

            Assert.That(_repository.Get("t-removed"), Is.Null, "TokenRemoved must immediately evict token from repository");
        }

        [Test]
        public void SharedFakeClock_TokenDiscoveryServiceAndValidatorUseSameClock()
        {
            Assert.That(_service.Clock, Is.SameAs(_clock));

            // Token confirmed at T=0
            var token = new TokenLocation
            {
                Id = "t-shared",
                Position = new Vec3(10, 0, 10),
                EstimatedValue = 10,
                DetectedAt = _clock.UtcNow,
                LastConfirmedAt = _clock.UtcNow
            };
            _repository.Add(token);

            // Advance clock by 35 seconds
            _clock.AdvanceSeconds(35);

            // Both validator and discovery service observe the exact same current time
            var valResult = _validator.Validate(token, null);
            Assert.That(valResult.Reason, Is.EqualTo(ValidationFailureReason.StaleToken));
            Assert.That(_service.Clock.UtcNow, Is.EqualTo(_clock.UtcNow));
        }

        [Test]
        public void CoverageHistory_PrunesEntriesOlderThanOldestLastConfirmedAt()
        {
            // Scan 1 at T=0 covers region around (0, 0)
            _service.ScanRegion(new Region(new Vec3(0, 0, 0), 10f));

            // Advance clock by 20s and add token with confirmedAt = T+20s
            _clock.AdvanceSeconds(20);
            var token = new TokenLocation
            {
                Id = "t-active",
                Position = new Vec3(50, 0, 50),
                EstimatedValue = 10,
                DetectedAt = _clock.UtcNow,
                LastConfirmedAt = _clock.UtcNow
            };
            _repository.Add(token);

            // Scan 2 at T=25s covers disjoint region around (100, 100)
            _clock.AdvanceSeconds(5);
            _service.ScanRegion(new Region(new Vec3(100, 0, 100), 10f));

            // Pruning stale tokens triggers bounded coverage cleanup:
            // Scan 1 at T=0 is strictly older than oldest confirmed token (T=20s), so it is pruned.
            _service.PruneStaleTokens();

            // Advance time past TTL (T=55s, age = 35s > 30s TTL).
            // Scan 1 was pruned, and Scan 2 does not cover (50, 50), so t-active survives!
            _clock.AdvanceSeconds(30);
            int pruned = _service.PruneStaleTokens();
            Assert.That(pruned, Is.EqualTo(0), "Token must survive because covering scan at T=0 was pruned and Scan 2 is disjoint");
            Assert.That(_repository.Get("t-active"), Is.Not.Null);
        }

        [Test]
        public void HandleMapChanged_DropsCoverageEntriesIntersectingAffectedRegion()
        {
            var region1 = new Region(new Vec3(10, 0, 10), 15f);
            _service.ScanRegion(region1);

            // Invalidate region overlapping region1
            var invalidRegion = new Region(new Vec3(15, 0, 15), 10f);
            _service.HandleMapChanged(MapChangedEventArgs.SubRegion(invalidRegion, "Landslide", _clock.UtcNow));

            // Add a token inside region1
            var token = new TokenLocation
            {
                Id = "t-landslide",
                Position = new Vec3(10, 0, 10),
                EstimatedValue = 10,
                DetectedAt = _clock.UtcNow.AddSeconds(-40),
                LastConfirmedAt = _clock.UtcNow.AddSeconds(-40)
            };
            _repository.Add(token);

            // Because coverage history was dropped for region1, the token does NOT expire
            int pruned = _service.PruneStaleTokens();
            Assert.That(pruned, Is.EqualTo(0), "Dropped coverage entries must not trigger TTL eviction");
        }

        [Test]
        public void PruneStaleTokens_ScanBeforeOrAtLastConfirmedAt_DoesNotExpireToken()
        {
            // Scan at T=0
            _service.ScanRegion(new Region(new Vec3(10, 0, 10), 20f));

            // Token confirmed at T=5s (after the scan)
            _clock.AdvanceSeconds(5);
            var token = new TokenLocation
            {
                Id = "t-after",
                Position = new Vec3(10, 0, 10),
                EstimatedValue = 10,
                DetectedAt = _clock.UtcNow,
                LastConfirmedAt = _clock.UtcNow
            };
            _repository.Add(token);

            // Advance past TTL (T = 5 + 35 = 40s) without any new scans in this region
            _clock.AdvanceSeconds(35);

            int pruned = _service.PruneStaleTokens();
            Assert.That(pruned, Is.EqualTo(0), "Scan prior to LastConfirmedAt cannot prove token absence");
            Assert.That(_repository.Get("t-after"), Is.Not.Null);
        }

        [Test]
        public void PruneStaleTokens_InstantAfterThreshold_ExpiresToken()
        {
            var token = new TokenLocation
            {
                Id = "t-after-thresh",
                Position = new Vec3(10, 0, 10),
                EstimatedValue = 20,
                DetectedAt = _clock.UtcNow,
                LastConfirmedAt = _clock.UtcNow
            };
            _repository.Add(token);

            // Scan after confirmation
            _clock.AdvanceSeconds(1);
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            // Instant after threshold: T = 30.001s
            _clock.SetTime(token.LastConfirmedAt.AddSeconds(30.001));

            int pruned = _service.PruneStaleTokens();
            Assert.That(pruned, Is.EqualTo(1));
            Assert.That(_repository.Get("t-after-thresh"), Is.Null);
        }

        [Test]
        public void PositionChanged_EpsilonThreshold_DifferentiatesSubtleVsRealMovement()
        {
            var initial = new TokenLocation { Id = "t-eps", Position = new Vec3(10f, 0, 10f), EstimatedValue = 10 };
            _gameApi.SpawnToken(initial);
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            // Move token by 0.0005f (< 0.001f epsilon) -> Considered unchanged
            _gameApi.SpawnToken(new TokenLocation { Id = "t-eps", Position = new Vec3(10.0005f, 0, 10f), EstimatedValue = 10 });

            bool eventFired = false;
            _service.TokensDiscovered += _ => eventFired = true;

            _service.ScanRegion(new Region(Vec3.Zero, 50f));
            Assert.That(eventFired, Is.False, "Movement below epsilon must not fire TokensDiscovered");

            // Move token by 0.005f (> 0.001f epsilon) -> Considered moved
            _gameApi.SpawnToken(new TokenLocation { Id = "t-eps", Position = new Vec3(10.005f, 0, 10f), EstimatedValue = 10 });

            _service.ScanRegion(new Region(Vec3.Zero, 50f));
            Assert.That(eventFired, Is.True, "Movement above epsilon must fire TokensDiscovered");
        }

        #endregion

        #region Step 1 - Phase 2A Hardening Tests

        [Test]
        public void Tick_RespectsScanInterval_NoScanBeforeInterval_ScansAtOrAfterInterval()
        {
            _service.ScanInterval = TimeSpan.FromSeconds(2.0);

            // Spawn first token
            _gameApi.SpawnToken(new TokenLocation { Id = "t-tick-1", Position = Vec3.Zero, EstimatedValue = 10 });

            // First Tick() should run scan immediately
            _service.Tick();
            Assert.That(_repository.Get("t-tick-1"), Is.Not.Null, "First Tick must scan immediately");

            // Spawn second token
            _gameApi.SpawnToken(new TokenLocation { Id = "t-tick-2", Position = Vec3.Zero, EstimatedValue = 20 });

            // Advance clock by 1.0s (< 2.0s interval)
            _clock.AdvanceSeconds(1.0);
            _service.Tick();
            Assert.That(_repository.Get("t-tick-2"), Is.Null, "Tick before ScanInterval elapsed must not trigger scan");

            // Advance clock by another 1.0s (total 2.0s >= interval)
            _clock.AdvanceSeconds(1.0);
            _service.Tick();
            Assert.That(_repository.Get("t-tick-2"), Is.Not.Null, "Tick at or after ScanInterval must trigger scan");
        }

        [Test]
        public void Tick_ScansPlayerPositionWithScanRadius_DiscoversOnlyWithinRadius()
        {
            _service.ScanRadius = 50f;
            _gameApi.Player.Teleport(new Vec3(100f, 0, 100f));

            // Within radius: distance 20 <= 50
            _gameApi.SpawnToken(new TokenLocation { Id = "t-near", Position = new Vec3(120f, 0, 100f), EstimatedValue = 10 });
            // Outside radius: distance 100 > 50
            _gameApi.SpawnToken(new TokenLocation { Id = "t-far", Position = new Vec3(200f, 0, 100f), EstimatedValue = 10 });

            _service.Tick();

            Assert.That(_repository.Get("t-near"), Is.Not.Null);
            Assert.That(_repository.Get("t-far"), Is.Null);
        }

        [Test]
        public void ScanAll_CoversFullMapBounds_DiscoversTokensAcrossEntireMap()
        {
            _gameApi.SpawnToken(new TokenLocation { Id = "t-nw", Position = new Vec3(100, 0, 100), EstimatedValue = 10 });
            _gameApi.SpawnToken(new TokenLocation { Id = "t-se", Position = new Vec3(900, 0, 900), EstimatedValue = 15 });

            int scanned = _service.ScanAll();

            Assert.That(scanned, Is.EqualTo(2));
            Assert.That(_repository.Get("t-nw"), Is.Not.Null);
            Assert.That(_repository.Get("t-se"), Is.Not.Null);
        }

        [Test]
        public void ScanRegion_CollectedToken_IsNotResurrectedByLaterScan()
        {
            _gameApi.SpawnToken(new TokenLocation { Id = "t-collected", Position = new Vec3(10, 0, 10), EstimatedValue = 10 });
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            // Mark as collected
            _gameApi.CollectToken("t-collected");
            var stored = _repository.Get("t-collected");
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.IsCollected, Is.True);

            // Subsequent scan finds the raw object in map
            bool discoveredFired = false;
            _service.TokensDiscovered += _ => discoveredFired = true;

            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            Assert.That(discoveredFired, Is.False, "Collected token must not fire TokensDiscovered on subsequent scans");
            var afterScan = _repository.Get("t-collected");
            Assert.That(afterScan!.IsCollected, Is.True, "Collected token must remain collected and never resurrected");
        }

        [Test]
        public void ScanRegion_TokenReappearingAfterEviction_IsReAddedWithClockUtcNowAsDetectedAt()
        {
            var initialDetected = _clock.UtcNow;
            _gameApi.SpawnToken(new TokenLocation { Id = "t-reappear", Position = new Vec3(10, 0, 10), EstimatedValue = 10 });
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            var stored = _repository.Get("t-reappear");
            Assert.That(stored!.DetectedAt, Is.EqualTo(initialDetected));

            // Evict token from repository (e.g. map change, TTL, or eviction)
            _repository.Remove("t-reappear");
            Assert.That(_repository.Get("t-reappear"), Is.Null, "Token must be evicted");

            // Advance time significantly
            _clock.AdvanceSeconds(100);
            var reDetectedTime = _clock.UtcNow;

            // Re-scan region where token reappears
            _service.ScanRegion(new Region(Vec3.Zero, 50f));

            var reAdded = _repository.Get("t-reappear");
            Assert.That(reAdded, Is.Not.Null);
            Assert.That(reAdded!.DetectedAt, Is.EqualTo(reDetectedTime), "Reappearing token must receive new DetectedAt = Clock.UtcNow");
            Assert.That(reAdded.DetectedAt, Is.GreaterThan(initialDetected));
        }

        [Test]
        public void ScanRegion_ProviderThrows_NoCoverageAddedAndNothingEvicted()
        {
            var token = new TokenLocation
            {
                Id = "t-safe-throw",
                Position = new Vec3(10, 0, 10),
                EstimatedValue = 10,
                DetectedAt = _clock.UtcNow,
                LastConfirmedAt = _clock.UtcNow
            };
            _repository.Add(token);

            // Advance clock past TTL
            _clock.AdvanceSeconds(50);

            var throwingProvider = new ThrowingMapProvider();
            var serviceWithFailingProvider = new TokenDiscoveryService(
                throwingProvider,
                _repository,
                _parser,
                _validator,
                _clock,
                ttlSeconds: 30f);

            Assert.Throws<InvalidOperationException>(() =>
                serviceWithFailingProvider.ScanRegion(new Region(Vec3.Zero, 50f)));

            Assert.That(serviceWithFailingProvider.CoverageHistoryCount, Is.EqualTo(0), "Failed scan must record zero coverage entries");

            int pruned = serviceWithFailingProvider.PruneStaleTokens();
            Assert.That(pruned, Is.EqualTo(0), "No token should be evicted as a result of a failed scan");
            Assert.That(_repository.Get("t-safe-throw"), Is.Not.Null);
        }

        [Test]
        public void CoverageHistory_CoalesceIdenticalCenterAndRadius_KeepsNewerAndPreventsDuplicateGrowth()
        {
            var region = new Region(new Vec3(50, 0, 50), 30f);

            for (int i = 0; i < 5; i++)
            {
                _clock.AdvanceSeconds(1);
                _service.ScanRegion(region);
            }

            Assert.That(_service.CoverageHistoryCount, Is.EqualTo(1), "Coverage entries with identical center and radius must be coalesced");
        }

        [Test]
        public void CoverageHistory_MaxCapacity_DropsOldestAndPreservesCap()
        {
            // Perform scans with unique centers to fill history beyond capacity
            for (int i = 0; i < TokenDiscoveryService.MaxCoverageHistoryCapacity + 25; i++)
            {
                _service.ScanRegion(new Region(new Vec3(i * 100f, 0, 0), 10f));
            }

            Assert.That(_service.CoverageHistoryCount, Is.EqualTo(TokenDiscoveryService.MaxCoverageHistoryCapacity),
                "Coverage history must not exceed MaxCoverageHistoryCapacity");
        }

        [Test]
        public void ParallelOperations_TickScanRegionAndHandleMapChanged_NoDeadlock()
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            var tasks = new List<System.Threading.Tasks.Task>();

            for (int i = 0; i < 4; i++)
            {
                int workerId = i;
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    for (int step = 0; step < 50; step++)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        if (workerId == 0)
                        {
                            _service.Tick();
                        }
                        else if (workerId == 1)
                        {
                            _service.ScanRegion(new Region(new Vec3(step, 0, step), 50f));
                        }
                        else if (workerId == 2)
                        {
                            _service.HandleMapChanged(MapChangedEventArgs.SubRegion(new Region(new Vec3(step, 0, step), 25f), "Earthquake", _clock.UtcNow));
                        }
                        else
                        {
                            _service.PruneStaleTokens();
                        }
                    }
                }, cts.Token));
            }

            bool completed = System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));
            Assert.That(completed, Is.True, "Parallel operations must complete without deadlock within timeout");
        }

        [Test]
        public void Scenario_DeterministicReplay_ProducesIdenticalStateAndEvents()
        {
            (List<string> Discovered, List<string> Expired, List<string> FinalSnapshot) RunSimulation()
            {
                var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
                var repo = new TokenRepository(50f);
                var parser = new DefaultTokenParser();
                var validator = new TokenValidator(clock, 30f);
                var gameApi = new SimGameApi(clock: clock);
                var service = new TokenDiscoveryService(gameApi, repo, parser, validator, clock, gameApi, ttlSeconds: 30f);

                var discoveredLog = new List<string>();
                var expiredLog = new List<string>();

                service.TokensDiscovered += tokens =>
                {
                    foreach (var t in tokens) discoveredLog.Add($"{t.Id}:{t.Position}:{t.EstimatedValue}");
                };
                service.TokensExpired += ids =>
                {
                    foreach (var id in ids) expiredLog.Add(id);
                };

                // Seeded sequence of operations
                var rng = new Random(1337);
                for (int step = 0; step < 20; step++)
                {
                    clock.AdvanceSeconds(2);

                    if (step % 2 == 0)
                    {
                        gameApi.SpawnToken(new TokenLocation
                        {
                            Id = $"token_{step}",
                            Position = new Vec3(rng.Next(-50, 50), 0, rng.Next(-50, 50)),
                            EstimatedValue = rng.Next(1, 100),
                            Rarity = (TokenRarity)rng.Next(0, 4)
                        });
                    }

                    service.Tick();

                    if (step == 8)
                    {
                        gameApi.CollectToken("token_0");
                    }

                    if (step == 12)
                    {
                        service.HandleMapChanged(MapChangedEventArgs.SubRegion(new Region(Vec3.Zero, 20f), "CaveIn", clock.UtcNow));
                    }
                }

                var finalSnapshot = repo.GetAvailable()
                    .OrderBy(t => t.Id, StringComparer.Ordinal)
                    .Select(t => $"{t.Id}:{t.Position}:{t.EstimatedValue}:{t.IsCollected}")
                    .ToList();

                return (discoveredLog, expiredLog, finalSnapshot);
            }

            var run1 = RunSimulation();
            var run2 = RunSimulation();

            Assert.That(run1.Discovered, Is.EqualTo(run2.Discovered), "Discovered events must be deterministically identical");
            Assert.That(run1.Expired, Is.EqualTo(run2.Expired), "Expired events must be deterministically identical");
            Assert.That(run1.FinalSnapshot, Is.EqualTo(run2.FinalSnapshot), "Final repository snapshots must be deterministically identical");
        }

        [Test]
        public void Contract_ITokenDiscoveryService_EmitsOnlyDiscoveredAndExpired_ConsumersRereadSnapshots()
        {
            // Verify by reflection / contract design that ITokenDiscoveryService contains only the two contract events
            var eventNames = typeof(ITokenDiscoveryService).GetEvents().Select(e => e.Name).OrderBy(n => n).ToList();
            Assert.That(eventNames, Is.EqualTo(new[] { "TokensDiscovered", "TokensExpired" }));
        }

        #endregion

        private sealed class ThrowingMapProvider : IMapProvider
        {
            public MapData GetMap() => new MapData("map", new MapBounds(new Vec3(-100, 0, -100), new Vec3(100, 0, 100)));
            public IEnumerable<RawMapObject> GetObjects(Region region) => throw new InvalidOperationException("Simulated map fetch fault.");
        }

        private sealed class MockMapProvider : IMapProvider
        {
            private readonly IEnumerable<RawMapObject> _objects;

            public MockMapProvider(IEnumerable<RawMapObject> objects)
            {
                _objects = objects;
            }

            public MapData GetMap() => new MapData("mock_map", new MapBounds(new Vec3(-1000, 0, -1000), new Vec3(1000, 0, 1000)));

            public IEnumerable<RawMapObject> GetObjects(Region region)
            {
                return _objects.Where(o => region.Contains(o.Position));
            }
        }
    }
}
