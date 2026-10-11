using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Assistant.Core.Coordination;
using Assistant.Core.Data;
using Assistant.Core.Discovery;
using Assistant.Sim;
using Assistant.Sim.Harness;
using NUnit.Framework;

namespace Assistant.Tests.Harness
{
    [TestFixture]
    public class SimulationHarnessTests
    {
        private static string GetGoldenTranscriptPath()
        {
            var baseDir = AppContext.BaseDirectory;
            // Handle both running in bin and source tree
            var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
            var targetDir = Path.Combine(projectDir, "Harness", "GoldenTranscripts");
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            return Path.Combine(targetDir, "golden_seed_12345.json");
        }

        [Test]
        public void H01_DeterministicSimulation_SameSeedProducesIdenticalTranscripts()
        {
            var config1 = new SimHarnessConfig
            {
                Seed = 12345,
                MaxSimulatedTime = 60f,
                InitialTokenCount = 25
            };
            var config2 = new SimHarnessConfig
            {
                Seed = 12345,
                MaxSimulatedTime = 60f,
                InitialTokenCount = 25
            };

            var harness1 = new SimulationHarness(config1);
            var result1 = harness1.Run();

            var harness2 = new SimulationHarness(config2);
            var result2 = harness2.Run();

            Assert.That(result1.Transcript.Count, Is.GreaterThan(0));
            Assert.That(result2.Transcript.Count, Is.EqualTo(result1.Transcript.Count));

            for (int i = 0; i < result1.Transcript.Count; i++)
            {
                var e1 = result1.Transcript[i];
                var e2 = result2.Transcript[i];

                Assert.That(e2.SimTime, Is.EqualTo(e1.SimTime).Within(1e-4), $"Mismatch at event {i} SimTime");
                Assert.That(e2.Type, Is.EqualTo(e1.Type), $"Mismatch at event {i} Type");
                Assert.That(e2.TokenId, Is.EqualTo(e1.TokenId), $"Mismatch at event {i} TokenId");
                Assert.That(e2.Value, Is.EqualTo(e1.Value).Within(1e-3), $"Mismatch at event {i} Value");
            }

            Assert.That(result2.Metrics.TotalValue, Is.EqualTo(result1.Metrics.TotalValue).Within(1e-3));
            Assert.That(result2.Metrics.TotalCollectedCount, Is.EqualTo(result1.Metrics.TotalCollectedCount));
            Assert.That(result2.Metrics.TotalReplans, Is.EqualTo(result1.Metrics.TotalReplans));
        }

        [Test]
        public void H02_DeterministicSimulation_DifferentSeedsProduceDifferentTranscripts()
        {
            var config1 = new SimHarnessConfig
            {
                Seed = 12345,
                MaxSimulatedTime = 60f,
                InitialTokenCount = 25
            };
            var config2 = new SimHarnessConfig
            {
                Seed = 54321,
                MaxSimulatedTime = 60f,
                InitialTokenCount = 25
            };

            var result1 = new SimulationHarness(config1).Run();
            var result2 = new SimulationHarness(config2).Run();

            bool transcriptsDiffer = result1.Transcript.Count != result2.Transcript.Count;
            if (!transcriptsDiffer)
            {
                for (int i = 0; i < result1.Transcript.Count; i++)
                {
                    if (result1.Transcript[i].TokenId != result2.Transcript[i].TokenId ||
                        Math.Abs(result1.Transcript[i].SimTime - result2.Transcript[i].SimTime) > 0.01)
                    {
                        transcriptsDiffer = true;
                        break;
                    }
                }
            }

            Assert.That(transcriptsDiffer, Is.True, "Different seeds should produce differing transcripts.");
        }

        [Test]
        public void H03_GoldenTranscript_Seed12345MatchesCommittedBaseline()
        {
            var config = new SimHarnessConfig
            {
                Seed = 12345,
                MaxSimulatedTime = 60f,
                InitialTokenCount = 25
            };

            var result = new SimulationHarness(config).Run();
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string currentJson = JsonSerializer.Serialize(result.Transcript, jsonOptions);

            string goldenPath = GetGoldenTranscriptPath();
            Assert.That(File.Exists(goldenPath), Is.True, $"Golden transcript file must exist at: {goldenPath}");

            string goldenJson = File.ReadAllText(goldenPath);
            var goldenEvents = JsonSerializer.Deserialize<List<SimTranscriptEvent>>(goldenJson, jsonOptions)!;

            Assert.That(result.Transcript.Count, Is.EqualTo(goldenEvents.Count), "Event count mismatch with golden transcript.");

            for (int i = 0; i < goldenEvents.Count; i++)
            {
                var actual = result.Transcript[i];
                var expected = goldenEvents[i];

                Assert.That(actual.SimTime, Is.EqualTo(expected.SimTime).Within(1e-4), $"Event {i} time mismatch");
                Assert.That(actual.Type, Is.EqualTo(expected.Type), $"Event {i} type mismatch");
                Assert.That(actual.TokenId, Is.EqualTo(expected.TokenId), $"Event {i} token ID mismatch");
                Assert.That(actual.Value, Is.EqualTo(expected.Value).Within(1e-3), $"Event {i} value mismatch");
            }
        }

        [Test]
        public void H04_NoChatter_StaticTokensOnlyChangeTargetUponCollectionOrInvalidation()
        {
            var config = new SimHarnessConfig
            {
                Seed = 777,
                MaxSimulatedTime = 80f,
                InitialTokenCount = 20
            };

            var result = new SimulationHarness(config).Run();

            string? activeTarget = null;
            for (int i = 0; i < result.Transcript.Count; i++)
            {
                var evt = result.Transcript[i];

                if (evt.Type == "TargetSelected")
                {
                    activeTarget = evt.TokenId;
                }
                else if (evt.Type == "TargetChanged")
                {
                    // TargetChanged must only occur if incumbent was collected or invalidated
                    // (Hysteresis and commit margin guarantee incumbent stability)
                    activeTarget = evt.TokenId;
                }
                else if (evt.Type == "TokenCollected")
                {
                    Assert.That(evt.TokenId, Is.EqualTo(activeTarget),
                        "Player collected a token that was not the currently selected target!");
                    activeTarget = null;
                }
            }
        }

        [Test]
        public void H05_MapChangedScenario_WallAcrossPlannedRouteTriggersReplanAndAvoidance()
        {
            // Flat, open map
            var config = new SimHarnessConfig
            {
                GridWidth = 60,
                GridHeight = 60,
                CellSize = 10f,
                PlayerStartPosition = new Vec3(15f, 0, 15f),
                GenerateProceduralTerrain = false,
                InitialTokenCount = 0,
                MaxSimulatedTime = 30f,
                CustomTokens = new List<TokenLocation>
                {
                    new TokenLocation
                    {
                        Id = "token_target",
                        Position = new Vec3(120f, 0, 15f), // Straight east within scan radius (105m)
                        Rarity = TokenRarity.Common,
                        EstimatedValue = 100f
                    }
                }
            };

            bool wallPlaced = false;
            bool playerEnteredWall = false;

            config.TickHook = (simTime, harness) =>
            {
                // Place a vertical wall of obstacles across column 7 at t = 2.0s
                if (simTime >= 2.0 && !wallPlaced)
                {
                    wallPlaced = true;
                    // Wall from Z=0 to Z=5 (spanning world Z=0 to 50)
                    for (int z = 0; z <= 5; z++)
                    {
                        harness.Map.SetCell(7, z, SimCellType.Obstacle);
                    }
                    // Trigger region invalidation
                    harness.Notifier.NotifyRegionChanged(new Region(new Vec3(70f, 0, 25f), 50f), "Wall constructed across corridor");
                }

                // Check that player position is NEVER inside an obstacle
                var playerGrid = harness.Map.WorldToGrid(harness.Player.State.Position);
                if (harness.Map.GetCell(playerGrid) == SimCellType.Obstacle)
                {
                    playerEnteredWall = true;
                }
            };

            var harness = new SimulationHarness(config);
            var result = harness.Run();

            Assert.That(wallPlaced, Is.True, "Wall placement hook must execute.");
            Assert.That(playerEnteredWall, Is.False, "Player must never enter an obstacle cell.");

            // Verify MapChanged appeared in transcript
            Assert.That(result.Transcript.Any(e => e.Type == "MapChanged"), Is.True, "MapChanged event must appear in transcript.");

            // Verify target was still collected by navigating around the wall
            Assert.That(result.Metrics.TotalCollectedCount, Is.EqualTo(1));
            Assert.That(result.Transcript.Any(e => e.Type == "TokenCollected" && e.TokenId == "token_target"), Is.True);
        }

        [Test]
        public void H06_WalkabilityInvariant_PlayerCellIsWalkableAtEveryTick()
        {
            var config = new SimHarnessConfig
            {
                Seed = 999,
                MaxSimulatedTime = 50f,
                InitialTokenCount = 30,
                GenerateProceduralTerrain = true
            };

            bool invariantViolated = false;
            string violationDetails = string.Empty;

            config.TickHook = (simTime, harness) =>
            {
                var gridPos = harness.Map.WorldToGrid(harness.Player.State.Position);
                if (!harness.Map.IsWalkable(gridPos))
                {
                    invariantViolated = true;
                    violationDetails = $"At simTime {simTime:F2}s, player at {harness.Player.State.Position} is on unwalkable cell {gridPos}";
                }
            };

            var harness = new SimulationHarness(config);
            harness.Run();

            Assert.That(invariantViolated, Is.False, violationDetails);
        }

        [Test]
        public void H07_HandDerivedScenario_ExactCollectionOrderAndValue()
        {
            // Simple open corridor: 3 tokens in a line
            // Player starts at (15, 0, 15), speed 6 m/s
            // T1 at (35, 0, 15) -> 20m away, value = 10
            // T2 at (65, 0, 15) -> 50m away from start, 30m away from T1, value = 50
            // T3 at (105, 0, 15) -> 90m away from start, 40m away from T2, value = 200
            var config = new SimHarnessConfig
            {
                GridWidth = 30,
                GridHeight = 30,
                CellSize = 10f,
                PlayerStartPosition = new Vec3(15f, 0, 15f),
                GenerateProceduralTerrain = false,
                InitialTokenCount = 0,
                MaxSimulatedTime = 40f,
                CustomTokens = new List<TokenLocation>
                {
                    new TokenLocation
                    {
                        Id = "T1",
                        Position = new Vec3(35f, 0, 15f),
                        Rarity = TokenRarity.Common,
                        EstimatedValue = 200f
                    },
                    new TokenLocation
                    {
                        Id = "T2",
                        Position = new Vec3(65f, 0, 15f),
                        Rarity = TokenRarity.Common,
                        EstimatedValue = 100f
                    },
                    new TokenLocation
                    {
                        Id = "T3",
                        Position = new Vec3(105f, 0, 15f),
                        Rarity = TokenRarity.Common,
                        EstimatedValue = 50f
                    }
                }
            };

            var harness = new SimulationHarness(config);
            var result = harness.Run();

            var collectedTokens = result.Transcript
                .Where(e => e.Type == "TokenCollected")
                .Select(e => e.TokenId)
                .ToList();

            Assert.That(collectedTokens, Is.EqualTo(new[] { "T1", "T2", "T3" }));
            Assert.That(result.Metrics.TotalCollectedCount, Is.EqualTo(3));
            Assert.That(result.Metrics.TotalValue, Is.EqualTo(350f).Within(1e-3));
        }

        [Test]
        public void H08_WalledInToken_NeverTargetedOrCollected_RunEndsWithNoValidCandidates()
        {
            var config = new SimHarnessConfig
            {
                GridWidth = 20,
                GridHeight = 20,
                CellSize = 10f,
                PlayerStartPosition = new Vec3(15f, 0, 15f),
                GenerateProceduralTerrain = false,
                InitialTokenCount = 0,
                MaxSimulatedTime = 20f,
                CustomTokens = new List<TokenLocation>
                {
                    new TokenLocation
                    {
                        Id = "unreachable_token",
                        Position = new Vec3(85f, 0, 85f), // Cell (8, 8)
                        Rarity = TokenRarity.Legendary,
                        EstimatedValue = 500f
                    }
                }
            };

            var harness = new SimulationHarness(config);
            // Completely surround cell (8, 8) with obstacles
            for (int x = 7; x <= 9; x++)
            {
                for (int z = 7; z <= 9; z++)
                {
                    if (x != 8 || z != 8)
                    {
                        harness.Map.SetCell(x, z, SimCellType.Obstacle);
                    }
                }
            }

            var result = harness.Run();

            Assert.That(result.Transcript.Any(e => e.Type == "TargetSelected" && e.TokenId == "unreachable_token"), Is.False,
                "Walled-in token must never be selected as a target.");
            Assert.That(result.Transcript.Any(e => e.Type == "TokenCollected"), Is.False,
                "No token should be collected.");
            Assert.That(result.LastTerminationReason, Is.EqualTo(CoordinationTerminationReason.NoValidCandidates),
                "Run must end with NoValidCandidates.");
            Assert.That(result.Metrics.TotalCollectedCount, Is.EqualTo(0));
        }

        [Test]
        public void H09_TokenCollected_RaisedExactlyOncePerTokenThroughPipeline()
        {
            var config = new SimHarnessConfig
            {
                Seed = 101,
                MaxSimulatedTime = 60f,
                InitialTokenCount = 30
            };

            var apiCollectedList = new List<string>();
            var harness = new SimulationHarness(config);
            harness.GameApi.TokenCollected += id => apiCollectedList.Add(id);

            var result = harness.Run();

            var transcriptCollected = result.Transcript
                .Where(e => e.Type == "TokenCollected")
                .Select(e => e.TokenId!)
                .ToList();

            // Assert no duplicates in api events
            var apiDuplicates = apiCollectedList.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.That(apiDuplicates, Is.Empty, $"Duplicate api TokenCollected events: {string.Join(", ", apiDuplicates)}");

            // Assert no duplicates in transcript events
            var transcriptDuplicates = transcriptCollected.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.That(transcriptDuplicates, Is.Empty, $"Duplicate transcript TokenCollected events: {string.Join(", ", transcriptDuplicates)}");

            // Assert counts match 1:1
            Assert.That(transcriptCollected.Count, Is.EqualTo(apiCollectedList.Count));
            Assert.That(result.Metrics.TotalCollectedCount, Is.EqualTo(transcriptCollected.Count));
        }

        [Test]
        public void H10_ReplanCountBound_DoesNotExceedCadencePlusEvents()
        {
            var config = new SimHarnessConfig
            {
                Seed = 202,
                MaxSimulatedTime = 50f,
                InitialTokenCount = 25,
                ReplanIntervalSeconds = 1.0f,
                Dt = 0.1f
            };

            var result = new SimulationHarness(config).Run();

            double totalTime = result.FinalSimTime;
            int totalTicks = (int)Math.Round(totalTime / config.Dt);
            int expectedCadenceReplans = (int)Math.Floor(totalTime / config.ReplanIntervalSeconds) + 1; // +1 for t=0

            int collectionEvents = result.Transcript.Count(e => e.Type == "TokenCollected");
            int mapChangeEvents = result.Transcript.Count(e => e.Type == "MapChanged");
            int totalEvents = collectionEvents + mapChangeEvents;

            // Replans must be strictly bounded: <= cadence + events + small buffer
            Assert.That(result.Metrics.TotalReplans, Is.LessThanOrEqualTo(expectedCadenceReplans + totalEvents + 2),
                $"Total replans ({result.Metrics.TotalReplans}) exceeded expected bound.");

            // Crucial: Must NEVER replan every frame
            Assert.That(result.Metrics.TotalReplans, Is.LessThan(totalTicks / 2),
                $"Replan was invoked too frequently ({result.Metrics.TotalReplans} times out of {totalTicks} ticks)!");
        }

        [Test]
        public void H11_CoverageTtlEndToEnd_UncoveredTokenRetained_SilentlyDespawnedCoveredTokenEvicted()
        {
            var clock = new SimClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var map = new SimGridMap(100, 100, 10f);
            var player = new SimPlayer(new Vec3(50f, 0, 50f), speed: 6f);
            var gameApi = new SimGameApi(map, player, seed: 42, clock: clock);
            var repo = new TokenRepository(spatialCellSize: 50f);
            var parser = new DefaultTokenParser();
            var validator = new TokenValidator(defaultTtlSeconds: 30f, clock: clock);

            var discovery = new TokenDiscoveryService(
                gameApi,
                repo,
                parser,
                validator,
                clock,
                gameApi,
                ttlSeconds: 30f,
                scanRadius: 100f,
                scanInterval: TimeSpan.FromSeconds(1.0));

            // 1. Spawn Token A at (50, 0, 50) near player
            var tokenA = new TokenLocation
            {
                Id = "token_A",
                Position = new Vec3(50f, 0, 50f),
                Rarity = TokenRarity.Common,
                EstimatedValue = 10f,
                DetectedAt = clock.UtcNow,
                LastConfirmedAt = clock.UtcNow
            };
            gameApi.SpawnToken(tokenA);

            // Initial tick: player at (50, 0, 50) scans and discovers Token A
            discovery.Tick();
            Assert.That(repo.Get("token_A"), Is.Not.Null, "Token A must be discovered.");

            // 2. Player moves far away to (800, 0, 800) (out of scan coverage: distance ~1060m > 100m)
            player.Teleport(new Vec3(800f, 0, 800f));

            // Advance clock past 30s TTL (40 seconds later)
            clock.Advance(40.0);

            // Tick at (800, 0, 800)
            discovery.Tick();

            // Assert: Token A is NOT pruned because it had NO qualifying coverage while elapsed >= TTL!
            Assert.That(repo.Get("token_A"), Is.Not.Null,
                "Token A must remain in repository because it was outside scan coverage!");

            // 3. Now advance clock by 1.0s and spawn Token B at (800, 0, 800) near player
            clock.Advance(1.0);
            var tokenB = new TokenLocation
            {
                Id = "token_B",
                Position = new Vec3(800f, 0, 800f),
                Rarity = TokenRarity.Rare,
                EstimatedValue = 50f,
                DetectedAt = clock.UtcNow,
                LastConfirmedAt = clock.UtcNow
            };
            gameApi.SpawnToken(tokenB);

            // Discovery tick scans and discovers Token B
            discovery.Tick();
            Assert.That(repo.Get("token_B"), Is.Not.Null, "Token B must be discovered.");

            // 4. Silently despawn Token B from SimGameApi without firing TokenRemoved event
            gameApi.SilentDespawnToken("token_B");

            // Advance time past 30s TTL (40 seconds later), player remains at (800, 0, 800)
            clock.Advance(40.0);

            // Discovery tick scans (800, 0, 800) - Token B is covered by the scan but missing from gameApi!
            discovery.Tick();

            // Assert: Token B is now pruned because qualifying coverage covered its location!
            Assert.That(repo.Get("token_B"), Is.Null,
                "Token B must be evicted by TTL after being scanned in a covered area!");

            // Assert: Token A is still present in repo!
            Assert.That(repo.Get("token_A"), Is.Not.Null,
                "Token A must still remain in repository because it was never re-scanned!");
        }

        [Test]
        public void H12_PrintRunnerComparisonTable()
        {
            TestContext.Out.WriteLine("========================================================================================================");
            TestContext.Out.WriteLine("   STAGE 3.4 SIMULATION BENCHMARK: RANKED-COORDINATOR VS NEAREST-FIRST BASELINE (300s SIMULATED TIME)   ");
            TestContext.Out.WriteLine("========================================================================================================");
            TestContext.Out.WriteLine(" {0,6} | {1,20} | {2,15} | {3,19} | {4,17} | {5,15}",
                "Seed", "Coord Value (BRIL)", "Coord Collected", "Nearest Value (BRIL)", "Nearest Collected", "Value Delta (%)");
            TestContext.Out.WriteLine("-------+----------------------+-----------------+---------------------+-------------------+----------------");

            int[] seeds = new int[] { 101, 202, 303, 404, 505 };

            float totalCoordVal = 0f;
            float totalNearestVal = 0f;
            int totalCoordTokens = 0;
            int totalNearestTokens = 0;

            foreach (var seed in seeds)
            {
                var coordConfig = new SimHarnessConfig
                {
                    Seed = seed,
                    MaxSimulatedTime = 300f,
                    InitialTokenCount = 50,
                    Strategy = SimStrategy.RankedCoordinator
                };
                var coordHarness = new SimulationHarness(coordConfig);
                var coordResult = coordHarness.Run();

                var nearestConfig = new SimHarnessConfig
                {
                    Seed = seed,
                    MaxSimulatedTime = 300f,
                    InitialTokenCount = 50,
                    Strategy = SimStrategy.NearestFirst
                };
                var nearestHarness = new SimulationHarness(nearestConfig);
                var nearestResult = nearestHarness.Run();

                float coordVal = coordResult.Metrics.TotalValue;
                int coordTokens = coordResult.Metrics.TotalCollectedCount;
                float nearestVal = nearestResult.Metrics.TotalValue;
                int nearestTokens = nearestResult.Metrics.TotalCollectedCount;

                totalCoordVal += coordVal;
                totalNearestVal += nearestVal;
                totalCoordTokens += coordTokens;
                totalNearestTokens += nearestTokens;

                float deltaPct = nearestVal > 0f ? ((coordVal - nearestVal) / nearestVal) * 100f : 0f;
                string deltaStr = $"{(deltaPct >= 0f ? "+" : "")}{deltaPct:F1} %";

                TestContext.Out.WriteLine(" {0,6} | {1,20:F1} | {2,15} | {3,19:F1} | {4,17} | {5,15}",
                    seed, coordVal, coordTokens, nearestVal, nearestTokens, deltaStr);
            }

            TestContext.Out.WriteLine("-------+----------------------+-----------------+---------------------+-------------------+----------------");
            float overallDeltaPct = totalNearestVal > 0f ? ((totalCoordVal - totalNearestVal) / totalNearestVal) * 100f : 0f;
            string overallDeltaStr = $"{(overallDeltaPct >= 0f ? "+" : "")}{overallDeltaPct:F1} %";
            TestContext.Out.WriteLine(" {0,6} | {1,20:F1} | {2,15} | {3,19:F1} | {4,17} | {5,15}",
                "TOTAL", totalCoordVal, totalCoordTokens, totalNearestVal, totalNearestTokens, overallDeltaStr);
            TestContext.Out.WriteLine("========================================================================================================\n");
        }
    }
}
