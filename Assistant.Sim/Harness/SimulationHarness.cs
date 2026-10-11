using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Coordination;
using Assistant.Core.Data;
using Assistant.Core.Discovery;
using Assistant.Core.Pathfinding;
using Assistant.Core.Ranking;

namespace Assistant.Sim.Harness
{
    /// <summary>
    /// Deterministic Stage 3.4 & 3.5 simulation harness executing end-to-end loops with
    /// discovery, navigation, coordination, and metrics tracking.
    /// </summary>
    public sealed class SimulationHarness
    {
        private readonly SimHarnessConfig _config;
        private readonly SimClock _clock;
        private readonly SimGridMap _map;
        private readonly SimPlayer _player;
        private readonly SimGameApi _gameApi;
        private readonly TokenRepository _repo;
        private readonly DefaultTokenParser _parser;
        private readonly TokenValidator _validator;
        private readonly TokenDiscoveryService _discovery;
        private readonly AStarPathfinder _pathfinder;
        private readonly DefaultTargetRanker _ranker;
        private readonly RouteCoordinator _coordinator;
        private readonly SimMapChangeNotifier _notifier;

        public SimClock Clock => _clock;
        public SimGridMap Map => _map;
        public SimPlayer Player => _player;
        public SimGameApi GameApi => _gameApi;
        public TokenRepository Repository => _repo;
        public TokenDiscoveryService Discovery => _discovery;
        public AStarPathfinder Pathfinder => _pathfinder;
        public RouteCoordinator Coordinator => _coordinator;
        public SimMapChangeNotifier Notifier => _notifier;

        public SimulationHarness(SimHarnessConfig? config = null)
        {
            _config = config ?? new SimHarnessConfig();

            _clock = new SimClock(_config.StartTime);
            _map = new SimGridMap(_config.GridWidth, _config.GridHeight, _config.CellSize);
            if (_config.GenerateProceduralTerrain)
            {
                _map.GenerateProceduralFeatures(_config.Seed);
            }

            _player = new SimPlayer(_config.PlayerStartPosition, _config.PlayerSpeed);
            _gameApi = new SimGameApi(_map, _player, seed: _config.Seed, clock: _clock);
            _repo = new TokenRepository(spatialCellSize: 50f);
            _parser = new DefaultTokenParser();
            _validator = new TokenValidator(defaultTtlSeconds: _config.TtlSeconds, clock: _clock);

            _discovery = new TokenDiscoveryService(
                _gameApi,
                _repo,
                _parser,
                _validator,
                _clock,
                _gameApi,
                ttlSeconds: _config.TtlSeconds,
                scanRadius: _config.ScanRadius,
                scanInterval: TimeSpan.FromSeconds(_config.ScanIntervalSeconds));

            _pathfinder = new AStarPathfinder(_map);
            _ranker = new DefaultTargetRanker();
            _coordinator = new RouteCoordinator(_repo, _ranker, _pathfinder, _map);
            _notifier = new SimMapChangeNotifier(_gameApi);

            // Populate tokens
            if (_config.CustomTokens != null && _config.CustomTokens.Count > 0)
            {
                foreach (var token in _config.CustomTokens)
                {
                    _gameApi.SpawnToken(token);
                }
            }
            else
            {
                for (int i = 0; i < _config.InitialTokenCount; i++)
                {
                    _gameApi.SpawnRandomToken();
                }
            }
        }

        public SimHarnessResult Run()
        {
            var transcript = new List<SimTranscriptEvent>();
            var metrics = new SimMetrics();

            string? currentTargetId = null;
            TokenLocation? currentTargetToken = null;
            var currentWaypoints = new List<Vec3>();
            int currentWaypointIndex = 0;

            double simTime = 0.0;
            double lastReplanTime = -999.0;
            bool mapChangedFired = false;
            bool targetCollectedThisTick = false;
            CoordinationTerminationReason lastTerminationReason = CoordinationTerminationReason.NoValidCandidates;

            // Wire up event listeners
            _gameApi.MapChanged += args =>
            {
                mapChangedFired = true;
                transcript.Add(new SimTranscriptEvent(simTime, "MapChanged", null, 0f));
            };

            _gameApi.TokenCollected += id =>
            {
                float val = 0f;
                var t = _repo.Get(id);
                if (t != null)
                {
                    val = t.EstimatedValue;
                }
                transcript.Add(new SimTranscriptEvent(simTime, "TokenCollected", id, val));
                metrics.TotalValue += val;
                metrics.TotalCollectedCount++;
                metrics.CollectionTimes.Add(simTime);
                targetCollectedThisTick = true;
            };

            // Local replan execution helper
            void ExecuteReplan()
            {
                transcript.Add(new SimTranscriptEvent(simTime, "Replan", currentTargetId, 0f));
                metrics.TotalReplans++;

                if (_config.Strategy == SimStrategy.RankedCoordinator)
                {
                    var sw = Stopwatch.StartNew();
                    var decision = _coordinator.Coordinate(
                        _player.State,
                        _config.RankingConfig,
                        _config.CoordinationConfig,
                        currentTargetId);
                    sw.Stop();

                    metrics.CoordinateCalls++;
                    metrics.CoordinateWallTimeMs += sw.Elapsed.TotalMilliseconds;
                    metrics.TotalPathSearches += decision.PathSearchesExecuted;
                    if (decision.PathSearchesExecuted > metrics.MaxPathSearchesExecuted)
                    {
                        metrics.MaxPathSearchesExecuted = decision.PathSearchesExecuted;
                    }

                    lastTerminationReason = decision.TerminationReason;

                    if (decision.SelectedTarget != null)
                    {
                        string newTargetId = decision.SelectedTarget.Id;
                        if (currentTargetId == null)
                        {
                            transcript.Add(new SimTranscriptEvent(simTime, "TargetSelected", newTargetId, decision.SelectedTarget.EstimatedValue));
                            currentTargetId = newTargetId;
                            currentTargetToken = decision.SelectedTarget;
                            currentWaypoints = ExtractWaypoints(decision.SelectedPath, decision.SelectedTarget.Position, _config.FollowerPolicy, _player.State.Position, _map);
                            currentWaypointIndex = 0;
                        }
                        else if (newTargetId != currentTargetId)
                        {
                            metrics.TargetSwitches++;
                            transcript.Add(new SimTranscriptEvent(simTime, "TargetChanged", newTargetId, decision.SelectedTarget.EstimatedValue));
                            currentTargetId = newTargetId;
                            currentTargetToken = decision.SelectedTarget;
                            currentWaypoints = ExtractWaypoints(decision.SelectedPath, decision.SelectedTarget.Position, _config.FollowerPolicy, _player.State.Position, _map);
                            currentWaypointIndex = 0;
                        }
                        else
                        {
                            // Incumbent retained: only update waypoints if current route was blocked or empty
                            if (currentWaypoints.Count == 0 || IsPathBlocked(currentWaypoints, currentWaypointIndex, _map))
                            {
                                currentWaypoints = ExtractWaypoints(decision.SelectedPath, decision.SelectedTarget.Position, _config.FollowerPolicy, _player.State.Position, _map);
                                currentWaypointIndex = 0;
                            }
                        }
                    }
                    else
                    {
                        currentTargetId = null;
                        currentTargetToken = null;
                        currentWaypoints.Clear();
                        currentWaypointIndex = 0;
                    }
                }
                else // Baselines: NearestFirstNaive, NearestReachable, BestRate, HighestValueFirst
                {
                    var sw = Stopwatch.StartNew();
                    metrics.CoordinateCalls++;

                    var available = _repo.GetAvailable();
                    var pGrid = _map.WorldToGrid(_player.State.Position);
                    GridPosition startCell = pGrid;
                    if (!_map.IsInBounds(pGrid) || !_map.IsWalkable(pGrid))
                    {
                        TrySnapToNearestWalkable(_map, _player.State.Position, pGrid, out startCell);
                    }

                    TokenLocation? selected = null;
                    PathResult? selectedPath = null;
                    int searches = 0;

                    if (_config.Strategy == SimStrategy.NearestFirst || _config.Strategy == SimStrategy.NearestFirstNaive)
                    {
                        var sorted = available
                            .Where(t => t != null && !t.IsCollected && t.Position.IsFinite())
                            .OrderBy(t => Vec3.Distance2D(_player.State.Position, t.Position))
                            .ThenBy(t => t.Id, StringComparer.Ordinal)
                            .ToList();

                        foreach (var candidate in sorted)
                        {
                            var goalGrid = _map.WorldToGrid(candidate.Position);
                            if (!_map.IsInBounds(goalGrid) || !_map.IsWalkable(goalGrid))
                                continue;

                            searches++;
                            var path = _pathfinder.FindPath(startCell, goalGrid);
                            if (path.Status == PathStatus.Success)
                            {
                                selected = candidate;
                                selectedPath = path;
                                break;
                            }
                        }
                    }
                    else if (_config.Strategy == SimStrategy.NearestReachable)
                    {
                        var validCandidates = available
                            .Where(t => t != null && !t.IsCollected && t.Position.IsFinite() && _map.IsInBounds(_map.WorldToGrid(t.Position)) && _map.IsWalkable(_map.WorldToGrid(t.Position)))
                            .OrderBy(t => Vec3.Distance2D(_player.State.Position, t.Position))
                            .ThenBy(t => t.Id, StringComparer.Ordinal)
                            .ToList();

                        float bestCost = float.PositiveInfinity;
                        TokenLocation? bestCandidate = null;
                        PathResult? bestPathResult = null;

                        foreach (var candidate in validCandidates)
                        {
                            float straightLineDist = Vec3.Distance2D(_player.State.Position, candidate.Position);
                            float lowerBoundCost = straightLineDist * _map.MinTraversalCost;

                            // Exact pruning: lower bound cannot beat bestCost so far
                            if (lowerBoundCost >= bestCost)
                            {
                                break;
                            }

                            searches++;
                            var goalGrid = _map.WorldToGrid(candidate.Position);
                            var path = _pathfinder.FindPath(startCell, goalGrid);
                            if (path.Status == PathStatus.Success)
                            {
                                float cost = WorldPathCost.Compute(_map, path, _player.State.Position, startCell, candidate.Position);
                                if (cost < bestCost)
                                {
                                    bestCost = cost;
                                    bestCandidate = candidate;
                                    bestPathResult = path;
                                }
                                else if (Math.Abs(cost - bestCost) <= 1e-6f && bestCandidate != null && StringComparer.Ordinal.Compare(candidate.Id, bestCandidate.Id) < 0)
                                {
                                    bestCost = cost;
                                    bestCandidate = candidate;
                                    bestPathResult = path;
                                }
                            }
                        }

                        if (bestCandidate != null)
                        {
                            selected = bestCandidate;
                            selectedPath = bestPathResult;
                        }
                    }
                    else if (_config.Strategy == SimStrategy.BestRate)
                    {
                        var validCandidates = available
                            .Where(t => t != null && !t.IsCollected && t.Position.IsFinite() && _map.IsInBounds(_map.WorldToGrid(t.Position)) && _map.IsWalkable(_map.WorldToGrid(t.Position)))
                            .ToList();

                        float speed = _player.State.Speed > 0f ? _player.State.Speed : 6.0f;
                        var candidatesWithUb = validCandidates
                            .Select(t =>
                            {
                                float dist = Vec3.Distance2D(_player.State.Position, t.Position);
                                float lbCost = Math.Max(dist * _map.MinTraversalCost, 0.001f);
                                double ubRate = (double)t.EstimatedValue * speed / lbCost;
                                return (Token: t, UbRate: ubRate);
                            })
                            .OrderByDescending(x => x.UbRate)
                            .ThenBy(x => x.Token.Id, StringComparer.Ordinal)
                            .ToList();

                        double bestActualRate = -1.0;
                        TokenLocation? bestCandidate = null;
                        PathResult? bestPathResult = null;

                        foreach (var item in candidatesWithUb)
                        {
                            // Exact pruning: upper bound rate cannot beat bestActualRate
                            if (item.UbRate <= bestActualRate)
                            {
                                break;
                            }

                            searches++;
                            var goalGrid = _map.WorldToGrid(item.Token.Position);
                            var path = _pathfinder.FindPath(startCell, goalGrid);
                            if (path.Status == PathStatus.Success)
                            {
                                float cost = WorldPathCost.Compute(_map, path, _player.State.Position, startCell, item.Token.Position);
                                if (!float.IsPositiveInfinity(cost))
                                {
                                    double travelTime = (double)cost / speed;
                                    double actualRate = travelTime > 1e-4 ? (double)item.Token.EstimatedValue / travelTime : (double)item.Token.EstimatedValue * 1e4;

                                    if (actualRate > bestActualRate)
                                    {
                                        bestActualRate = actualRate;
                                        bestCandidate = item.Token;
                                        bestPathResult = path;
                                    }
                                    else if (Math.Abs(actualRate - bestActualRate) <= 1e-9 && bestCandidate != null && StringComparer.Ordinal.Compare(item.Token.Id, bestCandidate.Id) < 0)
                                    {
                                        bestActualRate = actualRate;
                                        bestCandidate = item.Token;
                                        bestPathResult = path;
                                    }
                                }
                            }
                        }

                        if (bestCandidate != null)
                        {
                            selected = bestCandidate;
                            selectedPath = bestPathResult;
                        }
                    }
                    else if (_config.Strategy == SimStrategy.HighestValueFirst)
                    {
                        var sorted = available
                            .Where(t => t != null && !t.IsCollected && t.Position.IsFinite())
                            .OrderByDescending(t => t.EstimatedValue)
                            .ThenBy(t => Vec3.Distance2D(_player.State.Position, t.Position))
                            .ThenBy(t => t.Id, StringComparer.Ordinal)
                            .ToList();

                        foreach (var candidate in sorted)
                        {
                            var goalGrid = _map.WorldToGrid(candidate.Position);
                            if (!_map.IsInBounds(goalGrid) || !_map.IsWalkable(goalGrid))
                                continue;

                            searches++;
                            var path = _pathfinder.FindPath(startCell, goalGrid);
                            if (path.Status == PathStatus.Success)
                            {
                                selected = candidate;
                                selectedPath = path;
                                break;
                            }
                        }
                    }

                    sw.Stop();
                    metrics.CoordinateWallTimeMs += sw.Elapsed.TotalMilliseconds;
                    metrics.TotalPathSearches += searches;
                    if (searches > metrics.MaxPathSearchesExecuted)
                    {
                        metrics.MaxPathSearchesExecuted = searches;
                    }

                    if (selected != null)
                    {
                        string newTargetId = selected.Id;
                        if (currentTargetId == null)
                        {
                            transcript.Add(new SimTranscriptEvent(simTime, "TargetSelected", newTargetId, selected.EstimatedValue));
                            currentTargetId = newTargetId;
                            currentTargetToken = selected;
                            currentWaypoints = ExtractWaypoints(selectedPath, selected.Position, _config.FollowerPolicy, _player.State.Position, _map);
                            currentWaypointIndex = 0;
                        }
                        else if (newTargetId != currentTargetId)
                        {
                            metrics.TargetSwitches++;
                            transcript.Add(new SimTranscriptEvent(simTime, "TargetChanged", newTargetId, selected.EstimatedValue));
                            currentTargetId = newTargetId;
                            currentTargetToken = selected;
                            currentWaypoints = ExtractWaypoints(selectedPath, selected.Position, _config.FollowerPolicy, _player.State.Position, _map);
                            currentWaypointIndex = 0;
                        }
                        else
                        {
                            if (currentWaypoints.Count == 0 || IsPathBlocked(currentWaypoints, currentWaypointIndex, _map))
                            {
                                currentWaypoints = ExtractWaypoints(selectedPath, selected.Position, _config.FollowerPolicy, _player.State.Position, _map);
                                currentWaypointIndex = 0;
                            }
                        }
                        lastTerminationReason = CoordinationTerminationReason.OptimalFound;
                    }
                    else
                    {
                        currentTargetId = null;
                        currentTargetToken = null;
                        currentWaypoints.Clear();
                        currentWaypointIndex = 0;
                        lastTerminationReason = CoordinationTerminationReason.NoValidCandidates;
                    }
                }

                lastReplanTime = simTime;
                mapChangedFired = false;
                targetCollectedThisTick = false;
            }

            // Step 0 initialization (t = 0.0)
            _discovery.Tick(); // Initial scan around player
            ExecuteReplan();

            // Main loop: dt = 0.1s
            while (simTime < _config.MaxSimulatedTime - 1e-6)
            {
                // Advance clock and discovery
                _clock.Advance(_config.Dt);
                simTime = Math.Round(simTime + _config.Dt, 4);
                _discovery.Tick();

                // Test hook invocation
                _config.TickHook?.Invoke(simTime, this);

                // Handle obstacle appearing directly on player cell (Edge scenario)
                var currentPlayerGrid = _map.WorldToGrid(_player.State.Position);
                if (!_map.IsInBounds(currentPlayerGrid) || !_map.IsWalkable(currentPlayerGrid))
                {
                    if (TrySnapToNearestWalkable(_map, _player.State.Position, currentPlayerGrid, out var snapCell))
                    {
                        var snapWorld = _map.GridToWorld(snapCell);
                        _player.Teleport(snapWorld);
                        currentWaypoints.Clear();
                        currentWaypointIndex = 0;
                        ExecuteReplan();
                    }
                    else
                    {
                        // Enclosed in pocket with no reachable walkable cell: terminal state
                        break;
                    }
                }

                // 1. Move player along current route
                if (currentWaypoints.Count > 0 && currentWaypointIndex < currentWaypoints.Count)
                {
                    var prevPos = _player.State.Position;
                    _player.MoveTowards(currentWaypoints[currentWaypointIndex], _config.Dt);
                    metrics.DistanceWalked += Vec3.Distance(prevPos, _player.State.Position);
                    if (Vec3.Distance(_player.State.Position, currentWaypoints[currentWaypointIndex]) < 0.001f)
                    {
                        currentWaypointIndex++;
                    }
                }
                else
                {
                    metrics.IdleTime += _config.Dt;
                }

                // 2. Check collection condition
                bool targetCollected = false;
                if (currentTargetToken != null)
                {
                    // Refresh token from repo in case position or status updated
                    var activeToken = _repo.Get(currentTargetToken.Id);
                    if (activeToken != null && !activeToken.IsCollected)
                    {
                        float distToTarget = Vec3.Distance2D(_player.State.Position, activeToken.Position);
                        if (distToTarget <= _config.CollectRadius)
                        {
                            _gameApi.CollectToken(activeToken.Id);
                            targetCollected = true;
                            currentTargetToken = null;
                            currentTargetId = null;
                            currentWaypoints.Clear();
                            currentWaypointIndex = 0;
                        }
                    }
                }

                // 3. Check target removed, expired, or became unwalkable (Edge scenario: wall on target)
                bool targetRemovedOrExpired = false;
                if (currentTargetId != null)
                {
                    var inRepo = _repo.Get(currentTargetId);
                    if (inRepo == null || inRepo.IsCollected)
                    {
                        targetRemovedOrExpired = true;
                        currentTargetToken = null;
                        currentTargetId = null;
                        currentWaypoints.Clear();
                        currentWaypointIndex = 0;
                    }
                    else
                    {
                        var targetGrid = _map.WorldToGrid(inRepo.Position);
                        if (!_map.IsInBounds(targetGrid) || !_map.IsWalkable(targetGrid))
                        {
                            targetRemovedOrExpired = true;
                            currentTargetToken = null;
                            currentTargetId = null;
                            currentWaypoints.Clear();
                            currentWaypointIndex = 0;
                        }
                    }
                }

                // 4. Check if current path is blocked by new obstacles
                bool pathBlocked = false;
                if (currentWaypoints.Count > 0)
                {
                    pathBlocked = IsPathBlocked(currentWaypoints, currentWaypointIndex, _map);
                    if (pathBlocked)
                    {
                        currentWaypoints.Clear();
                        currentWaypointIndex = 0;
                    }
                }

                // 5. Evaluate replan trigger
                bool timeDue = _config.ReplanPolicy == ReplanPolicy.CadenceAndEvents &&
                               (simTime - lastReplanTime >= _config.ReplanIntervalSeconds - 1e-5);
                bool eventDue = targetCollected || targetCollectedThisTick || targetRemovedOrExpired || mapChangedFired || pathBlocked;

                if (timeDue || eventDue)
                {
                    ExecuteReplan();

                    if (_config.StopWhenNoValidTargets && currentTargetToken == null)
                    {
                        var availableTokens = _repo.GetAvailable();
                        if (availableTokens.Count == 0 || lastTerminationReason == CoordinationTerminationReason.NoValidCandidates)
                        {
                            break;
                        }
                    }
                }
            }

            metrics.TotalSimTime = simTime;

            return new SimHarnessResult(
                transcript,
                metrics,
                _player.State.Position,
                simTime,
                lastTerminationReason,
                completedSuccessfully: true);
        }

        private static List<Vec3> ExtractWaypoints(PathResult? path, Vec3 exactTokenPosition, FollowerWaypointPolicy policy, Vec3 playerPos, INavGrid grid)
        {
            var list = new List<Vec3>();
            if (path == null || path.Waypoints.Count == 0)
            {
                list.Add(exactTokenPosition);
                return list;
            }

            list.AddRange(path.Waypoints);
            // Replace the LAST waypoint with the exact token position
            list[list.Count - 1] = exactTokenPosition;

            // Follower policy: skip the first waypoint (snapped cell center) if straight line to second waypoint is strictly walkable
            if (policy == FollowerWaypointPolicy.SkipFirstIfWalkable && list.Count >= 2)
            {
                if (IsWalkableSegment(grid, playerPos, list[1]))
                {
                    list.RemoveAt(0);
                }
            }

            return list;
        }

        public static bool IsWalkableSegment(INavGrid grid, Vec3 from, Vec3 to)
        {
            var startCell = grid.WorldToGrid(from);
            var endCell = grid.WorldToGrid(to);

            if (!grid.IsInBounds(startCell) || !grid.IsWalkable(startCell)) return false;
            if (!grid.IsInBounds(endCell) || !grid.IsWalkable(endCell)) return false;

            float dx = to.X - from.X;
            float dz = to.Z - from.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 1e-4f) return true;

            float stepSize = Math.Min(grid.CellSize * 0.25f, 1.0f);
            int numSteps = (int)Math.Ceiling(dist / stepSize) + 1;

            GridPosition prevCell = startCell;

            for (int i = 0; i <= numSteps; i++)
            {
                float t = (float)i / numSteps;
                var samplePos = new Vec3(from.X + t * dx, 0f, from.Z + t * dz);
                var cell = grid.WorldToGrid(samplePos);

                if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
                {
                    return false;
                }

                // Strict diagonal corner-cutting guard: if crossing diagonally between adjacent cells
                if (Math.Abs(cell.X - prevCell.X) == 1 && Math.Abs(cell.Z - prevCell.Z) == 1)
                {
                    var adj1 = new GridPosition(prevCell.X, cell.Z);
                    var adj2 = new GridPosition(cell.X, prevCell.Z);
                    if (!grid.IsInBounds(adj1) || !grid.IsWalkable(adj1) ||
                        !grid.IsInBounds(adj2) || !grid.IsWalkable(adj2))
                    {
                        return false;
                    }
                }

                prevCell = cell;
            }

            return true;
        }

        public static bool TrySnapToNearestWalkable(INavGrid grid, Vec3 playerPos, GridPosition center, out GridPosition bestPos)
        {
            for (int r = 1; r <= 2; r++)
            {
                if (TryFindBestInRing(grid, playerPos, center, r, out bestPos))
                {
                    return true;
                }
            }
            bestPos = center;
            return false;
        }

        private static bool TryFindBestInRing(INavGrid grid, Vec3 playerPos, GridPosition center, int radius, out GridPosition bestPos)
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
                    if (!grid.IsInBounds(pos) || !grid.IsWalkable(pos))
                        continue;

                    Vec3 cellCenter = grid.GridToWorld(pos);
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

        private static bool IsPathBlocked(IReadOnlyList<Vec3> waypoints, int currentIndex, INavGrid grid)
        {
            for (int i = currentIndex; i < waypoints.Count; i++)
            {
                if (!grid.IsWalkable(grid.WorldToGrid(waypoints[i])))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
