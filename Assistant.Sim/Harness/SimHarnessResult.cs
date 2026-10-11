using System.Collections.Generic;
using Assistant.Core.Coordination;
using Assistant.Core.Data;

namespace Assistant.Sim.Harness
{
    /// <summary>
    /// Encapsulates the execution result, event transcript, and metrics from a simulation run.
    /// </summary>
    public sealed class SimHarnessResult
    {
        public IReadOnlyList<SimTranscriptEvent> Transcript { get; }
        public SimMetrics Metrics { get; }
        public Vec3 FinalPlayerPosition { get; }
        public double FinalSimTime { get; }
        public CoordinationTerminationReason LastTerminationReason { get; }
        public bool CompletedSuccessfully { get; }

        public SimHarnessResult(
            IReadOnlyList<SimTranscriptEvent> transcript,
            SimMetrics metrics,
            Vec3 finalPlayerPosition,
            double finalSimTime,
            CoordinationTerminationReason lastTerminationReason,
            bool completedSuccessfully)
        {
            Transcript = transcript;
            Metrics = metrics;
            FinalPlayerPosition = finalPlayerPosition;
            FinalSimTime = finalSimTime;
            LastTerminationReason = lastTerminationReason;
            CompletedSuccessfully = completedSuccessfully;
        }
    }
}
