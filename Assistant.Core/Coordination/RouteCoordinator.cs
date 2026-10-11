using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;
using Assistant.Core.Pathfinding;
using Assistant.Core.Ranking;

namespace Assistant.Core.Coordination
{
    /// <summary>
    /// Route coordinator orchestrating target discovery snapshots, straight-line upper bound ranking,
    /// branch-and-bound A* verification, and current target commitment stability.
    ///
    /// Thread safety: RouteCoordinator is NOT thread-safe and is designed for single-threaded execution.
    /// </summary>
    public sealed class RouteCoordinator : IRouteCoordinator
    {
        private readonly ITokenRepository _tokenRepository;
        private readonly ITargetRanker _targetRanker;
        private readonly IPathfinder _pathfinder;
        private readonly INavGrid _grid;

        public RouteCoordinator(
            ITokenRepository tokenRepository,
            ITargetRanker targetRanker,
            IPathfinder pathfinder,
            INavGrid grid)
        {
            _tokenRepository = tokenRepository ?? throw new ArgumentNullException(nameof(tokenRepository));
            _targetRanker = targetRanker ?? throw new ArgumentNullException(nameof(targetRanker));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }


        public TargetDecision Coordinate(
            PlayerState player,
            RankingConfig ranking,
            CoordinationConfig coordination,
            string? currentTargetId)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (ranking == null) throw new ArgumentNullException(nameof(ranking));
            if (coordination == null) throw new ArgumentNullException(nameof(coordination));

            // Step 1: Validate player coordinates
            if (!player.Position.IsFinite())
            {
                return TargetDecision.None(
                    CoordinationTerminationReason.NoValidCandidates,
                    0,
                    Array.Empty<string>(),
                    new Dictionary<string, PathStatus>());
            }

            // Step 2: Resolve Start Cell & Snapping
            var playerGrid = _grid.WorldToGrid(player.Position);
            bool playerSnappedToWalkable = false;
            GridPosition searchStartCell = playerGrid;

            if (!_grid.IsInBounds(playerGrid) || !_grid.IsWalkable(playerGrid))
            {
                if (!TrySnapToNearestWalkable(player.Position, playerGrid, out searchStartCell))
                {
                    return TargetDecision.None(
                        CoordinationTerminationReason.PlayerOnBlockedCell,
                        0,
                        Array.Empty<string>(),
                        new Dictionary<string, PathStatus>());
                }

                playerSnappedToWalkable = true;
            }

            // Step 3: Fetch Snapshot, Filter Valid Candidates & Validate Goal Cells
            var rawTokens = _tokenRepository.GetAvailable();
            var skipped = new List<string>();
            var diagnostics = new Dictionary<string, PathStatus>();
            var candidates = new List<TokenLocation>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < rawTokens.Count; i++)
            {
                var t = rawTokens[i];
                if (t == null || t.IsCollected) continue;
                if (!t.Position.IsFinite()) continue;
                if (string.IsNullOrEmpty(t.Id) || !seenIds.Add(t.Id)) continue;

                var goalGrid = _grid.WorldToGrid(t.Position);
                if (!_grid.IsInBounds(goalGrid) || !_grid.IsWalkable(goalGrid))
                {
                    diagnostics[t.Id] = PathStatus.InvalidGoal;
                    skipped.Add(t.Id);
                    continue;
                }

                candidates.Add(t);
            }

            if (candidates.Count == 0)
            {
                return TargetDecision.None(
                    CoordinationTerminationReason.NoValidCandidates,
                    0,
                    skipped,
                    diagnostics,
                    playerSnappedToWalkable);
            }

            int searchesRun = 0;
            int maxExpansions = _grid.GridWidth * _grid.GridHeight;
            TargetEvaluationResult? bestEval = null;
            PathResult? bestPath = null;
            TokenLocation? bestToken = null;
            bool isCurrentTargetRetainedInsideCommit = false;

            // Step 4: Evaluate Commit Radius Stability
            if (!string.IsNullOrEmpty(currentTargetId))
            {
                var currentToken = candidates.FirstOrDefault(c => StringComparer.Ordinal.Equals(c.Id, currentTargetId));
                if (currentToken != null)
                {
                    double dist = Vec3.Distance2DDouble(player.Position, currentToken.Position);
                    if (dist <= (double)coordination.CommitRadius)
                    {
                        searchesRun++;
                        var goalGrid = _grid.WorldToGrid(currentToken.Position);
                        var directPath = _pathfinder.FindPath(searchStartCell, goalGrid, maxExpansions);
                        diagnostics[currentToken.Id] = directPath.Status;

                        if (directPath.Status == PathStatus.Success)
                        {
                            float pathCostWorld = WorldPathCost.Compute(_grid, directPath, player.Position, searchStartCell, currentToken.Position);
                            if (float.IsPositiveInfinity(pathCostWorld))
                            {
                                skipped.Add(currentToken.Id);
                            }
                            else
                            {
                                var currentEval = _targetRanker.Evaluate(
                                    currentToken,
                                    player,
                                    ranking,
                                    currentTargetId,
                                    verifiedPathCost: pathCostWorld,
                                    lineCostPerMeter: _grid.MinTraversalCost);

                                if (currentEval != null)
                                {
                                    bestToken = currentToken;
                                    bestPath = directPath;
                                    bestEval = currentEval;
                                    isCurrentTargetRetainedInsideCommit = true;

                                    if (float.IsPositiveInfinity(coordination.CommitOverrideMargin))
                                    {
                                        return new TargetDecision(
                                            currentToken,
                                            directPath,
                                            currentEval,
                                            CoordinationTerminationReason.OptimalFound,
                                            searchesRun,
                                            skipped,
                                            diagnostics,
                                            playerSnappedToWalkable);
                                    }
                                }
                                else
                                {
                                    skipped.Add(currentToken.Id);
                                }
                            }
                        }
                        else
                        {
                            skipped.Add(currentToken.Id);
                        }
                    }
                }
            }

            // Step 5: Straight-Line Upper Bound Evaluation
            var upperBounds = _targetRanker.RankCandidates(
                candidates,
                player,
                ranking,
                currentTargetId,
                verifiedPathCosts: null,
                lineCostPerMeter: _grid.MinTraversalCost);

            if (upperBounds.Count == 0)
            {
                return TargetDecision.None(
                    CoordinationTerminationReason.NoValidCandidates,
                    searchesRun,
                    skipped,
                    diagnostics,
                    playerSnappedToWalkable);
            }

            // Step 6: Branch-and-Bound Evaluation Loop
            bool searchBudgetExceededOccurred = false;
            bool stoppedByPruning = false;
            bool hasUnprunedUnevaluatedCandidate = false;

            for (int i = 0; i < upperBounds.Count; i++)
            {
                var candidateEval = upperBounds[i];

                // Skip if already evaluated in commit-radius check or already searched
                if (bestToken != null && StringComparer.Ordinal.Equals(candidateEval.Token.Id, bestToken.Id))
                    continue;
                if (diagnostics.ContainsKey(candidateEval.Token.Id))
                    continue;

                // Effective threshold
                long effectiveThresholdMicros;
                if (bestEval != null)
                {
                    if (isCurrentTargetRetainedInsideCommit)
                    {
                        if (float.IsPositiveInfinity(coordination.CommitOverrideMargin))
                        {
                            effectiveThresholdMicros = long.MaxValue;
                        }
                        else
                        {
                            double weightSum = (double)ranking.WeightValue + (double)ranking.WeightTime + (double)ranking.WeightRarity + (double)ranking.WeightHysteresis;
                            long marginMicros = (long)Math.Floor((double)coordination.CommitOverrideMargin * weightSum * 1_000_000.0 + 0.5);
                            effectiveThresholdMicros = bestEval.ScoreMicros > long.MaxValue - marginMicros
                                ? long.MaxValue
                                : bestEval.ScoreMicros + marginMicros;
                        }
                    }
                    else
                    {
                        effectiveThresholdMicros = bestEval.ScoreMicros;
                    }

                    // Branch-and-bound slack-adjusted pruning check
                    long adjustedUpperMicros = candidateEval.ScoreMicros + coordination.UpperBoundSlackMicros;
                    bool shouldPrune;
                    if (isCurrentTargetRetainedInsideCommit)
                    {
                        // Inside commit radius, any candidate whose score reaches or exceeds the threshold
                        // can dethrone the incumbent regardless of candidate ID ordering.
                        // Therefore, equality with effectiveThresholdMicros must not be pruned based on ID.
                        // Note: with slack > 0, the tie branch only matters inside the sub-slack band.
                        shouldPrune = adjustedUpperMicros < effectiveThresholdMicros;
                    }
                    else
                    {
                        // Outside commit radius, standard tie-break applies: equal scores only replace
                        // the incumbent if the candidate ID sorts strictly before the incumbent ID.
                        // Note: with slack > 0, the tie branch only matters inside the sub-slack band.
                        shouldPrune = adjustedUpperMicros < effectiveThresholdMicros ||
                           (adjustedUpperMicros == effectiveThresholdMicros && StringComparer.Ordinal.Compare(candidateEval.Token.Id, bestToken!.Id) > 0);
                    }

                    if (shouldPrune)
                    {
                        stoppedByPruning = true;
                        break;
                    }
                }

                // Search budget cap
                if (searchesRun >= coordination.MaxPathSearches)
                {
                    hasUnprunedUnevaluatedCandidate = true;
                    break;
                }

                // Execute path search
                searchesRun++;
                var goalGrid = _grid.WorldToGrid(candidateEval.Token.Position);
                var pathResult = _pathfinder.FindPath(searchStartCell, goalGrid, maxExpansions);
                diagnostics[candidateEval.Token.Id] = pathResult.Status;

                if (pathResult.Status == PathStatus.Success)
                {
                    float verifiedWorldCost = WorldPathCost.Compute(_grid, pathResult, player.Position, searchStartCell, candidateEval.Token.Position);
                    if (float.IsPositiveInfinity(verifiedWorldCost))
                    {
                        skipped.Add(candidateEval.Token.Id);
                        continue;
                    }

                    var verifiedEval = _targetRanker.Evaluate(
                        candidateEval.Token,
                        player,
                        ranking,
                        currentTargetId,
                        verifiedPathCost: verifiedWorldCost,
                        lineCostPerMeter: _grid.MinTraversalCost);

                    if (verifiedEval == null)
                    {
                        skipped.Add(candidateEval.Token.Id);
                        continue;
                    }

                    if (bestEval == null)
                    {
                        bestEval = verifiedEval;
                        bestPath = pathResult;
                        bestToken = candidateEval.Token;
                    }
                    else if (isCurrentTargetRetainedInsideCommit)
                    {
                        if (float.IsPositiveInfinity(coordination.CommitOverrideMargin))
                        {
                            continue;
                        }

                        double weightSum = (double)ranking.WeightValue + (double)ranking.WeightTime + (double)ranking.WeightRarity + (double)ranking.WeightHysteresis;
                        long marginMicros = (long)Math.Floor((double)coordination.CommitOverrideMargin * weightSum * 1_000_000.0 + 0.5);
                        long thresholdMicros = bestEval.ScoreMicros > long.MaxValue - marginMicros
                            ? long.MaxValue
                            : bestEval.ScoreMicros + marginMicros;

                        if (verifiedEval.ScoreMicros >= thresholdMicros)
                        {
                            bestEval = verifiedEval;
                            bestPath = pathResult;
                            bestToken = candidateEval.Token;
                            isCurrentTargetRetainedInsideCommit = false; // Dethroned!
                        }
                    }
                    else
                    {
                        if (verifiedEval.ScoreMicros > bestEval.ScoreMicros ||
                           (verifiedEval.ScoreMicros == bestEval.ScoreMicros && StringComparer.Ordinal.Compare(candidateEval.Token.Id, bestToken!.Id) < 0))
                        {
                            bestEval = verifiedEval;
                            bestPath = pathResult;
                            bestToken = candidateEval.Token;
                        }
                    }
                }
                else
                {
                    if (pathResult.Status == PathStatus.SearchBudgetExceeded)
                    {
                        long adjustedUpper = candidateEval.ScoreMicros + coordination.UpperBoundSlackMicros;
                        long currentBest = bestEval != null ? bestEval.ScoreMicros : long.MinValue;
                        if (adjustedUpper >= currentBest)
                        {
                            searchBudgetExceededOccurred = true;
                        }
                    }

                    skipped.Add(candidateEval.Token.Id);
                }
            }

            if (bestToken == null)
            {
                return TargetDecision.None(
                    CoordinationTerminationReason.NoValidCandidates,
                    searchesRun,
                    skipped,
                    diagnostics,
                    playerSnappedToWalkable);
            }

            CoordinationTerminationReason finalReason;
            if (stoppedByPruning)
            {
                finalReason = searchBudgetExceededOccurred
                    ? CoordinationTerminationReason.SearchBudgetReached
                    : CoordinationTerminationReason.OptimalFound;
            }
            else if (hasUnprunedUnevaluatedCandidate)
            {
                finalReason = CoordinationTerminationReason.SearchBudgetReached;
            }
            else
            {
                finalReason = searchBudgetExceededOccurred
                    ? CoordinationTerminationReason.SearchBudgetReached
                    : CoordinationTerminationReason.AllCandidatesEvaluated;
            }

            return new TargetDecision(
                bestToken,
                bestPath,
                bestEval,
                finalReason,
                searchesRun,
                skipped,
                diagnostics,
                playerSnappedToWalkable);
        }

        private bool TrySnapToNearestWalkable(Vec3 playerPos, GridPosition playerGrid, out GridPosition snappedGrid)
        {
            if (TryFindBestInRing(playerPos, playerGrid, 1, out snappedGrid))
            {
                return true;
            }

            if (TryFindBestInRing(playerPos, playerGrid, 2, out snappedGrid))
            {
                return true;
            }

            snappedGrid = playerGrid;
            return false;
        }

        private bool TryFindBestInRing(Vec3 playerPos, GridPosition center, int radius, out GridPosition bestPos)
        {
            bestPos = center;
            double bestDist = double.MaxValue;
            int bestX = int.MaxValue;
            int bestZ = int.MaxValue;
            bool found = false;

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                        continue;

                    var pos = new GridPosition(center.X + dx, center.Z + dz);
                    if (!_grid.IsInBounds(pos) || !_grid.IsWalkable(pos))
                        continue;

                    Vec3 cellCenter = _grid.GridToWorld(pos);
                    double dist = Vec3.Distance2DDouble(playerPos, cellCenter);

                    if (!found)
                    {
                        found = true;
                        bestDist = dist;
                        bestX = pos.X;
                        bestZ = pos.Z;
                        bestPos = pos;
                    }
                    else
                    {
                        // Primary: distance ascending, Secondary: X ascending, Tertiary: Z ascending
                        if (dist < bestDist - 1e-9)
                        {
                            bestDist = dist;
                            bestX = pos.X;
                            bestZ = pos.Z;
                            bestPos = pos;
                        }
                        else if (Math.Abs(dist - bestDist) <= 1e-9)
                        {
                            if (pos.X < bestX || (pos.X == bestX && pos.Z < bestZ))
                            {
                                bestDist = dist;
                                bestX = pos.X;
                                bestZ = pos.Z;
                                bestPos = pos;
                            }
                        }
                    }
                }
            }

            return found;
        }
    }
}
