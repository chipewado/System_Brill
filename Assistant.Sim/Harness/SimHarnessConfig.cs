using System;
using System.Collections.Generic;
using Assistant.Core.Coordination;
using Assistant.Core.Data;
using Assistant.Core.Ranking;

namespace Assistant.Sim.Harness
{
    /// <summary>
    /// Configuration for a simulation run.
    /// </summary>
    public sealed class SimHarnessConfig
    {
        public int GridWidth { get; set; } = 100;
        public int GridHeight { get; set; } = 100;
        public float CellSize { get; set; } = 10f;
        public float PlayerSpeed { get; set; } = 6f;
        public Vec3 PlayerStartPosition { get; set; } = new Vec3(50f, 0, 50f);
        public float ScanRadius { get; set; } = 200f;
        public float TtlSeconds { get; set; } = 30f;
        public float ScanIntervalSeconds { get; set; } = 1.0f;
        public float ReplanIntervalSeconds { get; set; } = 1.0f;
        public float CollectRadius { get; set; } = 1.0f;
        public float Dt { get; set; } = 0.1f;
        public float MaxSimulatedTime { get; set; } = 600f;
        public int InitialTokenCount { get; set; } = 30;
        public int Seed { get; set; } = 42;
        public bool GenerateProceduralTerrain { get; set; } = true;
        public SimStrategy Strategy { get; set; } = SimStrategy.RankedCoordinator;
        public RankingConfig RankingConfig { get; set; } = new RankingConfig();
        public CoordinationConfig CoordinationConfig { get; set; } = new CoordinationConfig();
        public List<TokenLocation>? CustomTokens { get; set; }
        public DateTimeOffset StartTime { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public bool StopWhenNoValidTargets { get; set; } = true;
        public ReplanPolicy ReplanPolicy { get; set; } = ReplanPolicy.CadenceAndEvents;
        public FollowerWaypointPolicy FollowerPolicy { get; set; } = FollowerWaypointPolicy.KeepFirst;

        /// <summary>
        /// Optional per-tick hook for tests (e.g. wall placement or invariant assertions).
        /// Signature: (simTime, harness).
        /// </summary>
        public Action<double, SimulationHarness>? TickHook { get; set; }
    }
}
