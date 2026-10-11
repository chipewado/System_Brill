using System;
using System.Collections.Generic;

namespace Assistant.Sim.Harness
{
    /// <summary>
    /// Cumulative performance and diagnostic metrics for a simulation run.
    /// </summary>
    public sealed class SimMetrics
    {
        public float TotalValue { get; set; }
        public int TotalCollectedCount { get; set; }
        public List<double> CollectionTimes { get; } = new List<double>();
        public double MeanTimePerCollection => TotalCollectedCount > 0 ? TotalSimTime / TotalCollectedCount : 0.0;
        public int TargetSwitches { get; set; }
        public int CoordinateCalls { get; set; }
        public int TotalPathSearches { get; set; }
        public double MeanPathSearchesExecuted => CoordinateCalls > 0 ? (double)TotalPathSearches / CoordinateCalls : 0.0;
        public int MaxPathSearchesExecuted { get; set; }
        public double CoordinateWallTimeMs { get; set; } // Informational only, never asserted
        public double TotalSimTime { get; set; }
        public int TotalReplans { get; set; }
    }
}
