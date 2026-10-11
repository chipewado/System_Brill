using System;
using System.Diagnostics;
using System.Linq;
using Assistant.Core.Data;
using Assistant.Core.Discovery;
using Assistant.Sim;

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

            // Wire up event listeners (Architecture: GameApi -> Repository)
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
            gameApi.MapChanged += args => Console.WriteLine($"    [Event] Map structure changed! ({args.Reason})");

            // 4. Spawn Specification Preset Tokens (Section 21)
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

            // 10. Stage 3.4 Simulation Harness Comparison (Ranked-Coordinator vs Nearest-First)
            RunStage34HarnessComparison();
        }

        private static void RunStage34HarnessComparison()
        {
            Console.WriteLine("========================================================================================================");
            Console.WriteLine("   STAGE 3.4 SIMULATION BENCHMARK: RANKED-COORDINATOR VS NEAREST-FIRST BASELINE (300s SIMULATED TIME)   ");
            Console.WriteLine("========================================================================================================");
            Console.WriteLine(" {0,6} | {1,20} | {2,15} | {3,19} | {4,17} | {5,15}",
                "Seed", "Coord Value (BRIL)", "Coord Collected", "Nearest Value (BRIL)", "Nearest Collected", "Value Delta (%)");
            Console.WriteLine("-------+----------------------+-----------------+---------------------+-------------------+----------------");

            int[] seeds = new int[] { 101, 202, 303, 404, 505 };

            float totalCoordVal = 0f;
            float totalNearestVal = 0f;
            int totalCoordTokens = 0;
            int totalNearestTokens = 0;

            foreach (var seed in seeds)
            {
                var coordConfig = new Assistant.Sim.Harness.SimHarnessConfig
                {
                    Seed = seed,
                    MaxSimulatedTime = 300f,
                    InitialTokenCount = 50,
                    Strategy = Assistant.Sim.Harness.SimStrategy.RankedCoordinator
                };
                var coordHarness = new Assistant.Sim.Harness.SimulationHarness(coordConfig);
                var coordResult = coordHarness.Run();

                var nearestConfig = new Assistant.Sim.Harness.SimHarnessConfig
                {
                    Seed = seed,
                    MaxSimulatedTime = 300f,
                    InitialTokenCount = 50,
                    Strategy = Assistant.Sim.Harness.SimStrategy.NearestFirst
                };
                var nearestHarness = new Assistant.Sim.Harness.SimulationHarness(nearestConfig);
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

                Console.WriteLine(" {0,6} | {1,20:F1} | {2,15} | {3,19:F1} | {4,17} | {5,15}",
                    seed, coordVal, coordTokens, nearestVal, nearestTokens, deltaStr);
            }

            Console.WriteLine("-------+----------------------+-----------------+---------------------+-------------------+----------------");
            float overallDeltaPct = totalNearestVal > 0f ? ((totalCoordVal - totalNearestVal) / totalNearestVal) * 100f : 0f;
            string overallDeltaStr = $"{(overallDeltaPct >= 0f ? "+" : "")}{overallDeltaPct:F1} %";
            Console.WriteLine(" {0,6} | {1,20:F1} | {2,15} | {3,19:F1} | {4,17} | {5,15}",
                "TOTAL", totalCoordVal, totalCoordTokens, totalNearestVal, totalNearestTokens, overallDeltaStr);
            Console.WriteLine("========================================================================================================\n");
        }
    }
}
