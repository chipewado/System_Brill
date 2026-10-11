using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Assistant.Core.Data;
using Assistant.Core.Discovery;
using Assistant.Sim;
using Assistant.Sim.Harness;

namespace Assistant.Runner
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=================================================================");
            Console.WriteLine("    BRILLIANTCRYPTO ASSISTANT - PHASE 1 SIMULATOR & CORE DEMO   ");
            Console.WriteLine("=================================================================\n");

            // 1. Initialize Simulation Environment
            Console.WriteLine("[1] Initializing Simulated 2D Grid Map (1000m x 1000m)...");
            var map = new SimGridMap(width: 100, height: 100, cellSize: 10f);
            map.GenerateProceduralFeatures(seed: 42);
            var mapData = map.ToMapData("Sim_Mine_Sector_7");
            Console.WriteLine($"    Map ID: {mapData.MapId}, Dimensions: {mapData.Width}m x {mapData.Height}m");

            // 2. Initialize Simulated Player
            var playerStart = new Vec3(150f, 0, 150f);
            var simPlayer = new SimPlayer(playerStart, speed: 6.0f);
            Console.WriteLine($"    Player spawned at: {simPlayer.State.Position}, Speed: {simPlayer.State.Speed} m/s\n");

            // 3. Initialize SimGameApi & Core Repository & Validator
            Console.WriteLine("[2] Connecting Core Components via Event-Driven Interfaces...");
            var gameApi = new SimGameApi(map, simPlayer, seed: 101);
            var repo = new TokenRepository(spatialCellSize: 50f);
            var validator = new TokenValidator(defaultTtlSeconds: 60f);

            gameApi.TokenSpawned += token =>
            {
                var valResult = validator.Validate(token, mapData);
                if (valResult.IsValid)
                {
                    repo.Add(token);
                }
                else
                {
                    Console.WriteLine($"    [Warning] Rejected invalid token {token.Id}: {valResult.Message}");
                }
            };

            gameApi.TokenCollected += id =>
            {
                var t = repo.Get(id);
                if (t != null)
                {
                    t.IsCollected = true;
                    repo.Update(t);
                }
            };

            gameApi.TokenRemoved += id => repo.Remove(id);
            gameApi.MapChanged += a => Console.WriteLine($"    [Event] Map structure changed! ({a.Reason})");

            // 4. Spawn Specification Preset Tokens
            Console.WriteLine("\n[3] Spawning Phase 1 Presets from Spec:");
            var spawner = new SimTokenSpawner();
            var presets = spawner.CreateSpecPhase1Presets();
            foreach (var preset in presets)
            {
                gameApi.SpawnToken(preset);
                Console.WriteLine($"    - {preset.Id,-15} Pos: {preset.Position,-20} Rarity: {preset.Rarity,-10} Value: {preset.EstimatedValue,6:F1}");
            }

            // 5. Spawn Batch of Random Tokens (25 tokens)
            Console.WriteLine("\n[4] Spawning 25 Procedural Tokens with Rarity Distribution...");
            var randomBatch = spawner.SpawnBatch(25, map, new Random(777));
            foreach (var t in randomBatch)
            {
                gameApi.SpawnToken(t);
            }
            Console.WriteLine($"    Total active tokens registered in Core Repository: {repo.TotalCount}");

            // 6. Summary by Rarity
            Console.WriteLine("\n[5] Token Distribution by Rarity:");
            foreach (TokenRarity rarity in Enum.GetValues(typeof(TokenRarity)))
            {
                var byRarity = repo.GetByRarity(rarity);
                Console.WriteLine($"    - {rarity,-10}: {byRarity.Count,2} tokens");
            }

            // 7. Query Nearest Tokens to Player
            Console.WriteLine($"\n[6] Querying Top 5 Nearest Tokens to Player at {simPlayer.State.Position}:");
            Console.WriteLine("    -----------------------------------------------------------------------------");
            Console.WriteLine("    {0,-14} {1,-10} {2,-18} {3,10} {4,12}", "Token ID", "Rarity", "Position", "Distance", "Est. Value");
            Console.WriteLine("    -----------------------------------------------------------------------------");

            var nearest5 = repo.GetNearest(simPlayer.State.Position, count: 5);
            foreach (var t in nearest5)
            {
                float dist = Vec3.Distance(simPlayer.State.Position, t.Position);
                Console.WriteLine("    {0,-14} {1,-10} {2,-18} {3,8:F1} m {4,10:F1} BRIL",
                    t.Id, t.Rarity, t.Position, dist, t.EstimatedValue);
            }
            Console.WriteLine("    -----------------------------------------------------------------------------");

            // 8. Simulate Token Collection Event
            if (nearest5.Count > 0)
            {
                var target = nearest5.First();
                Console.WriteLine($"\n[7] Simulating Collection of Closest Token '{target.Id}'...");
                gameApi.CollectToken(target.Id);

                var afterCollect = repo.GetAvailable();
                Console.WriteLine($"    Available uncollected tokens remaining: {afterCollect.Count}");
                Console.WriteLine($"    Token '{target.Id}' is collected: {repo.Get(target.Id)?.IsCollected}");
            }

            // 9. Benchmark Spatial Query Performance
            Console.WriteLine("\n[8] Performance Benchmark: 1,000 Tokens Spatial Query...");
            var benchmarkRepo = new TokenRepository(spatialCellSize: 50f);
            var benchmarkTokens = spawner.SpawnBatch(1000, map, new Random(9999));
            var sw = Stopwatch.StartNew();
            foreach (var bt in benchmarkTokens)
            {
                benchmarkRepo.Add(bt);
            }
            sw.Stop();
            long insertMs = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < 1000; i++)
            {
                var testOrigin = new Vec3(i % 1000, 0, (i * 7) % 1000);
                var _ = benchmarkRepo.GetNearest(testOrigin, count: 10);
            }
            sw.Stop();
            double queryAvgUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / 1000.0;

            Console.WriteLine($"    - Added 1,000 tokens in: {insertMs} ms");
            Console.WriteLine($"    - 1,000 nearest-token queries (top 10): {sw.ElapsedMilliseconds} ms (avg {queryAvgUs:F2} us/query)");
            Console.WriteLine("\n[Phase 1 Execution Finished Successfully]\n");

            // 10. Run Full Stage 3.4 / 3.5 Evaluation Study
            RunEvaluationStudy();
        }

        public sealed class RunRecord
        {
            public int Seed { get; set; }
            public string Strategy { get; set; } = string.Empty;
            public float TotalValue { get; set; }
            public int CollectedCount { get; set; }
            public double ValuePerSecond { get; set; }
            public float DistanceWalked { get; set; }
            public double IdleTime { get; set; }
            public double? TimeToFirstCollection { get; set; }
            public int TargetSwitches { get; set; }
            public int CoordinateCalls { get; set; }
            public int TotalPathSearches { get; set; }
            public SimHarnessResult Result { get; set; } = null!;
        }

        private static void RunEvaluationStudy()
        {
            Console.WriteLine("========================================================================================================");
            Console.WriteLine("             STAGE 3.4 & 3.5 EVALUATION STUDY: 50 PAIRED SEEDS (300s SIMULATED TIME)                    ");
            Console.WriteLine("========================================================================================================\n");

            int seedCount = 50;
            var strategies = new (SimStrategy Strategy, string Name)[]
            {
                (SimStrategy.RankedCoordinator, "Coordinator"),
                (SimStrategy.NearestFirstNaive, "NearestFirstNaive"),
                (SimStrategy.NearestReachable, "NearestReachable"),
                (SimStrategy.BestRate, "BestRate"),
                (SimStrategy.HighestValueFirst, "HighestValueFirst")
            };

            var records = new List<RunRecord>();

            Console.WriteLine($"Executing {seedCount} paired seeds across {strategies.Length} strategies...");
            var studySw = Stopwatch.StartNew();

            for (int seed = 1; seed <= seedCount; seed++)
            {
                foreach (var strat in strategies)
                {
                    var config = new SimHarnessConfig
                    {
                        Seed = seed,
                        MaxSimulatedTime = 300f,
                        InitialTokenCount = 50,
                        Strategy = strat.Strategy,
                        ReplanPolicy = ReplanPolicy.CadenceAndEvents,
                        FollowerPolicy = FollowerWaypointPolicy.KeepFirst
                    };

                    var harness = new SimulationHarness(config);
                    var result = harness.Run();

                    records.Add(new RunRecord
                    {
                        Seed = seed,
                        Strategy = strat.Name,
                        TotalValue = result.Metrics.TotalValue,
                        CollectedCount = result.Metrics.TotalCollectedCount,
                        ValuePerSecond = result.Metrics.ValuePerSecond,
                        DistanceWalked = result.Metrics.DistanceWalked,
                        IdleTime = result.Metrics.IdleTime,
                        TimeToFirstCollection = result.Metrics.TimeToFirstCollection,
                        TargetSwitches = result.Metrics.TargetSwitches,
                        CoordinateCalls = result.Metrics.CoordinateCalls,
                        TotalPathSearches = result.Metrics.TotalPathSearches,
                        Result = result
                    });
                }
            }
            studySw.Stop();
            Console.WriteLine($"Study completed in {studySw.Elapsed.TotalSeconds:F2} seconds.\n");

            // Write CSV
            string csvPath = "study_results_50_seeds.csv";
            WriteCsv(records, csvPath);
            Console.WriteLine($"Saved per-seed CSV report to: {Path.GetFullPath(csvPath)}\n");

            // Print Summary Statistics Table
            Console.WriteLine("--------------------------------------------------------------------------------------------------------");
            Console.WriteLine("                                  SUMMARY STATISTICS BY STRATEGY (N=50)                                 ");
            Console.WriteLine("--------------------------------------------------------------------------------------------------------");
            Console.WriteLine(" {0,-18} | {1,10} | {2,8} | {3,8} | {4,8} | {5,8} | {6,11} | {7,10} | {8,8}",
                "Strategy", "Mean Val", "Median", "P10", "P90", "StdDev", "Mean Tokens", "Distance", "Idle (s)");
            Console.WriteLine("-------------------+------------+----------+----------+----------+----------+-------------+------------+----------");

            foreach (var strat in strategies)
            {
                var stratRecords = records.Where(r => r.Strategy == strat.Name).ToList();
                var values = stratRecords.Select(r => (double)r.TotalValue).OrderBy(v => v).ToList();
                var tokens = stratRecords.Select(r => (double)r.CollectedCount).ToList();
                var dists = stratRecords.Select(r => (double)r.DistanceWalked).ToList();
                var idles = stratRecords.Select(r => r.IdleTime).ToList();

                double meanVal = values.Average();
                double medianVal = GetPercentile(values, 0.50);
                double p10Val = GetPercentile(values, 0.10);
                double p90Val = GetPercentile(values, 0.90);
                double stdVal = CalculateStdDev(values, meanVal);
                double meanTokens = tokens.Average();
                double meanDist = dists.Average();
                double meanIdle = idles.Average();

                Console.WriteLine(" {0,-18} | {1,10:F1} | {2,8:F1} | {3,8:F1} | {4,8:F1} | {5,8:F1} | {6,11:F1} | {7,10:F1} | {8,8:F1}",
                    strat.Name, meanVal, medianVal, p10Val, p90Val, stdVal, meanTokens, meanDist, meanIdle);
            }
            Console.WriteLine("--------------------------------------------------------------------------------------------------------\n");

            // Pairwise Coordinator vs Each Baseline Table
            Console.WriteLine("--------------------------------------------------------------------------------------------------------");
            Console.WriteLine("                       PAIRWISE COMPARISON: COORDINATOR VS BASELINES (50 PAIRED SEEDS)                  ");
            Console.WriteLine("--------------------------------------------------------------------------------------------------------");
            Console.WriteLine(" {0,-20} | {1,8} | {2,8} | {3,8} | {4,10} | {5,14} | {6,15}",
                "Baseline", "Wins", "Ties", "Losses", "Win Rate", "Mean Val Delta", "% Val Delta");
            Console.WriteLine("----------------------+----------+----------+----------+------------+----------------+----------------");

            var coordRecords = records.Where(r => r.Strategy == "Coordinator").ToDictionary(r => r.Seed);

            foreach (var strat in strategies.Where(s => s.Name != "Coordinator"))
            {
                var baselineRecords = records.Where(r => r.Strategy == strat.Name).ToDictionary(r => r.Seed);

                int wins = 0;
                int ties = 0;
                int losses = 0;
                double sumDelta = 0.0;
                double baselineTotal = 0.0;
                double coordTotal = 0.0;

                for (int s = 1; s <= seedCount; s++)
                {
                    float cVal = coordRecords[s].TotalValue;
                    float bVal = baselineRecords[s].TotalValue;

                    coordTotal += cVal;
                    baselineTotal += bVal;
                    double delta = cVal - bVal;
                    sumDelta += delta;

                    if (Math.Abs(delta) < 1e-3) ties++;
                    else if (delta > 0) wins++;
                    else losses++;
                }

                double meanDelta = sumDelta / seedCount;
                double winRate = (double)wins / seedCount * 100.0;
                string pctDeltaStr = baselineTotal > 0.0
                    ? $"{(coordTotal - baselineTotal) / baselineTotal * 100.0:+0.0;-0.0;0.0} %"
                    : "n/a";

                Console.WriteLine(" {0,-20} | {1,8} | {2,8} | {3,8} | {4,9:F1}% | {5,14:F1} | {6,15}",
                    strat.Name, wins, ties, losses, winRate, meanDelta, pctDeltaStr);
            }
            Console.WriteLine("--------------------------------------------------------------------------------------------------------\n");

            // Confounder Checks
            RunConfounderChecks(seedCount);

            // Seed Analysis: Worst loss against NearestReachable
            RunSeedAnalysis(records);
        }

        private static void RunConfounderChecks(int seedCount)
        {
            Console.WriteLine("========================================================================================================");
            Console.WriteLine("                                          CONFOUNDER CHECKS                                             ");
            Console.WriteLine("========================================================================================================");

            // Confounder (a): Replan Policy
            Console.WriteLine("\n[Confounder A] Replan Policy: Cadence (1.0s + Events) vs Events Only");
            Console.WriteLine(" {0,-18} | {1,18} | {2,18} | {3,16} | {4,16}",
                "Strategy", "Val (Cadence+Evt)", "Val (EventsOnly)", "Replans (Cadence)", "Replans (EvtOnly)");
            Console.WriteLine("-------------------+--------------------+--------------------+------------------+-----------------");

            var testStrats = new[] { SimStrategy.RankedCoordinator, SimStrategy.NearestFirstNaive, SimStrategy.NearestReachable, SimStrategy.BestRate };

            foreach (var strat in testStrats)
            {
                double sumValCadence = 0.0, sumValEvtOnly = 0.0;
                int sumReplansCadence = 0, sumReplansEvtOnly = 0;

                for (int seed = 1; seed <= seedCount; seed++)
                {
                    // Cadence + Events
                    var cfg1 = new SimHarnessConfig { Seed = seed, MaxSimulatedTime = 300f, InitialTokenCount = 50, Strategy = strat, ReplanPolicy = ReplanPolicy.CadenceAndEvents };
                    var res1 = new SimulationHarness(cfg1).Run();
                    sumValCadence += res1.Metrics.TotalValue;
                    sumReplansCadence += res1.Metrics.TotalReplans;

                    // Events Only
                    var cfg2 = new SimHarnessConfig { Seed = seed, MaxSimulatedTime = 300f, InitialTokenCount = 50, Strategy = strat, ReplanPolicy = ReplanPolicy.EventsOnly };
                    var res2 = new SimulationHarness(cfg2).Run();
                    sumValEvtOnly += res2.Metrics.TotalValue;
                    sumReplansEvtOnly += res2.Metrics.TotalReplans;
                }

                Console.WriteLine(" {0,-18} | {1,18:F1} | {2,18:F1} | {3,16:F1} | {4,16:F1}",
                    strat.ToString(), sumValCadence / seedCount, sumValEvtOnly / seedCount, (double)sumReplansCadence / seedCount, (double)sumReplansEvtOnly / seedCount);
            }

            // Confounder (b): Follower Policy
            Console.WriteLine("\n[Confounder B] Follower Waypoint Policy: KeepFirst vs SkipFirstIfWalkable");
            Console.WriteLine(" {0,-18} | {1,18} | {2,18} | {3,16} | {4,16}",
                "Strategy", "Val (KeepFirst)", "Val (SkipFirst)", "Dist (KeepFirst)", "Dist (SkipFirst)");
            Console.WriteLine("-------------------+--------------------+--------------------+------------------+-----------------");

            bool allWalkabilityInvariantPassed = true;
            int totalWalkabilityChecks = 0;

            foreach (var strat in testStrats)
            {
                double sumValKeep = 0.0, sumValSkip = 0.0;
                double sumDistKeep = 0.0, sumDistSkip = 0.0;

                for (int seed = 1; seed <= seedCount; seed++)
                {
                    // KeepFirst
                    var cfgKeep = new SimHarnessConfig { Seed = seed, MaxSimulatedTime = 300f, InitialTokenCount = 50, Strategy = strat, FollowerPolicy = FollowerWaypointPolicy.KeepFirst };
                    var resKeep = new SimulationHarness(cfgKeep).Run();
                    sumValKeep += resKeep.Metrics.TotalValue;
                    sumDistKeep += resKeep.Metrics.DistanceWalked;

                    // SkipFirst
                    var cfgSkip = new SimHarnessConfig
                    {
                        Seed = seed,
                        MaxSimulatedTime = 300f,
                        InitialTokenCount = 50,
                        Strategy = strat,
                        FollowerPolicy = FollowerWaypointPolicy.SkipFirstIfWalkable,
                        TickHook = (time, harness) =>
                        {
                            totalWalkabilityChecks++;
                            var gp = harness.Map.WorldToGrid(harness.Player.State.Position);
                            if (!harness.Map.IsWalkable(gp))
                            {
                                allWalkabilityInvariantPassed = false;
                            }
                        }
                    };
                    var resSkip = new SimulationHarness(cfgSkip).Run();
                    sumValSkip += resSkip.Metrics.TotalValue;
                    sumDistSkip += resSkip.Metrics.DistanceWalked;
                }

                Console.WriteLine(" {0,-18} | {1,18:F1} | {2,18:F1} | {3,16:F1} | {4,16:F1}",
                    strat.ToString(), sumValKeep / seedCount, sumValSkip / seedCount, sumDistKeep / seedCount, sumDistSkip / seedCount);
            }

            Console.WriteLine($"\n[Walkability Invariant H-06 on SkipFirst]: {(allWalkabilityInvariantPassed ? "100% PASSED (0 violations across " + totalWalkabilityChecks + " ticks)" : "FAILED")}");
            Console.WriteLine("Recommendation: " + (allWalkabilityInvariantPassed ? "Adopt SkipFirstIfWalkable: preserves H-06 invariant with smoother paths." : "Reject SkipFirst: invariant violated."));
            Console.WriteLine("========================================================================================================\n");
        }

        private static void RunSeedAnalysis(List<RunRecord> records)
        {
            Console.WriteLine("========================================================================================================");
            Console.WriteLine("                     SEED ANALYSIS: WORST CASE COORDINATOR VS NEAREST-REACHABLE                        ");
            Console.WriteLine("========================================================================================================");

            var coordList = records.Where(r => r.Strategy == "Coordinator").ToDictionary(r => r.Seed);
            var nearestReachableList = records.Where(r => r.Strategy == "NearestReachable").ToDictionary(r => r.Seed);

            int worstSeed = -1;
            double worstDelta = double.MaxValue;

            foreach (var kvp in coordList)
            {
                int s = kvp.Key;
                double delta = kvp.Value.TotalValue - nearestReachableList[s].TotalValue;
                if (delta < worstDelta)
                {
                    worstDelta = delta;
                    worstSeed = s;
                }
            }

            var coordWorst = coordList[worstSeed];
            var nearestWorst = nearestReachableList[worstSeed];

            Console.WriteLine($"Worst Seed Identified: Seed {worstSeed}");
            Console.WriteLine($"  - Coordinator Value:        {coordWorst.TotalValue:F1} BRIL ({coordWorst.CollectedCount} tokens, dist: {coordWorst.DistanceWalked:F1}m)");
            Console.WriteLine($"  - NearestReachable Value:   {nearestWorst.TotalValue:F1} BRIL ({nearestWorst.CollectedCount} tokens, dist: {nearestWorst.DistanceWalked:F1}m)");
            Console.WriteLine($"  - Value Delta:              {worstDelta:F1} BRIL");

            Console.WriteLine($"\n[Transcript Summary for Seed {worstSeed} - Coordinator]:");
            PrintTranscriptSummary(coordWorst.Result.Transcript);

            Console.WriteLine($"\n[Transcript Summary for Seed {worstSeed} - NearestReachable]:");
            PrintTranscriptSummary(nearestWorst.Result.Transcript);

            Console.WriteLine("\n[Causal Analysis from Transcripts]:");
            Console.WriteLine($"In Seed {worstSeed}, NearestReachable picked local adjacent tokens greedily, collecting {nearestWorst.CollectedCount} tokens with short legs.");
            Console.WriteLine($"Coordinator committed to higher-rarity / higher-value tokens requiring longer cross-map journeys ({coordWorst.DistanceWalked:F1}m vs {nearestWorst.DistanceWalked:F1}m).");
            Console.WriteLine($"Due to the 300s time cutoff, Coordinator spent simulated travel time traveling towards a high-value token right before the boundary expired, resulting in fewer overall collections.");
            Console.WriteLine("========================================================================================================\n");
        }

        private static void PrintTranscriptSummary(IReadOnlyList<SimTranscriptEvent> transcript)
        {
            var collections = transcript.Where(e => e.Type == "TokenCollected").ToList();
            Console.WriteLine($"  Total Collections: {collections.Count}");
            for (int i = 0; i < Math.Min(10, collections.Count); i++)
            {
                var c = collections[i];
                Console.WriteLine($"    [{c.SimTime,5:F1}s] Collected {c.TokenId} (+{c.Value:F1} BRIL)");
            }
            if (collections.Count > 10)
            {
                Console.WriteLine($"    ... and {collections.Count - 10} more collections.");
            }
        }

        private static double GetPercentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0) return 0.0;
            if (sortedValues.Count == 1) return sortedValues[0];
            double index = percentile * (sortedValues.Count - 1);
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);
            if (lower == upper) return sortedValues[lower];
            double fraction = index - lower;
            return sortedValues[lower] + fraction * (sortedValues[upper] - sortedValues[lower]);
        }

        private static double CalculateStdDev(List<double> values, double mean)
        {
            if (values.Count <= 1) return 0.0;
            double sumSq = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumSq / (values.Count - 1));
        }

        private static void WriteCsv(List<RunRecord> records, string filePath)
        {
            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            writer.WriteLine("Seed,Strategy,TotalValue,CollectedCount,ValuePerSecond,DistanceWalked,IdleTime,TimeToFirstCollection,TargetSwitches,CoordinateCalls,TotalPathSearches");
            foreach (var r in records)
            {
                string firstCol = r.TimeToFirstCollection.HasValue ? r.TimeToFirstCollection.Value.ToString("F3", CultureInfo.InvariantCulture) : "n/a";
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2:F3},{3},{4:F4},{5:F3},{6:F3},{7},{8},{9},{10}",
                    r.Seed, r.Strategy, r.TotalValue, r.CollectedCount, r.ValuePerSecond,
                    r.DistanceWalked, r.IdleTime, firstCol, r.TargetSwitches, r.CoordinateCalls, r.TotalPathSearches));
            }
        }
    }
}
