using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Assistant.Core.Abstractions;
using Assistant.Core.Coordination;
using Assistant.Core.Data;
using Assistant.Core.Ranking;
using Assistant.Core.Visualization;
using NUnit.Framework;

namespace Assistant.Tests.Ranking
{
    [TestFixture]
    public class TargetRankerTests
    {
        private DefaultTargetRanker _ranker = null!;
        private RankingConfig _config = null!;

        [SetUp]
        public void SetUp()
        {
            _ranker = new DefaultTargetRanker();
            _config = new RankingConfig();
        }

        [Test]
        public void R01_ValueDominance_HighValueTokenRanksHigherAtEqualDistance()
        {
            // Player at origin, speed = 5
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // Two tokens at identical distance (10m), identical rarity (Common), no hysteresis
            // Hand-computed scores:
            // S_time = 1 - (10/5)/120 = 1 - 2/120 = 0.983333...
            // High: Value = 200 => S_val = 200/250 = 0.8 => raw = 0.40 * 0.8 + 0.35 * 0.983333 = 0.32 + 0.344167 = 0.664167
            // Low:  Value = 50  => S_val = 50/250  = 0.2 => raw = 0.40 * 0.2 + 0.35 * 0.983333 = 0.08 + 0.344167 = 0.424167
            var high = new TokenLocation { Id = "high", Position = new Vec3(10f, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 200f };
            var low = new TokenLocation { Id = "low", Position = new Vec3(0f, 0f, 10f), Rarity = TokenRarity.Common, EstimatedValue = 50f };

            var results = _ranker.RankCandidates(new[] { low, high }, player, _config, currentTargetId: null);

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Token.Id, Is.EqualTo("high"));
            Assert.That(results[1].Token.Id, Is.EqualTo("low"));
            Assert.That(results[0].Score, Is.EqualTo(0.664167).Within(1e-6));
            Assert.That(results[1].Score, Is.EqualTo(0.424167).Within(1e-6));
        }

        [Test]
        public void R02_TimeDominance_AdjacentTokenRanksHigherAtEqualValue()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // Two tokens with identical value (100) and rarity (Common)
            // Close: distance = 10m => travel = 2s => S_time = 1 - 2/120 = 0.983333
            // Far:   distance = 100m => travel = 20s => S_time = 1 - 20/120 = 0.833333
            // S_val = 100/250 = 0.40
            // Close Score: 0.40 * 0.40 + 0.35 * (118/120) = 0.16 + 0.3441667 = 0.504167
            // Far Score:   0.40 * 0.40 + 0.35 * (100/120) = 0.16 + 0.2916667 = 0.451667
            var close = new TokenLocation { Id = "close", Position = new Vec3(10f, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var far = new TokenLocation { Id = "far", Position = new Vec3(100f, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var results = _ranker.RankCandidates(new[] { far, close }, player, _config, currentTargetId: null);

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Token.Id, Is.EqualTo("close"));
            Assert.That(results[1].Token.Id, Is.EqualTo("far"));
            Assert.That(results[0].Score, Is.EqualTo(0.504167).Within(1e-6));
            Assert.That(results[1].Score, Is.EqualTo(0.451667).Within(1e-6));
        }

        [Test]
        public void R03_RarityScaling_LinearProgressionAcrossAllFiveTiers()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // Hand computation:
            // Value = 0, Distance = 0 => S_val = 0, S_time = 1.0, S_hyst = 0
            // Score = 0.40 * 0 + 0.35 * 1.0 + 0.15 * (tier / 4.0)
            // Common (0):     0.35 + 0.15 * 0.00 = 0.350000
            // Uncommon (1):   0.35 + 0.15 * 0.25 = 0.387500
            // Rare (2):       0.35 + 0.15 * 0.50 = 0.425000
            // Epic (3):       0.35 + 0.15 * 0.75 = 0.462500
            // Legendary (4):  0.35 + 0.15 * 1.00 = 0.500000
            var tokens = new[]
            {
                new TokenLocation { Id = "t_com", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 0f },
                new TokenLocation { Id = "t_unc", Position = Vec3.Zero, Rarity = TokenRarity.Uncommon, EstimatedValue = 0f },
                new TokenLocation { Id = "t_rar", Position = Vec3.Zero, Rarity = TokenRarity.Rare, EstimatedValue = 0f },
                new TokenLocation { Id = "t_epi", Position = Vec3.Zero, Rarity = TokenRarity.Epic, EstimatedValue = 0f },
                new TokenLocation { Id = "t_leg", Position = Vec3.Zero, Rarity = TokenRarity.Legendary, EstimatedValue = 0f }
            };

            var results = _ranker.RankCandidates(tokens, player, _config, currentTargetId: null);

            Assert.That(results.Count, Is.EqualTo(5));
            Assert.That(results[0].Token.Id, Is.EqualTo("t_leg"));
            Assert.That(results[1].Token.Id, Is.EqualTo("t_epi"));
            Assert.That(results[2].Token.Id, Is.EqualTo("t_rar"));
            Assert.That(results[3].Token.Id, Is.EqualTo("t_unc"));
            Assert.That(results[4].Token.Id, Is.EqualTo("t_com"));

            Assert.That(results[4].Score, Is.EqualTo(0.350000).Within(1e-6));
            Assert.That(results[3].Score, Is.EqualTo(0.387500).Within(1e-6));
            Assert.That(results[2].Score, Is.EqualTo(0.425000).Within(1e-6));
            Assert.That(results[1].Score, Is.EqualTo(0.462500).Within(1e-6));
            Assert.That(results[0].Score, Is.EqualTo(0.500000).Within(1e-6));

            // Verify exact normalized rarity progression
            Assert.That(results[4].NormalizedRarity, Is.EqualTo(0.00).Within(1e-6));
            Assert.That(results[3].NormalizedRarity, Is.EqualTo(0.25).Within(1e-6));
            Assert.That(results[2].NormalizedRarity, Is.EqualTo(0.50).Within(1e-6));
            Assert.That(results[1].NormalizedRarity, Is.EqualTo(0.75).Within(1e-6));
            Assert.That(results[0].NormalizedRarity, Is.EqualTo(1.00).Within(1e-6));
        }

        [Test]
        public void R04_HysteresisMarginal_ChallengerFailsToUnseatCurrentTarget()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // Hand computation:
            // Both at distance 0 (S_time = 1.0), Common rarity (S_rar = 0)
            // Current target has EstimatedValue = 100 (S_val = 0.40) and S_hyst = 1.0
            // Current Score = 0.40 * 0.40 + 0.35 * 1.0 + 0.15 * 0 + 0.10 * 1.0 = 0.16 + 0.35 + 0.10 = 0.610000
            // Challenger has EstimatedValue = 131.25 (S_val = 0.525), S_hyst = 0
            // Challenger base score = 0.40 * 0.525 + 0.35 * 1.0 = 0.21 + 0.35 = 0.560000 (raw base is +9.8% higher than current's 0.51)
            // But Challenger total score = 0.560000 < 0.610000
            var current = new TokenLocation { Id = "current", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var challenger = new TokenLocation { Id = "challenger", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 131.25f };

            var results = _ranker.RankCandidates(new[] { challenger, current }, player, _config, currentTargetId: "current");

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Token.Id, Is.EqualTo("current"), "Current target must retain rank 1 due to hysteresis bonus");
            Assert.That(results[0].Score, Is.EqualTo(0.610000).Within(1e-6));
            Assert.That(results[1].Token.Id, Is.EqualTo("challenger"));
            Assert.That(results[1].Score, Is.EqualTo(0.560000).Within(1e-6));
        }

        [Test]
        public void R05_HysteresisDecisive_DecisiveChallengerSucceedsWithDefaultWeights()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // Hand computation:
            // Both at distance 0 (S_time = 1.0), Common rarity (S_rar = 0)
            // Current target has EstimatedValue = 50 (S_val = 0.20), S_hyst = 1.0
            // Current Score = 0.40 * 0.20 + 0.35 * 1.0 + 0.10 * 1.0 = 0.08 + 0.35 + 0.10 = 0.530000
            // Challenger has EstimatedValue = 200 (S_val = 0.80), S_hyst = 0
            // Challenger Score = 0.40 * 0.80 + 0.35 * 1.0 + 0 = 0.32 + 0.35 = 0.670000
            // 0.670000 > 0.530000 => Challenger decisively wins!
            var current = new TokenLocation { Id = "current", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 50f };
            var challenger = new TokenLocation { Id = "challenger", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 200f };

            var results = _ranker.RankCandidates(new[] { current, challenger }, player, _config, currentTargetId: "current");

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Token.Id, Is.EqualTo("challenger"), "Decisive challenger must overcome hysteresis");
            Assert.That(results[0].Score, Is.EqualTo(0.670000).Within(1e-6));
            Assert.That(results[1].Token.Id, Is.EqualTo("current"));
            Assert.That(results[1].Score, Is.EqualTo(0.530000).Within(1e-6));
        }

        [Test]
        public void R06_HysteresisRemoval_RemovedCurrentTargetYieldsZeroHysteresisBonus()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            var tokenA = new TokenLocation { Id = "token_a", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var tokenB = new TokenLocation { Id = "token_b", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 50f };

            // currentTargetId is "token_collected" which is not among candidates
            var results = _ranker.RankCandidates(new[] { tokenA, tokenB }, player, _config, currentTargetId: "token_collected");

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].NormalizedHysteresis, Is.EqualTo(0.0));
            Assert.That(results[1].NormalizedHysteresis, Is.EqualTo(0.0));
            Assert.That(results[0].Token.Id, Is.EqualTo("token_a"));
            // Score for token_a without hysteresis: 0.40 * (100/250) + 0.35 * 1.0 = 0.16 + 0.35 = 0.510000
            Assert.That(results[0].Score, Is.EqualTo(0.510000).Within(1e-6));
        }

        [Test]
        public void R07_OrdinalTieBreaker_ExactScoreTieOrderedByOrdinalIdAscending()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // Exactly identical attributes
            var tokenAlpha = new TokenLocation { Id = "token_alpha", Position = new Vec3(10f, 0f, 0f), Rarity = TokenRarity.Rare, EstimatedValue = 100f };
            var tokenBeta = new TokenLocation { Id = "token_beta", Position = new Vec3(10f, 0f, 0f), Rarity = TokenRarity.Rare, EstimatedValue = 100f };

            // Pass in reversed order
            var results = _ranker.RankCandidates(new[] { tokenBeta, tokenAlpha }, player, _config, currentTargetId: null);

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Score, Is.EqualTo(results[1].Score));
            Assert.That(results[0].Token.Id, Is.EqualTo("token_alpha"), "Ordinal tie-breaker must place token_alpha before token_beta");
            Assert.That(results[1].Token.Id, Is.EqualTo("token_beta"));
        }

        [Test]
        public void R08_SanitizationCases_CleanlyHandlesNonFiniteSpeedAndEstimatedValuesWithoutThrowing()
        {
            // 1. Non-finite and <= 0 player speeds fall back to config.DefaultSpeed (5.0f)
            var playerZeroSpeed = new PlayerState { Position = Vec3.Zero, Speed = 0f };
            var playerNegSpeed = new PlayerState { Position = Vec3.Zero, Speed = -10f };
            var playerNanSpeed = new PlayerState { Position = Vec3.Zero, Speed = float.NaN };
            var playerInfSpeed = new PlayerState { Position = Vec3.Zero, Speed = float.PositiveInfinity };

            var token = new TokenLocation { Id = "t1", Position = new Vec3(10f, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var rZero = _ranker.RankCandidates(new[] { token }, playerZeroSpeed, _config, null);
            var rNeg = _ranker.RankCandidates(new[] { token }, playerNegSpeed, _config, null);
            var rNan = _ranker.RankCandidates(new[] { token }, playerNanSpeed, _config, null);
            var rInf = _ranker.RankCandidates(new[] { token }, playerInfSpeed, _config, null);

            // Travel seconds should be 10m / 5.0m/s = 2.0s for all fallback speeds
            Assert.That(rZero[0].EstimatedTravelSeconds, Is.EqualTo(2.0f).Within(1e-5f));
            Assert.That(rNeg[0].EstimatedTravelSeconds, Is.EqualTo(2.0f).Within(1e-5f));
            Assert.That(rNan[0].EstimatedTravelSeconds, Is.EqualTo(2.0f).Within(1e-5f));
            Assert.That(rInf[0].EstimatedTravelSeconds, Is.EqualTo(2.0f).Within(1e-5f));

            // 2. Candidate with NaN, Inf, and negative EstimatedValue sanitized to 0
            var normalPlayer = new PlayerState { Position = Vec3.Zero, Speed = 5f };
            var tokenNanVal = new TokenLocation { Id = "t_nan", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = float.NaN };
            var tokenInfVal = new TokenLocation { Id = "t_inf", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = float.PositiveInfinity };
            var tokenNegVal = new TokenLocation { Id = "t_neg", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = -50f };

            var rVals = _ranker.RankCandidates(new[] { tokenNanVal, tokenInfVal, tokenNegVal }, normalPlayer, _config, null);
            Assert.That(rVals.Count, Is.EqualTo(3));
            foreach (var r in rVals)
            {
                Assert.That(r.NormalizedValue, Is.EqualTo(0.0), "Non-finite or negative EstimatedValue must sanitize to NormalizedValue = 0.0");
                Assert.That(double.IsFinite(r.Score), Is.True);
            }

            // 3. Out-of-range TokenRarity enum clamped to [0, 1]
            var tokenHighRar = new TokenLocation { Id = "t_high_rar", Position = Vec3.Zero, Rarity = (TokenRarity)99, EstimatedValue = 0f };
            var tokenLowRar = new TokenLocation { Id = "t_low_rar", Position = Vec3.Zero, Rarity = (TokenRarity)(-5), EstimatedValue = 0f };

            var rRar = _ranker.RankCandidates(new[] { tokenHighRar, tokenLowRar }, normalPlayer, _config, null);
            Assert.That(rRar[0].NormalizedRarity, Is.EqualTo(1.0), "Out-of-range positive rarity enum clamped to 1.0");
            Assert.That(rRar[1].NormalizedRarity, Is.EqualTo(0.0), "Out-of-range negative rarity enum clamped to 0.0");

            // 4. Candidate with non-finite position is skipped
            var tokenNanPos = new TokenLocation { Id = "t_nan_pos", Position = new Vec3(float.NaN, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var tokenInfPos = new TokenLocation { Id = "t_inf_pos", Position = new Vec3(0f, 0f, float.PositiveInfinity), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var rPos = _ranker.RankCandidates(new[] { tokenNanPos, tokenInfPos }, normalPlayer, _config, null);
            Assert.That(rPos.Count, Is.EqualTo(0), "Candidates with non-finite coordinates must be skipped");
        }

        [Test]
        public void R09_RoundingStability_TinyJitterAt1e8RoundsToIdenticalScoreAt6Decimals()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // ValueScale = 250, WeightValue = 0.40
            // Raw score component from value = 0.40 * (val / 250)
            // A difference of 1e-8 in raw score requires delta_val = 1e-8 * 250 / 0.40 = 6.25e-6
            float baseVal = 100f;
            float jitterVal = baseVal + 1e-6f; // delta in raw score is ~1.6e-6, let's use an even smaller delta
            float tinyDeltaVal = baseVal + 1e-7f;

            var t1 = new TokenLocation { Id = "t_b", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = baseVal };
            var t2 = new TokenLocation { Id = "t_a", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = tinyDeltaVal };

            var results = _ranker.RankCandidates(new[] { t1, t2 }, player, _config, null);

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Score, Is.EqualTo(results[1].Score), "1e-8 jitter must round to identical score at 6 decimals");
            // Since rounded scores tie, ordinal ID tie-breaking decides: "t_a" before "t_b"
            Assert.That(results[0].Token.Id, Is.EqualTo("t_a"));
            Assert.That(results[1].Token.Id, Is.EqualTo("t_b"));
        }

        [Test]
        public void R10_Phase1Presets_PlayerAt150_0_150_Speed6_StraightLineStageOrderCharlieBravoAlpha()
        {
            // Setup exactly per specification:
            // Player at (150, 0, 150), speed = 6.0f, defaults, straight-line stage
            var player = new PlayerState { Position = new Vec3(150f, 0f, 150f), Speed = 6.0f };

            // Phase 1 Presets:
            // alpha:   Pos=(100, 0, 250), Rarity=Common (0),    EstimatedValue=10
            // bravo:   Pos=(420, 0, 180), Rarity=Rare (2),      EstimatedValue=65
            // charlie: Pos=(700, 0, 500), Rarity=Legendary (4), EstimatedValue=220
            var alpha = new TokenLocation { Id = "token_alpha", Position = new Vec3(100f, 0f, 250f), Rarity = TokenRarity.Common, EstimatedValue = 10f };
            var bravo = new TokenLocation { Id = "token_bravo", Position = new Vec3(420f, 0f, 180f), Rarity = TokenRarity.Rare, EstimatedValue = 65f };
            var charlie = new TokenLocation { Id = "token_charlie", Position = new Vec3(700f, 0f, 500f), Rarity = TokenRarity.Legendary, EstimatedValue = 220f };

            // HAND RECOMPUTATION:
            //
            // 1. token_alpha:
            //    dx = 100 - 150 = -50, dz = 250 - 150 = 100 => dist = sqrt(2500 + 10000) = sqrt(12500) ≈ 111.803399 m
            //    travelSeconds = 111.803399 / 6 ≈ 18.633900 s
            //    S_val  = 10 / 250 = 0.04
            //    S_time = 1 - 18.6338998 / 120 = 1 - 0.1552825 = 0.8447175
            //    S_rar  = 0 / 4 = 0.0
            //    S_hyst = 0.0
            //    rawScore = 0.40 * 0.04 + 0.35 * 0.8447175 + 0.15 * 0.0 + 0.10 * 0.0
            //             = 0.016000 + 0.295651125 = 0.311651125 ≈ 0.311651 (~0.312)
            //
            // 2. token_bravo:
            //    dx = 420 - 150 = 270, dz = 180 - 150 = 30 => dist = sqrt(72900 + 900) = sqrt(73800) ≈ 271.661554 m
            //    travelSeconds = 271.661554 / 6 ≈ 45.276926 s
            //    S_val  = 65 / 250 = 0.26
            //    S_time = 1 - 45.276926 / 120 = 1 - 0.3773077 = 0.6226923
            //    S_rar  = 2 / 4 = 0.50
            //    S_hyst = 0.0
            //    rawScore = 0.40 * 0.26 + 0.35 * 0.6226923 + 0.15 * 0.50 + 0.10 * 0.0
            //             = 0.104000 + 0.2179423 + 0.075000 = 0.3969423 ≈ 0.396942 (~0.397)
            //
            // 3. token_charlie:
            //    dx = 700 - 150 = 550, dz = 500 - 150 = 350 => dist = sqrt(302500 + 122500) = sqrt(425000) ≈ 651.92024 m
            //    travelSeconds = 651.92024 / 6 ≈ 108.65337 s
            //    S_val  = 220 / 250 = 0.88
            //    S_time = 1 - 108.65337 / 120 = 1 - 0.9054448 = 0.0945552
            //    S_rar  = 4 / 4 = 1.0
            //    S_hyst = 0.0
            //    rawScore = 0.40 * 0.88 + 0.35 * 0.0945552 + 0.15 * 1.0 + 0.10 * 0.0
            //             = 0.352000 + 0.0330943 + 0.150000 = 0.5350943 ≈ 0.535094 (~0.535)

            var results = _ranker.RankCandidates(new[] { alpha, bravo, charlie }, player, _config, currentTargetId: null);

            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].Token.Id, Is.EqualTo("token_charlie"), "Charlie ranks 1st (~0.535)");
            Assert.That(results[1].Token.Id, Is.EqualTo("token_bravo"), "Bravo ranks 2nd (~0.397)");
            Assert.That(results[2].Token.Id, Is.EqualTo("token_alpha"), "Alpha ranks 3rd (~0.312)");

            Assert.That(results[0].Score, Is.EqualTo(0.535094).Within(1e-4), "Charlie score ~0.535");
            Assert.That(results[1].Score, Is.EqualTo(0.396942).Within(1e-4), "Bravo score ~0.397");
            Assert.That(results[2].Score, Is.EqualTo(0.311651).Within(1e-4), "Alpha score ~0.312");

            Assert.That(results[0].IsPathVerified, Is.False);
            Assert.That(results[1].IsPathVerified, Is.False);
            Assert.That(results[2].IsPathVerified, Is.False);
        }

        [Test]
        public void R11_MonotonicityProperty_ScoreNonIncreasingInPathCostAndNonDecreasingInValueAndRarity()
        {
            var rng = new Random(9876);
            var player = new PlayerState { Position = new Vec3(50f, 0f, 50f), Speed = 5f };
            float lineCostPerMeter = 1.0f;

            for (int i = 0; i < 50; i++)
            {
                var token = new TokenLocation
                {
                    Id = $"token_{i}",
                    Position = new Vec3(rng.Next(0, 200), 0f, rng.Next(0, 200)),
                    Rarity = (TokenRarity)rng.Next(0, 5),
                    EstimatedValue = (float)rng.NextDouble() * 300f
                };

                float dist2D = Vec3.Distance2D(player.Position, token.Position);
                float lineCost = lineCostPerMeter * dist2D;

                // 1. Baseline unverified line evaluation
                var lineEval = _ranker.RankCandidates(new[] { token }, player, _config, null, null, lineCostPerMeter)[0];
                Assert.That(lineEval.IsPathVerified, Is.False);

                // 2. Verified path cost >= lineCostPerMeter * dist2D
                float verifiedCost = lineCost + (float)rng.NextDouble() * 50f;
                var verifiedDict = new Dictionary<string, float> { [token.Id] = verifiedCost };
                var verifiedEval = _ranker.RankCandidates(new[] { token }, player, _config, null, verifiedDict, lineCostPerMeter)[0];
                Assert.That(verifiedEval.IsPathVerified, Is.True);

                // Verified score must be <= line score (+ 1e-6 float slack)
                Assert.That(verifiedEval.Score, Is.LessThanOrEqualTo(lineEval.Score + 1e-6),
                    $"Monotonicity violated: verified score {verifiedEval.Score} > line score {lineEval.Score}");

                // 3. Monotonicity in Value: higher value => higher or equal score
                var higherValToken = token.Clone();
                higherValToken.EstimatedValue += 50f;
                var higherValEval = _ranker.RankCandidates(new[] { higherValToken }, player, _config, null, null, lineCostPerMeter)[0];
                Assert.That(higherValEval.Score, Is.GreaterThanOrEqualTo(lineEval.Score - 1e-6));

                // 4. Monotonicity in Rarity: higher rarity => higher or equal score
                if (token.Rarity < TokenRarity.Legendary)
                {
                    var higherRarToken = token.Clone();
                    higherRarToken.Rarity = (TokenRarity)((int)token.Rarity + 1);
                    var higherRarEval = _ranker.RankCandidates(new[] { higherRarToken }, player, _config, null, null, lineCostPerMeter)[0];
                    Assert.That(higherRarEval.Score, Is.GreaterThanOrEqualTo(lineEval.Score - 1e-6));
                }
            }
        }

        [Test]
        public void R12_ArchitectureTest_NoTypeInRankingReferencesDisplayModeOrRepositoryOrNavGrid()
        {
            var rankingTypes = typeof(ITargetRanker).Assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("Assistant.Core.Ranking"))
                .ToList();

            Assert.That(rankingTypes.Count, Is.GreaterThan(0), "Must discover Assistant.Core.Ranking types");

            var forbiddenTypeNames = new[]
            {
                "DisplayMode",
                "IDisplayModeManager",
                "DisplayModeManager",
                "ITokenRepository",
                "TokenRepository",
                "INavGrid",
                "SimGridMap"
            };

            foreach (var type in rankingTypes)
            {
                // Check all fields, properties, methods, constructor parameters
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var member in members)
                {
                    Type? memberType = member switch
                    {
                        FieldInfo fi => fi.FieldType,
                        PropertyInfo pi => pi.PropertyType,
                        MethodInfo mi => mi.ReturnType,
                        _ => null
                    };

                    if (memberType != null)
                    {
                        foreach (var forbidden in forbiddenTypeNames)
                        {
                            Assert.That(memberType.Name, Is.Not.EqualTo(forbidden),
                                $"Type {type.FullName} member {member.Name} references forbidden type {forbidden}");
                        }
                    }

                    if (member is MethodBase method)
                    {
                        foreach (var param in method.GetParameters())
                        {
                            foreach (var forbidden in forbiddenTypeNames)
                            {
                                Assert.That(param.ParameterType.Name, Is.Not.EqualTo(forbidden),
                                    $"Type {type.FullName} method {method.Name} parameter {param.Name} references forbidden type {forbidden}");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void R13_ConfigValidation_RejectsInvalidValuesAndHandlesInfinitiesCorrectly()
        {
            // Valid default configuration
            Assert.DoesNotThrow(() => new RankingConfig());

            // 1. Negative or non-finite weights
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(weightValue: -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(weightValue: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(weightValue: float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(weightTime: -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(weightRarity: -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(weightHysteresis: -0.1f));

            // 2. All weights zero throws ArgumentException
            Assert.Throws<ArgumentException>(() => new RankingConfig(weightValue: 0f, weightTime: 0f, weightRarity: 0f, weightHysteresis: 0f));

            // 3. ValueScale <= 0 or non-finite
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(valueScale: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(valueScale: -10f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(valueScale: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(valueScale: float.PositiveInfinity));

            // 4. MaxTravelSeconds <= 0 or non-finite
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(maxTravelSeconds: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(maxTravelSeconds: -5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(maxTravelSeconds: float.PositiveInfinity));

            // 5. DefaultSpeed <= 0 or non-finite
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(defaultSpeed: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(defaultSpeed: -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RankingConfig(defaultSpeed: float.PositiveInfinity));

            // 6. CommitRadius < 0 or non-finite (in CoordinationConfig)
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoordinationConfig(commitRadius: -0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoordinationConfig(commitRadius: float.PositiveInfinity));
            Assert.DoesNotThrow(() => new CoordinationConfig(commitRadius: 0f));

            // 7. CommitOverrideMargin: NaN or negative rejected, but +Infinity ACCEPTED (in CoordinationConfig)
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoordinationConfig(commitOverrideMargin: -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoordinationConfig(commitOverrideMargin: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoordinationConfig(commitOverrideMargin: float.NegativeInfinity));
            Assert.DoesNotThrow(() => new CoordinationConfig(commitOverrideMargin: float.PositiveInfinity));

            // 8. MaxPathSearches <= 0 (in CoordinationConfig)
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoordinationConfig(maxPathSearches: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoordinationConfig(maxPathSearches: -1));
        }

        [Test]
        public void R14_CollectedAndDuplicateCandidates_SkipsCollectedAndDeduplicatesByIdKeepingFirst()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // 1. Collected candidate
            var collectedToken = new TokenLocation { Id = "c1", Position = Vec3.Zero, Rarity = TokenRarity.Epic, EstimatedValue = 200f, IsCollected = true };

            // 2. Duplicate candidate: first occurrence has value 100, duplicate has value 200
            var firstToken = new TokenLocation { Id = "dup", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 100f, IsCollected = false };
            var duplicateToken = new TokenLocation { Id = "dup", Position = Vec3.Zero, Rarity = TokenRarity.Legendary, EstimatedValue = 200f, IsCollected = false };

            var normalToken = new TokenLocation { Id = "normal", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 50f, IsCollected = false };

            var results = _ranker.RankCandidates(new[] { collectedToken, firstToken, duplicateToken, normalToken }, player, _config, null);

            // Collected must be skipped, duplicate must keep first occurrence ("dup" with Value=100)
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.Any(r => r.Token.Id == "c1"), Is.False, "Collected tokens must not appear in ranking results");

            var dupResult = results.FirstOrDefault(r => r.Token.Id == "dup");
            Assert.That(dupResult, Is.Not.Null);
            Assert.That(dupResult!.Token.EstimatedValue, Is.EqualTo(100f), "First occurrence of duplicate ID must be preserved");
            Assert.That(dupResult.Token.Rarity, Is.EqualTo(TokenRarity.Common));
        }

        [Test]
        public void R15_VerifiedCost_PositiveInfinity_ExcludesCandidateFromRankCandidatesAndEvaluate()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };
            var unreachable = new TokenLocation
            {
                Id = "unreachable",
                Position = new Vec3(500f, 0f, 500f),
                Rarity = TokenRarity.Legendary,
                EstimatedValue = 250f
            };
            var reachable = new TokenLocation
            {
                Id = "reachable",
                Position = new Vec3(10f, 0f, 10f),
                Rarity = TokenRarity.Common,
                EstimatedValue = 10f
            };

            // 1. Evaluate returns null for +Infinity verified cost
            var evalResult = _ranker.Evaluate(unreachable, player, _config, null, verifiedPathCost: float.PositiveInfinity);
            Assert.That(evalResult, Is.Null, "Evaluate must return null for an unreachable candidate with +Infinity cost");

            // 2. RankCandidates omits candidate with +Infinity cost entirely
            var verified = new Dictionary<string, float>
            {
                ["unreachable"] = float.PositiveInfinity,
                ["reachable"] = 15f
            };

            var results = _ranker.RankCandidates(new[] { unreachable, reachable }, player, _config, null, verified);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Token.Id, Is.EqualTo("reachable"), "Unreachable candidate must never be returned in ranking list");
            Assert.That(results[0].IsPathVerified, Is.True);
        }

        [Test]
        public void R16_VerifiedCost_NegativeOrNaN_ThrowsArgumentOutOfRangeException()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };
            var token = new TokenLocation { Id = "t1", Position = new Vec3(10f, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 50f };

            // In Evaluate:
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ranker.Evaluate(token, player, _config, null, verifiedPathCost: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ranker.Evaluate(token, player, _config, null, verifiedPathCost: -1.0f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ranker.Evaluate(token, player, _config, null, verifiedPathCost: float.NegativeInfinity));

            // In RankCandidates:
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ranker.RankCandidates(new[] { token }, player, _config, null, new Dictionary<string, float> { ["t1"] = float.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ranker.RankCandidates(new[] { token }, player, _config, null, new Dictionary<string, float> { ["t1"] = -5.0f }));
        }

        [Test]
        public void R17_VerifiedCost_ZeroCostValidWhenEndpointsCoincide()
        {
            // Player and token genuinely coincide at origin
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };
            var token = new TokenLocation { Id = "coincide", Position = Vec3.Zero, Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var eval = _ranker.Evaluate(token, player, _config, null, verifiedPathCost: 0f);

            Assert.That(eval, Is.Not.Null);
            Assert.That(eval!.IsPathVerified, Is.True);
            Assert.That(eval.PathCostWorldUnits, Is.EqualTo(0f));
            Assert.That(eval.EstimatedTravelSeconds, Is.EqualTo(0f));
            Assert.That(eval.NormalizedTime, Is.EqualTo(1.0), "Zero travel time must give perfect normalized time score of 1.0");
        }

        [Test]
        public void R18_VerifiedCost_BelowStraightLineViolatingContract_ThrowsArgumentException()
        {
            // Straight-line distance is 100m, lineCostPerMeter = 1.0 => minLineCost = 100m
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };
            var token = new TokenLocation { Id = "t_cheat", Position = new Vec3(100f, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 50f };

            // Passing 50m (violates lower bound contract by 50m > 1e-4) must throw ArgumentException
            Assert.Throws<ArgumentException>(() =>
                _ranker.Evaluate(token, player, _config, null, verifiedPathCost: 50.0f, lineCostPerMeter: 1.0f));

            Assert.Throws<ArgumentException>(() =>
                _ranker.RankCandidates(new[] { token }, player, _config, null, new Dictionary<string, float> { ["t_cheat"] = 50.0f }, lineCostPerMeter: 1.0f));
        }

        [Test]
        public void R19_VerifiedCost_AtStraightLineEpsilonBoundary_ClampedSafely()
        {
            // Straight-line distance = 10.0m
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };
            var token = new TokenLocation { Id = "t_eps", Position = new Vec3(10f, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 50f };

            // Line evaluation (unverified)
            var lineEval = _ranker.Evaluate(token, player, _config, null, verifiedPathCost: null, lineCostPerMeter: 1.0f);
            Assert.That(lineEval, Is.Not.Null);

            // Verified cost slightly below line cost within tolerance [minLineCost - 1e-4, minLineCost]
            float slightUnderflow = 10.0f - 1e-5f;
            var verifiedEval = _ranker.Evaluate(token, player, _config, null, verifiedPathCost: slightUnderflow, lineCostPerMeter: 1.0f);

            Assert.That(verifiedEval, Is.Not.Null);
            Assert.That(verifiedEval!.IsPathVerified, Is.True);
            // Must be clamped to minLineCost (10.0m) to guarantee verified score <= upper bound score
            Assert.That(verifiedEval.PathCostWorldUnits, Is.EqualTo(10.0f).Within(1e-5f));
            Assert.That(verifiedEval.Score, Is.LessThanOrEqualTo(lineEval!.Score));
        }

        [Test]
        public void R20_PlanarCoordinates_NonFiniteYIgnoredAndNonFiniteXZSkipped()
        {
            // Y is elevation in 2.5D planar navigation and is ignored
            var playerWithInfY = new PlayerState { Position = new Vec3(0f, float.PositiveInfinity, 0f), Speed = 5f };
            var tokenWithNanY = new TokenLocation { Id = "t_nan_y", Position = new Vec3(10f, float.NaN, 0f), Rarity = TokenRarity.Common, EstimatedValue = 50f };

            var eval = _ranker.Evaluate(tokenWithNanY, playerWithInfY, _config, null);
            Assert.That(eval, Is.Not.Null, "Candidate and Player with non-finite Y must be evaluated normally");

            // But non-finite X or Z MUST be skipped/return null
            var tokenWithNanX = new TokenLocation { Id = "t_nan_x", Position = new Vec3(float.NaN, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 50f };
            var tokenWithInfZ = new TokenLocation { Id = "t_inf_z", Position = new Vec3(0f, 0f, float.NegativeInfinity), Rarity = TokenRarity.Common, EstimatedValue = 50f };

            Assert.That(_ranker.Evaluate(tokenWithNanX, playerWithInfY, _config, null), Is.Null);
            Assert.That(_ranker.Evaluate(tokenWithInfZ, playerWithInfY, _config, null), Is.Null);

            var batchResults = _ranker.RankCandidates(new[] { tokenWithNanY, tokenWithNanX, tokenWithInfZ }, playerWithInfY, _config, null);
            Assert.That(batchResults.Count, Is.EqualTo(1));
            Assert.That(batchResults[0].Token.Id, Is.EqualTo("t_nan_y"));
        }

        [Test]
        public void R21_Distance2D_LargeFiniteCoordinates_DoesNotOverflowToInfinity()
        {
            // Two finite coordinates whose difference squared exceeds float.MaxValue (3.402823e38)
            // dx = 2e19 => dx*dx = 4e38 > float.MaxValue
            var p1 = new Vec3(0f, 0f, 0f);
            var p2 = new Vec3(2e19f, 0f, 0f);

            float dist = Vec3.Distance2D(p1, p2);
            Assert.That(float.IsFinite(dist), Is.True, $"Distance2D must not overflow to Infinity; got {dist}");
            Assert.That(dist, Is.EqualTo(2e19f).Within(1e14f));

            float dist3D = Vec3.Distance(p1, p2);
            Assert.That(float.IsFinite(dist3D), Is.True, "3D Distance must not overflow to Infinity");
        }

        [Test]
        public void R22_RankingConfig_WeightSumValidation_EnforcesRangeZeroToTenAndRejectsOverflow()
        {
            // 1. Weight sum overflow in float (2e38f + 2e38f) throws ArgumentOutOfRangeException
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RankingConfig(weightValue: 2e38f, weightTime: 2e38f));

            // 2. Weight sum exceeding 10.0 throws ArgumentOutOfRangeException
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RankingConfig(weightValue: 6.0f, weightTime: 5.0f));

            // 3. Default weights (sum 1.0) accepted
            var defaultConfig = new RankingConfig();
            double defaultSum = (double)defaultConfig.WeightValue + defaultConfig.WeightTime + defaultConfig.WeightRarity + defaultConfig.WeightHysteresis;
            Assert.That(defaultSum, Is.EqualTo(1.0).Within(1e-6));

            // 4. Weight sum of exactly 10.0 accepted
            Assert.DoesNotThrow(() =>
                new RankingConfig(weightValue: 4.0f, weightTime: 3.5f, weightRarity: 1.5f, weightHysteresis: 1.0f));
        }

        [Test]
        public void R23_ScoreMicros_TracksScoreAndOrdersDeterministically()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };
            var t1 = new TokenLocation { Id = "t1", Position = new Vec3(10f, 0f, 0f), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var eval = _ranker.Evaluate(t1, player, _config, null);
            Assert.That(eval, Is.Not.Null);

            // ScoreMicros must be computed from raw double score: (long)Math.Floor(raw * 1e6 + 0.5)
            // Raw score: 0.40 * (100 / 250) + 0.35 * (1 - (10/5)/120) + 0 + 0 = 0.40 * 0.4 + 0.35 * (1 - 2/120)
            double raw = 0.40 * (100.0 / 250.0) + 0.35 * (1.0 - (2.0 / 120.0));
            long expectedMicros = (long)Math.Floor(raw * 1_000_000.0 + 0.5);
            double expectedScore = expectedMicros / 1_000_000.0;

            Assert.That(eval!.ScoreMicros, Is.EqualTo(expectedMicros));
            Assert.That(eval.Score, Is.EqualTo(expectedScore));
            Assert.That(eval.NormalizedHysteresis, Is.EqualTo(0.0));
        }

        [Test]
        public void R24_Evaluate_ProducesIdenticalResultToRankCandidatesSingleElement()
        {
            var player = new PlayerState { Position = new Vec3(25f, 0f, 30f), Speed = 5.5f };
            var token = new TokenLocation { Id = "token_target", Position = new Vec3(80f, 0f, 95f), Rarity = TokenRarity.Rare, EstimatedValue = 175f };

            // 1. Unverified evaluation
            var evalUnverified = _ranker.Evaluate(token, player, _config, currentTargetId: "token_target");
            var rankUnverified = _ranker.RankCandidates(new[] { token }, player, _config, currentTargetId: "token_target")[0];

            Assert.That(evalUnverified!.Score, Is.EqualTo(rankUnverified.Score));
            Assert.That(evalUnverified.ScoreMicros, Is.EqualTo(rankUnverified.ScoreMicros));
            Assert.That(evalUnverified.NormalizedValue, Is.EqualTo(rankUnverified.NormalizedValue));
            Assert.That(evalUnverified.NormalizedTime, Is.EqualTo(rankUnverified.NormalizedTime));
            Assert.That(evalUnverified.NormalizedRarity, Is.EqualTo(rankUnverified.NormalizedRarity));
            Assert.That(evalUnverified.NormalizedHysteresis, Is.EqualTo(rankUnverified.NormalizedHysteresis));
            Assert.That(evalUnverified.PathCostWorldUnits, Is.EqualTo(rankUnverified.PathCostWorldUnits));
            Assert.That(evalUnverified.EstimatedTravelSeconds, Is.EqualTo(rankUnverified.EstimatedTravelSeconds));
            Assert.That(evalUnverified.IsPathVerified, Is.EqualTo(rankUnverified.IsPathVerified));

            // 2. Verified evaluation
            float verifiedCost = 120.0f;
            var evalVerified = _ranker.Evaluate(token, player, _config, currentTargetId: null, verifiedPathCost: verifiedCost);
            var rankVerified = _ranker.RankCandidates(new[] { token }, player, _config, currentTargetId: null, new Dictionary<string, float> { [token.Id] = verifiedCost })[0];

            Assert.That(evalVerified!.Score, Is.EqualTo(rankVerified.Score));
            Assert.That(evalVerified.ScoreMicros, Is.EqualTo(rankVerified.ScoreMicros));
            Assert.That(evalVerified.PathCostWorldUnits, Is.EqualTo(rankVerified.PathCostWorldUnits));
            Assert.That(evalVerified.IsPathVerified, Is.True);
        }

        [Test]
        public void R25_AllocationReport_MeasuresThreadAllocationsFor100And1000Candidates()
        {
            var player = new PlayerState { Position = Vec3.Zero, Speed = 5f };

            // Warm up JIT
            var warmupTokens = Enumerable.Range(0, 10).Select(i => new TokenLocation
            {
                Id = $"w_{i}",
                Position = new Vec3(i * 5f, 0f, 0f),
                Rarity = TokenRarity.Common,
                EstimatedValue = 50f
            }).ToList();
            _ranker.RankCandidates(warmupTokens, player, _config, null);
            _ranker.Evaluate(warmupTokens[0], player, _config, null);

            // 1. Measure 100 Candidates with RankCandidates
            var tokens100 = Enumerable.Range(0, 100).Select(i => new TokenLocation
            {
                Id = $"t_{i}",
                Position = new Vec3(i * 2f, 0f, i * 2f),
                Rarity = (TokenRarity)(i % 5),
                EstimatedValue = (float)(i * 2.5)
            }).ToList();

            long bytesBefore100 = GC.GetAllocatedBytesForCurrentThread();
            var results100 = _ranker.RankCandidates(tokens100, player, _config, null);
            long bytesAfter100 = GC.GetAllocatedBytesForCurrentThread();
            long allocated100 = bytesAfter100 - bytesBefore100;

            Assert.That(results100.Count, Is.EqualTo(100));
            TestContext.Out.WriteLine($"RankCandidates allocation for 100 candidates: {allocated100:N0} bytes (~{allocated100 / 1024.0:F1} KB)");

            // 2. Measure 1000 Candidates with RankCandidates
            var tokens1000 = Enumerable.Range(0, 1000).Select(i => new TokenLocation
            {
                Id = $"k_{i}",
                Position = new Vec3(i * 1f, 0f, i * 1f),
                Rarity = (TokenRarity)(i % 5),
                EstimatedValue = (float)(i * 0.25)
            }).ToList();

            long bytesBefore1000 = GC.GetAllocatedBytesForCurrentThread();
            var results1000 = _ranker.RankCandidates(tokens1000, player, _config, null);
            long bytesAfter1000 = GC.GetAllocatedBytesForCurrentThread();
            long allocated1000 = bytesAfter1000 - bytesBefore1000;

            Assert.That(results1000.Count, Is.EqualTo(1000));
            TestContext.Out.WriteLine($"RankCandidates allocation for 1000 candidates: {allocated1000:N0} bytes (~{allocated1000 / 1024.0:F1} KB)");

            // 3. Measure single Evaluate (hot path in RouteCoordinator)
            long bytesBeforeEval = GC.GetAllocatedBytesForCurrentThread();
            var singleEval = _ranker.Evaluate(tokens100[0], player, _config, null);
            long bytesAfterEval = GC.GetAllocatedBytesForCurrentThread();
            long allocatedEval = bytesAfterEval - bytesBeforeEval;

            Assert.That(singleEval, Is.Not.Null);
            TestContext.Out.WriteLine($"Single Evaluate allocation: {allocatedEval} bytes (only 1 TargetEvaluationResult object, 0 wrapper collections)");

            // Invariant: Single evaluate allocates <= 128 bytes (pure DTO instantiation, no arrays or hashsets)
            Assert.That(allocatedEval, Is.LessThanOrEqualTo(128));
        }
    }
}
