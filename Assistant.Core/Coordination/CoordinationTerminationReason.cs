namespace Assistant.Core.Coordination
{
    /// <summary>
    /// Explicit termination reasons for the route coordinator's decision cycle.
    /// Provides deterministic diagnostics for candidate search completion or early exit.
    /// </summary>
    public enum CoordinationTerminationReason
    {
        /// <summary>
        /// Search completed early because branch-and-bound safely pruned all remaining candidates,
        /// proving the selected target is score-optimal.
        /// </summary>
        OptimalFound,

        /// <summary>
        /// Search stopped because MaxPathSearches cap was reached with unpruned candidates remaining,
        /// or an expansion budget (SearchBudgetExceeded) was hit on a competing candidate.
        /// </summary>
        SearchBudgetReached,

        /// <summary>
        /// All valid candidates in the repository were evaluated without branch-and-bound pruning early.
        /// </summary>
        AllCandidatesEvaluated,

        /// <summary>
        /// No valid, walkable, and reachable candidate target could be found.
        /// </summary>
        NoValidCandidates,

        /// <summary>
        /// The player start position is unwalkable or out-of-bounds, and cannot be snapped
        /// to any walkable cell within Chebyshev radius 2.
        /// </summary>
        PlayerOnBlockedCell
    }
}
