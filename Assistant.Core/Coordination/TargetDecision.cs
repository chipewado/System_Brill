using System;
using System.Collections.Generic;
using Assistant.Core.Data;
using Assistant.Core.Pathfinding;
using Assistant.Core.Ranking;

namespace Assistant.Core.Coordination
{
    /// <summary>
    /// Immutable record representing the coordinator's navigation recommendation and complete execution diagnostics.
    /// </summary>
    public sealed class TargetDecision
    {
        public TokenLocation? SelectedTarget { get; }
        public PathResult? SelectedPath { get; }
        public TargetEvaluationResult? Evaluation { get; }
        public CoordinationTerminationReason TerminationReason { get; }
        public int PathSearchesExecuted { get; }
        public IReadOnlyList<string> SkippedCandidateIds { get; }
        public IReadOnlyDictionary<string, PathStatus> DiagnosticStatusByCandidateId { get; }
        public bool PlayerSnappedToWalkable { get; }

        public static TargetDecision None(
            CoordinationTerminationReason reason,
            int searchesExecuted,
            IReadOnlyList<string> skipped,
            IReadOnlyDictionary<string, PathStatus> diagnostics,
            bool playerSnapped = false) =>
            new TargetDecision(null, null, null, reason, searchesExecuted, skipped, diagnostics, playerSnapped);

        public TargetDecision(
            TokenLocation? selectedTarget,
            PathResult? selectedPath,
            TargetEvaluationResult? evaluation,
            CoordinationTerminationReason terminationReason,
            int pathSearchesExecuted,
            IReadOnlyList<string> skippedCandidateIds,
            IReadOnlyDictionary<string, PathStatus> diagnosticStatusByCandidateId,
            bool playerSnappedToWalkable = false)
        {
            SelectedTarget = selectedTarget;
            SelectedPath = selectedPath;
            Evaluation = evaluation;
            TerminationReason = terminationReason;
            PathSearchesExecuted = pathSearchesExecuted;
            SkippedCandidateIds = skippedCandidateIds ?? Array.Empty<string>();
            DiagnosticStatusByCandidateId = diagnosticStatusByCandidateId ?? new Dictionary<string, PathStatus>();
            PlayerSnappedToWalkable = playerSnappedToWalkable;
        }
    }
}
