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
    /// Deterministic Stage 3.4 simulation harness executing end-to-end loops with
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
                            currentWaypoints = ExtractWaypoints(decision.SelectedPath, decision.SelectedTarget.Position);
                            currentWaypointIndex = 0;
                        }
                        else if (newTargetId != currentTargetId)
                        {
                            metrics.TargetSwitches++;
                            transcript.Add(new SimTranscriptEvent(simTime, "TargetChanged", newTargetId, decision.SelectedTarget.EstimatedValue));
                            currentTargetId = newTargetId;
                            currentTargetToken = decision.SelectedTarget;
                            currentWaypoints = ExtractWaypoints(decision.SelectedPath, decision.SelectedTarget.Position);
                            currentWaypointIndex = 0;
                        }
                        else
                        {
                            // Incumbent retained: only update waypoints if current route was blocked or empty
                            if (currentWaypoints.Count == 0 || IsPathBlocked(currentWaypoints, currentWaypointIndex, _map))
                            {
                                currentWaypoints = ExtractWaypoints(decision.SelectedPath, decision.SelectedTarget.Position);
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
                else // NearestFirst baseline
                {
                    var sw = Stopwatch.StartNew();
                    metrics.CoordinateCalls++;
                    var available = _repo.GetAvailable();
                    var sorted = available
                        .OrderBy(t => Vec3.Distance2D(_player.State.Position, t.Position))
                        .ThenBy(t => t.Id, StringComparer.Ordinal)
                        .ToList();

                    TokenLocation? selected = null;
                    PathResult? selectedPath = null;
                    int searches = 0;

                    foreach (var candidate in sorted)
                    {
                        searches++;
                        var path = _pathfinder.FindPath(_player.State.Position, candidate.Position);
                        if (path.Status == PathStatus.Success)
                        {
                            selected = candidate;
                            selectedPath = path;
                            break;
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
                            currentWaypoints = ExtractWaypoints(selectedPath, selected.Position);
                            currentWaypointIndex = 0;
                        }
                        else if (newTargetId != currentTargetId)
                        {
                            metrics.TargetSwitches++;
                            transcript.Add(new SimTranscriptEvent(simTime, "TargetChanged", newTargetId, selected.EstimatedValue));
                            currentTargetId = newTargetId;
                            currentTargetToken = selected;
                            currentWaypoints = ExtractWaypoints(selectedPath, selected.Position);
                            currentWaypointIndex = 0;
                        }
                        else
                        {
                            if (currentWaypoints.Count == 0 || IsPathBlocked(currentWaypoints, currentWaypointIndex, _map))
                            {
                                currentWaypoints = ExtractWaypoints(selectedPath, selected.Position);
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

                // 1. Move player along current route
                if (currentWaypoints.Count > 0 && currentWaypointIndex < currentWaypoints.Count)
                {
                    _player.MoveTowards(currentWaypoints[currentWaypointIndex], _config.Dt);
                    if (Vec3.Distance(_player.State.Position, currentWaypoints[currentWaypointIndex]) < 0.001f)
                    {
                        currentWaypointIndex++;
                    }
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

                // 3. Check target removed or expired from repository
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
                bool timeDue = simTime - lastReplanTime >= _config.ReplanIntervalSeconds - 1e-5;
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

        private static List<Vec3> ExtractWaypoints(PathResult? path, Vec3 exactTokenPosition)
        {
            var list = new List<Vec3>();
            if (path == null || path.Waypoints.Count == 0)
            {
                list.Add(exactTokenPosition);
                return list;
            }

            list.AddRange(path.Waypoints);
            // KEEP the first waypoint, replace the LAST waypoint with the exact token position
            list[list.Count - 1] = exactTokenPosition;
            return list;
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
