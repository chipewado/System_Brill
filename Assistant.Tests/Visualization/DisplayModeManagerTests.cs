using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;
using Assistant.Core.Discovery;
using Assistant.Core.Visualization;
using NUnit.Framework;

namespace Assistant.Tests.Visualization
{
    [TestFixture]
    public class DisplayModeManagerTests
    {
        private DisplayModeManager _manager = null!;
        private Vec3 _playerPos;

        [SetUp]
        public void SetUp()
        {
            _manager = new DisplayModeManager();
            _playerPos = new Vec3(0f, 0f, 0f);
        }

        #region Contract Tests (Section 6.3)

        [Test]
        public void IsVisible_InclusiveFilter_ReturnsTrueOnlyForConfiguredRarities()
        {
            var mode = new DisplayMode("m1")
            {
                Filter = FilterMode.Inclusive,
                Rarities = new HashSet<TokenRarity> { TokenRarity.Rare, TokenRarity.Epic }
            };

            var rareToken = new TokenLocation { Id = "t1", Rarity = TokenRarity.Rare, Position = _playerPos };
            var epicToken = new TokenLocation { Id = "t2", Rarity = TokenRarity.Epic, Position = _playerPos };
            var commonToken = new TokenLocation { Id = "t3", Rarity = TokenRarity.Common, Position = _playerPos };

            Assert.That(mode.Matches(rareToken, _playerPos), Is.True);
            Assert.That(mode.Matches(epicToken, _playerPos), Is.True);
            Assert.That(mode.Matches(commonToken, _playerPos), Is.False);
        }

        [Test]
        public void IsVisible_ExclusiveFilter_ReturnsFalseOnlyForConfiguredRarities()
        {
            var mode = new DisplayMode("m2")
            {
                Filter = FilterMode.Exclusive,
                Rarities = new HashSet<TokenRarity> { TokenRarity.Common }
            };

            var commonToken = new TokenLocation { Id = "t1", Rarity = TokenRarity.Common, Position = _playerPos };
            var rareToken = new TokenLocation { Id = "t2", Rarity = TokenRarity.Rare, Position = _playerPos };

            Assert.That(mode.Matches(commonToken, _playerPos), Is.False);
            Assert.That(mode.Matches(rareToken, _playerPos), Is.True);
        }

        [Test]
        public void FilterTokens_InclusiveAndExclusiveWithSameList_ProduceExactComplementSets()
        {
            var rng = new Random(12345);
            var rarities = new[] { TokenRarity.Rare, TokenRarity.Epic };

            var modeInc = new DisplayMode("inc")
            {
                Filter = FilterMode.Inclusive,
                Rarities = new HashSet<TokenRarity>(rarities)
            };
            var modeExc = new DisplayMode("exc")
            {
                Filter = FilterMode.Exclusive,
                Rarities = new HashSet<TokenRarity>(rarities)
            };

            var allRarities = (TokenRarity[])Enum.GetValues(typeof(TokenRarity));
            var tokens = new List<TokenLocation>();
            for (int i = 0; i < 100; i++)
            {
                tokens.Add(new TokenLocation
                {
                    Id = $"tok_{i}",
                    Rarity = allRarities[rng.Next(allRarities.Length)],
                    Position = _playerPos,
                    EstimatedValue = 10f
                });
            }

            foreach (var token in tokens)
            {
                bool matchesInc = modeInc.Matches(token, _playerPos);
                bool matchesExc = modeExc.Matches(token, _playerPos);

                Assert.That(matchesInc ^ matchesExc, Is.True,
                    $"Token {token.Id} with rarity {token.Rarity} must match either Inclusive or Exclusive, never both or neither");
            }
        }

        [Test]
        public void GetDominantMode_MultipleMatchingModes_ReturnsHighestPriority()
        {
            var lowPriority = new DisplayMode("low") { Priority = 1, Color = ColorRgba.White, Filter = FilterMode.Exclusive };
            var highPriority = new DisplayMode("high") { Priority = 10, Color = ColorRgba.Yellow, Filter = FilterMode.Exclusive };

            _manager = new DisplayModeManager(new[] { lowPriority, highPriority });

            var token = new TokenLocation { Id = "t1", Rarity = TokenRarity.Common, Position = _playerPos };
            var dominant = _manager.GetDominantMode(token, _playerPos);

            Assert.That(dominant, Is.Not.Null);
            Assert.That(dominant!.Id, Is.EqualTo("high"));
            Assert.That(dominant.Priority, Is.EqualTo(10));
        }

        [Test]
        public void GetDominantMode_PriorityTie_SelectsFirstByListOrderThenId()
        {
            // Same priority: list order must break tie
            var modeFirstInList = new DisplayMode("z_mode") { Priority = 5, Filter = FilterMode.Exclusive };
            var modeSecondInList = new DisplayMode("a_mode") { Priority = 5, Filter = FilterMode.Exclusive };

            _manager = new DisplayModeManager(new[] { modeFirstInList, modeSecondInList });

            var token = new TokenLocation { Id = "t1", Rarity = TokenRarity.Common, Position = _playerPos };
            var dominant = _manager.GetDominantMode(token, _playerPos);

            Assert.That(dominant, Is.Not.Null);
            Assert.That(dominant!.Id, Is.EqualTo("z_mode"), "List order must take precedence over Id alphabetical tie-breaker");
        }

        [Test]
        public void FilterTokens_ExceedingCap_RespectsMaxVisibleMarkersLimitWithDeterministicTieBreaker()
        {
            var config = new OverlayConfig { MaxVisibleMarkers = 3 };
            _manager = new DisplayModeManager(new[] { BuiltInTemplates.CreateAllTokens() }, config);

            var tokens = new List<TokenLocation>
            {
                new TokenLocation { Id = "c", Rarity = TokenRarity.Epic, EstimatedValue = 50, Position = new Vec3(10, 0, 0) },
                new TokenLocation { Id = "b", Rarity = TokenRarity.Epic, EstimatedValue = 50, Position = new Vec3(10, 0, 0) },
                new TokenLocation { Id = "a", Rarity = TokenRarity.Epic, EstimatedValue = 50, Position = new Vec3(10, 0, 0) },
                new TokenLocation { Id = "d", Rarity = TokenRarity.Epic, EstimatedValue = 50, Position = new Vec3(10, 0, 0) }
            };

            var filtered = _manager.FilterTokens(tokens, _playerPos);

            Assert.That(filtered.Count, Is.EqualTo(3));
            // Complete tie on rarity, value, and distance -> Id ascending: "a", "b", "c"
            Assert.That(filtered[0].Id, Is.EqualTo("a"));
            Assert.That(filtered[1].Id, Is.EqualTo("b"));
            Assert.That(filtered[2].Id, Is.EqualTo("c"));
        }

        [Test]
        public void BuiltInTemplates_AllFiveTemplates_HaveValidDistinctConfigurationsAndRequiredIds()
        {
            var all = BuiltInTemplates.CreateAllTokens();
            var rarePlus = BuiltInTemplates.CreateRarePlus();
            var legendary = BuiltInTemplates.CreateLegendaryOnly();
            var nearby = BuiltInTemplates.CreateNearby();
            var bestValue = BuiltInTemplates.CreateBestValue();

            Assert.That(all.Id, Is.EqualTo(BuiltInTemplates.AllId));
            Assert.That(rarePlus.Id, Is.EqualTo(BuiltInTemplates.RarePlusId));
            Assert.That(legendary.Id, Is.EqualTo(BuiltInTemplates.LegendaryId));
            Assert.That(nearby.Id, Is.EqualTo(BuiltInTemplates.NearbyId));
            Assert.That(bestValue.Id, Is.EqualTo(BuiltInTemplates.BestValueId));

            var ids = new HashSet<string> { all.Id, rarePlus.Id, legendary.Id, nearby.Id, bestValue.Id };
            Assert.That(ids.Count, Is.EqualTo(5), "All 5 template IDs must be distinct");

            // Verify stable IDs without Guids
            Assert.That(Guid.TryParse(all.Id, out _), Is.False);
            Assert.That(Guid.TryParse(rarePlus.Id, out _), Is.False);
        }

        [Test]
        public void ToggleModeByKey_ValidKey_TogglesEnabledState()
        {
            var mode = new DisplayMode("m1") { KeyBinding = "F1", Enabled = true };
            _manager = new DisplayModeManager(new[] { mode });

            _manager.ToggleModeByKey("f1"); // Case-insensitive
            Assert.That(_manager.Modes[0].Enabled, Is.False);

            _manager.ToggleModeByKey("F1");
            Assert.That(_manager.Modes[0].Enabled, Is.True);

            // Unknown key does nothing
            _manager.ToggleModeByKey("F9");
            Assert.That(_manager.Modes[0].Enabled, Is.True);
        }

        #endregion

        #region Deterministic Matching & Edge Cases

        [Test]
        public void Matching_EmptyRaritiesSet_InclusiveMatchesNothing_ExclusiveMatchesEverything()
        {
            var inclusiveEmpty = new DisplayMode("inc_empty")
            {
                Filter = FilterMode.Inclusive,
                Rarities = new HashSet<TokenRarity>()
            };

            var exclusiveEmpty = new DisplayMode("exc_empty")
            {
                Filter = FilterMode.Exclusive,
                Rarities = new HashSet<TokenRarity>()
            };

            var token = new TokenLocation { Id = "t1", Rarity = TokenRarity.Legendary, Position = _playerPos, EstimatedValue = 100 };

            Assert.That(inclusiveEmpty.Matches(token, _playerPos), Is.False, "Inclusive mode with empty set matches nothing");
            Assert.That(exclusiveEmpty.Matches(token, _playerPos), Is.True, "Exclusive mode with empty set matches everything");
        }

        [Test]
        public void Matching_MinimumValueAndMaxDistance_EnforcesThresholds()
        {
            var mode = new DisplayMode("m")
            {
                Filter = FilterMode.Exclusive,
                MinValue = 50f,
                MaxDistance = 100f
            };

            var lowValue = new TokenLocation { Id = "t1", Position = new Vec3(10, 0, 0), EstimatedValue = 40f };
            var highValueClose = new TokenLocation { Id = "t2", Position = new Vec3(50, 0, 0), EstimatedValue = 60f };
            var highValueFar = new TokenLocation { Id = "t3", Position = new Vec3(150, 0, 0), EstimatedValue = 80f };

            Assert.That(mode.Matches(lowValue, _playerPos), Is.False);
            Assert.That(mode.Matches(highValueClose, _playerPos), Is.True);
            Assert.That(mode.Matches(highValueFar, _playerPos), Is.False);
        }

        [Test]
        public void Matching_UnlimitedDistance_NullMaxDistanceMatchesAtAnyDistance()
        {
            var mode = new DisplayMode("m")
            {
                Filter = FilterMode.Exclusive,
                MaxDistance = null // unlimited
            };

            var farToken = new TokenLocation { Id = "t_far", Position = new Vec3(50000f, 0, 50000f), EstimatedValue = 10f };
            Assert.That(mode.Matches(farToken, _playerPos), Is.True);
        }

        [Test]
        public void Matching_DisabledMode_DoesNotMatch()
        {
            var mode = new DisplayMode("m")
            {
                Enabled = false,
                Filter = FilterMode.Exclusive
            };

            var token = new TokenLocation { Id = "t1", Position = _playerPos, EstimatedValue = 10f };
            Assert.That(mode.Matches(token, _playerPos), Is.False);
        }

        [Test]
        public void Matching_NegativeOrNaNDistanceOrValue_FollowsValidationPolicy()
        {
            var modeNegativeDist = new DisplayMode("m_neg") { MaxDistance = -10f };
            var modeNaNDist = new DisplayMode("m_nan") { MaxDistance = float.NaN };
            var modeNaNValue = new DisplayMode("m_nan_val") { MinValue = float.NaN };

            var token = new TokenLocation { Id = "t1", Position = _playerPos, EstimatedValue = 10f };

            Assert.That(modeNegativeDist.Matches(token, _playerPos), Is.False);
            Assert.That(modeNaNDist.Matches(token, _playerPos), Is.False);
            Assert.That(modeNaNValue.Matches(token, _playerPos), Is.False);
        }

        [Test]
        public void AddMode_DuplicateId_ThrowsArgumentException()
        {
            _manager = new DisplayModeManager(new[] { new DisplayMode("id1") });

            Assert.Throws<ArgumentException>(() => _manager.AddMode(new DisplayMode("id1")));
        }

        [Test]
        public void DisplayMode_BlankOrWhitespaceId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new DisplayMode(""));
            Assert.Throws<ArgumentException>(() => new DisplayMode("   "));
        }

        [Test]
        public void ApplyTemplate_UnknownName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _manager.ApplyTemplate("NonExistentTemplate"));
        }

        [Test]
        public void DominantMode_ControlsBothColorAndTracer()
        {
            // Mode A: Higher priority, ShowTracer = false, Color = Red
            var modeA = new DisplayMode("modeA")
            {
                Priority = 10,
                ShowTracer = false,
                Color = ColorRgba.Red,
                Filter = FilterMode.Exclusive
            };

            // Mode B: Lower priority, ShowTracer = true, Color = Cyan
            var modeB = new DisplayMode("modeB")
            {
                Priority = 5,
                ShowTracer = true,
                Color = ColorRgba.Cyan,
                Filter = FilterMode.Exclusive
            };

            _manager = new DisplayModeManager(new[] { modeA, modeB });

            var token = new TokenLocation { Id = "t1", Position = _playerPos, EstimatedValue = 10f };
            var dominant = _manager.GetDominantMode(token, _playerPos);

            Assert.That(dominant, Is.Not.Null);
            Assert.That(dominant!.Color, Is.EqualTo(ColorRgba.Red));
            Assert.That(dominant.ShowTracer, Is.False, "Dominant mode must control BOTH color and tracer");

            var tracers = _manager.GetTracerTokens(new[] { token }, _playerPos);
            Assert.That(tracers.Count, Is.EqualTo(0), "Token must not appear in tracers because its dominant mode has ShowTracer = false");
        }

        [Test]
        public void FilterTokens_MaxVisibleMarkersZeroOrNegative_ReturnsEmpty()
        {
            var configZero = new OverlayConfig { MaxVisibleMarkers = 0 };
            _manager = new DisplayModeManager(new[] { BuiltInTemplates.CreateAllTokens() }, configZero);

            var tokens = new[] { new TokenLocation { Id = "t1", Position = _playerPos, EstimatedValue = 10f } };
            Assert.That(_manager.FilterTokens(tokens, _playerPos), Is.Empty);

            var configNeg = new OverlayConfig { MaxVisibleMarkers = -5 };
            _manager.UpdateConfig(configNeg);
            Assert.That(_manager.FilterTokens(tokens, _playerPos), Is.Empty);
        }

        [Test]
        public void GetTracerTokens_RespectsDominantModeShowTracerAndDistanceAndCap()
        {
            var tracerMode = new DisplayMode("tracer_mode")
            {
                Filter = FilterMode.Exclusive,
                ShowTracer = true,
                Priority = 10
            };
            var config = new OverlayConfig
            {
                TracerMaxDistance = 100f,
                MaxTracerCount = 2
            };

            _manager = new DisplayModeManager(new[] { tracerMode }, config);

            var tokens = new List<TokenLocation>
            {
                new TokenLocation { Id = "t1", Position = new Vec3(20, 0, 0), EstimatedValue = 10, Rarity = TokenRarity.Legendary },
                new TokenLocation { Id = "t2", Position = new Vec3(50, 0, 0), EstimatedValue = 10, Rarity = TokenRarity.Epic },
                new TokenLocation { Id = "t3", Position = new Vec3(70, 0, 0), EstimatedValue = 10, Rarity = TokenRarity.Rare },
                new TokenLocation { Id = "t_far", Position = new Vec3(200, 0, 0), EstimatedValue = 10, Rarity = TokenRarity.Legendary } // beyond 100m
            };

            var tracers = _manager.GetTracerTokens(tokens, _playerPos);

            Assert.That(tracers.Count, Is.EqualTo(2), "Must cap to MaxTracerCount");
            Assert.That(tracers[0].Id, Is.EqualTo("t1")); // Legendary
            Assert.That(tracers[1].Id, Is.EqualTo("t2")); // Epic
            Assert.That(tracers.Any(t => t.Id == "t_far"), Is.False, "Far token beyond TracerMaxDistance must be excluded");
        }

        [Test]
        public void Events_ModesChangedAndConfigChanged_FireOncePerChange_AndIsolateSubscriberExceptions()
        {
            int modesChangedFired = 0;
            int configChangedFired = 0;

            _manager.ModesChanged += () => throw new InvalidOperationException("Failing subscriber");
            _manager.ModesChanged += () => modesChangedFired++;

            _manager.ConfigChanged += () => throw new InvalidOperationException("Failing subscriber");
            _manager.ConfigChanged += () => configChangedFired++;

            _manager.AddMode(new DisplayMode("new_mode"));
            Assert.That(modesChangedFired, Is.EqualTo(1));

            _manager.UpdateConfig(new OverlayConfig { MaxVisibleMarkers = 20 });
            Assert.That(configChangedFired, Is.EqualTo(1));
        }

        [Test]
        public void FilterTokens_DeterministicRepeatedCalls_ReturnsIdenticalResults()
        {
            _manager.ApplyTemplate("All");
            var tokens = new List<TokenLocation>
            {
                new TokenLocation { Id = "t1", Position = new Vec3(10, 0, 20), EstimatedValue = 50, Rarity = TokenRarity.Rare },
                new TokenLocation { Id = "t2", Position = new Vec3(30, 0, 40), EstimatedValue = 100, Rarity = TokenRarity.Legendary }
            };

            var first = _manager.FilterTokens(tokens, _playerPos);

            for (int i = 0; i < 20; i++)
            {
                var repeat = _manager.FilterTokens(tokens, _playerPos);
                Assert.That(repeat.Select(t => t.Id), Is.EqualTo(first.Select(t => t.Id)));
            }
        }

        [Test]
        public void FilterTokens_DoesNotMutateTokenLocationOrTokenRepository()
        {
            var repo = new TokenRepository(50f);
            var token = new TokenLocation
            {
                Id = "repo_token",
                Position = new Vec3(15f, 0f, 15f),
                Rarity = TokenRarity.Epic,
                EstimatedValue = 75f
            };
            repo.Add(token);

            var filtered = _manager.FilterTokens(repo.GetAvailable(), _playerPos);

            Assert.That(filtered.Count, Is.EqualTo(1));
            // Verify repository still contains the original unmodified token
            var stored = repo.Get("repo_token");
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Position.X, Is.EqualTo(15f));
            Assert.That(stored.IsCollected, Is.False);
        }

        [Test]
        public void Modes_ReturnsDefensiveSnapshots_MutatingReturnedListDoesNotAffectManager()
        {
            var modes = _manager.Modes;
            Assert.That(modes.Count, Is.GreaterThan(0));

            // Mutating returned object
            modes[0].Enabled = !modes[0].Enabled;

            // Internal manager state must remain unchanged
            Assert.That(_manager.Modes[0].Enabled, Is.Not.EqualTo(modes[0].Enabled));
        }

        #endregion
    }
}
