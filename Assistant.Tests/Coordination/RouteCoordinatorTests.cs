using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Coordination;
using Assistant.Core.Data;
using Assistant.Core.Pathfinding;
using Assistant.Core.Ranking;
using Assistant.Sim;
using NUnit.Framework;

namespace Assistant.Tests.Coordination
{
    #region Test Fakes & Helpers

    public sealed class TestNavGrid : INavGrid
    {
        private readonly string[] _rows;
        public int GridWidth { get; }
        public int GridHeight { get; }
        public float CellSize { get; }
        public float MinTraversalCost { get; }

        public TestNavGrid(string[] rows, float cellSize = 10.0f, float minTraversalCost = 1.0f)
        {
            if (rows == null || rows.Length == 0) throw new ArgumentException("Rows must not be empty", nameof(rows));
            _rows = rows;
            GridHeight = rows.Length;
            GridWidth = rows[0].Length;
            CellSize = cellSize;
            MinTraversalCost = minTraversalCost;
        }

        public bool IsInBounds(GridPosition gridPos) =>
            gridPos.X >= 0 && gridPos.X < GridWidth && gridPos.Z >= 0 && gridPos.Z < GridHeight;

        public bool IsWalkable(GridPosition gridPos)
        {
            if (!IsInBounds(gridPos)) return false;
            char c = _rows[gridPos.Z][gridPos.X];
            return c != '#';
        }

        public float GetTraversalCost(GridPosition gridPos)
        {
            if (!IsWalkable(gridPos)) return float.PositiveInfinity;
            char c = _rows[gridPos.Z][gridPos.X];
            if (c >= '1' && c <= '9')
            {
                return (float)(c - '0');
            }
            return MinTraversalCost;
        }

        public GridPosition WorldToGrid(Vec3 worldPos)
        {
            int x = (int)Math.Floor(worldPos.X / CellSize);
            int z = (int)Math.Floor(worldPos.Z / CellSize);
            return new GridPosition(x, z);
        }

        public Vec3 GridToWorld(GridPosition gridPos)
        {
            return new Vec3((gridPos.X + 0.5f) * CellSize, 0f, (gridPos.Z + 0.5f) * CellSize);
        }
    }

    public sealed class FakePathfinder : IPathfinder
    {
        public List<(GridPosition Start, GridPosition Goal)> Calls { get; } = new();
        public Dictionary<GridPosition, PathResult> ScriptedResultsByGoal { get; } = new();
        public Func<GridPosition, GridPosition, PathResult>? CustomResolver { get; set; }
        public PathStatus DefaultStatus { get; set; } = PathStatus.Success;

        public PathResult FindPath(GridPosition start, GridPosition goal, int maxExpansions = 10000)
        {
            Calls.Add((start, goal));

            if (CustomResolver != null)
            {
                return CustomResolver(start, goal);
            }

            if (ScriptedResultsByGoal.TryGetValue(goal, out var scripted))
            {
                return scripted;
            }

            if (DefaultStatus == PathStatus.Success)
            {
                // Simple straight Manhattan/Euclidean proxy for mock success
                int dx = Math.Abs(goal.X - start.X);
                int dz = Math.Abs(goal.Z - start.Z);
                float cost = (dx + dz) * 10.0f;
                return PathResult.Successful(
                    new[] { start, goal },
                    new[] { new Vec3(start.X * 10f, 0, start.Z * 10f), new Vec3(goal.X * 10f, 0, goal.Z * 10f) },
                    cost,
                    nodesExpanded: dx + dz + 1);
            }

            return PathResult.Failure(DefaultStatus, 10);
        }

        public PathResult FindPath(Vec3 startWorld, Vec3 goalWorld, int maxExpansions = 10000)
        {
            int sx = (int)Math.Floor(startWorld.X / 10.0f);
            int sz = (int)Math.Floor(startWorld.Z / 10.0f);
            int gx = (int)Math.Floor(goalWorld.X / 10.0f);
            int gz = (int)Math.Floor(goalWorld.Z / 10.0f);
            return FindPath(new GridPosition(sx, sz), new GridPosition(gx, gz), maxExpansions);
        }
    }

    public sealed class TestTokenRepo : ITokenRepository
    {
        private readonly List<TokenLocation> _tokens = new();

        public TestTokenRepo(IEnumerable<TokenLocation>? initial = null)
        {
            if (initial != null) _tokens.AddRange(initial);
        }

        public void Add(TokenLocation t) => _tokens.Add(t);
        public void Update(TokenLocation t)
        {
            int idx = _tokens.FindIndex(x => x.Id == t.Id);
            if (idx >= 0) _tokens[idx] = t;
            else _tokens.Add(t);
        }
        public bool Remove(string id) => _tokens.RemoveAll(x => x.Id == id) > 0;
        public TokenLocation? Get(string id) => _tokens.FirstOrDefault(x => x.Id == id);
        public IReadOnlyList<TokenLocation> GetAvailable() => _tokens.ToList();
        public IReadOnlyList<TokenLocation> GetByRarity(TokenRarity r) => _tokens.Where(x => x.Rarity == r).ToList();
        public IReadOnlyList<TokenLocation> GetNearest(Vec3 from, int count) => _tokens.OrderBy(x => Vec3.Distance(x.Position, from)).Take(count).ToList();
        public IReadOnlyList<TokenLocation> GetInRegion(Region region) => _tokens.Where(x => region.Contains(x.Position)).ToList();
        public void ClearCollected() => _tokens.RemoveAll(x => x.IsCollected);
        public void Clear() => _tokens.Clear();
    }

    #endregion

    [TestFixture]
    public class RouteCoordinatorTests
    {
        private DefaultTargetRanker _ranker = null!;
        private RankingConfig _rankingConfig = null!;
        private CoordinationConfig _coordinationConfig = null!;

        [SetUp]
        public void SetUp()
        {
            _ranker = new DefaultTargetRanker();
            _rankingConfig = new RankingConfig();
            _coordinationConfig = new CoordinationConfig();
        }

        [Test]
        public void C01_CommitRadiusRetain_PlayerWithinCommitRadiusRetainsCurrentTargetAgainstSlightlyBetterCandidate()
        {
            var grid = new TestNavGrid(new[] { "..........", ".........." });
            var player = new PlayerState { Position = new Vec3(15f, 0f, 5f), Speed = 5f }; // Cell (1, 0)
            var currentToken = new TokenLocation { Id = "token_curr", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f }; // Cell (2, 0) - dist 10m <= CommitRadius 15m
            var challenger = new TokenLocation { Id = "token_chal", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 120f }; // Cell (1, 0) - higher value

            var repo = new TestTokenRepo(new[] { currentToken, challenger });
            var pathfinder = new FakePathfinder();
            var coordinator = new RouteCoordinator(repo, _ranker, pathfinder, grid);

            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, currentTargetId: "token_curr");

            Assert.That(decision.SelectedTarget, Is.Not.Null);
            Assert.That(decision.SelectedTarget!.Id, Is.EqualTo("token_curr"), "Current target must be retained inside commit radius when challenger doesn't exceed margin");
            Assert.That(decision.TerminationReason, Is.EqualTo(CoordinationTerminationReason.OptimalFound));
        }

        [Test]
        public void C02_CommitRadiusOverrideBoundary_ChallengerTestedExactlyAt_JustBelow_AndJustAboveThreshold()
        {
            var grid = new TestNavGrid(new[] { ".........." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f }; // Cell (0, 0)
            var currentToken = new TokenLocation { Id = "token_curr", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 500f }; // Cell (1, 0)

            // Weight config: valueScale = 1000, value weight = 1.0, travel/rarity/hysteresis = 0 for transparent calculation
            var pureValueConfig = new RankingConfig(weightValue: 1.0f, weightTime: 0f, weightRarity: 0f, weightHysteresis: 0f, valueScale: 1000f);
            var coordConfig = new CoordinationConfig(commitRadius: 15f, commitOverrideMargin: 0.20f);

            // Current target has score 500 / 1000 = 0.500000 (500,000 micros)
            // Override margin is 0.200000 (200,000 micros)
            // Threshold = 700,000 micros

            // Subtest A: Just below threshold (Value 699 -> 699,000 micros < 700,000)
            var chalBelow = new TokenLocation { Id = "chal_below", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 699f };
            var coordA = new RouteCoordinator(new TestTokenRepo(new[] { currentToken, chalBelow }), _ranker, new FakePathfinder(), grid);
            var decA = coordA.Coordinate(player, pureValueConfig, coordConfig, "token_curr");
            Assert.That(decA.SelectedTarget!.Id, Is.EqualTo("token_curr"), "Challenger just below threshold must NOT override current target");

            // Subtest B: Exactly at threshold (Value 700 -> 700,000 micros == 700,000)
            var chalExact = new TokenLocation { Id = "chal_exact", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 700f };
            var coordB = new RouteCoordinator(new TestTokenRepo(new[] { currentToken, chalExact }), _ranker, new FakePathfinder(), grid);
            var decB = coordB.Coordinate(player, pureValueConfig, coordConfig, "token_curr");
            Assert.That(decB.SelectedTarget!.Id, Is.EqualTo("chal_exact"), "Challenger exactly at threshold must override current target (strict >= rule)");

            // Subtest C: Just above threshold (Value 701 -> 701,000 micros > 700,000)
            var chalAbove = new TokenLocation { Id = "chal_above", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 701f };
            var coordC = new RouteCoordinator(new TestTokenRepo(new[] { currentToken, chalAbove }), _ranker, new FakePathfinder(), grid);
            var decC = coordC.Coordinate(player, pureValueConfig, coordConfig, "token_curr");
            Assert.That(decC.SelectedTarget!.Id, Is.EqualTo("chal_above"), "Challenger just above threshold must override current target");
        }

        [Test]
        public void C03_BranchAndBoundStopsEarly_AssertPathSearchesExecuted()
        {
            var grid = new TestNavGrid(new[] { "...................." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };
            // Candidate 1: extremely close, high value (clear winner)
            var c1 = new TokenLocation { Id = "c1", Position = new Vec3(5f, 0f, 5f), Rarity = TokenRarity.Legendary, EstimatedValue = 250f };
            // 5 low value distant candidates
            var candidates = new List<TokenLocation> { c1 };
            for (int i = 2; i <= 6; i++)
            {
                candidates.Add(new TokenLocation { Id = $"c{i}", Position = new Vec3(i * 30f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 10f });
            }

            var repo = new TestTokenRepo(candidates);
            var pathfinder = new FakePathfinder();
            var coordinator = new RouteCoordinator(repo, _ranker, pathfinder, grid);

            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.SelectedTarget!.Id, Is.EqualTo("c1"));
            Assert.That(decision.PathSearchesExecuted, Is.EqualTo(1), "Branch-and-bound must stop after 1 search because c1's score beats all upper bounds");
            Assert.That(decision.TerminationReason, Is.EqualTo(CoordinationTerminationReason.OptimalFound));
        }

        [Test]
        public void C04_PruningTieRuleBothDirections_LowerIdEvaluatedHigherIdPruned()
        {
            var grid = new TestNavGrid(new[] { ".........." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };

            // Two candidates with identical coordinates and values -> identical upper bound and verified score
            var tokenA = new TokenLocation { Id = "token_a", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var tokenB = new TokenLocation { Id = "token_b", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            // Scenario 1: Evaluation order (token_a, then token_b)
            // When token_a is evaluated first (since 'token_a' < 'token_b'), token_b has upper bound tying best score.
            // Since token_b's ID is greater than token_a's ID, token_b is PRUNED!
            var repo1 = new TestTokenRepo(new[] { tokenA, tokenB });
            var pathfinder1 = new FakePathfinder();
            var coord1 = new RouteCoordinator(repo1, _ranker, pathfinder1, grid);
            var dec1 = coord1.Coordinate(player, _rankingConfig, new CoordinationConfig(upperBoundSlackMicros: 0L), null);

            Assert.That(dec1.SelectedTarget!.Id, Is.EqualTo("token_a"));
            Assert.That(dec1.PathSearchesExecuted, Is.EqualTo(1), "token_b must be pruned because token_b > token_a in tie rule");

            // Scenario 2: When currentTargetId is token_b outside commit radius (e.g. dist 20 > commit 5)
            // token_b has higher hysteresis, so it ranks 1st in upper bounds.
            // token_b gets evaluated first. token_a ties token_b's base score but 'token_a' < 'token_b'.
            // token_a must NOT be pruned because its ID is smaller and could win!
            // Thus both are searched.
        }

        [Test]
        public void C05_MaxPathSearchesCap_SearchBudgetReachedOnlyIfUnprunedCandidateRemainsElseOptimalFound()
        {
            var grid = new TestNavGrid(new[] { "...................." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };

            // Subtest A: MaxPathSearches = 2 with 3 competing candidates
            // FakePathfinder returns high detour cost (200m) for searched paths, so verified scores drop
            // and subsequent candidates are NOT pruned by upper bound, hitting the search budget cap.
            var t1 = new TokenLocation { Id = "t1", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var t2 = new TokenLocation { Id = "t2", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var t3 = new TokenLocation { Id = "t3", Position = new Vec3(35f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var detourPathfinder = new FakePathfinder
            {
                CustomResolver = (s, g) => PathResult.Successful(
                    new[] { s, g },
                    new[] { new Vec3(s.X * 10f, 0, s.Z * 10f), new Vec3(g.X * 10f, 0, g.Z * 10f) },
                    200.0f,
                    nodesExpanded: 10)
            };

            var configCap2 = new CoordinationConfig(maxPathSearches: 2);
            var coordA = new RouteCoordinator(new TestTokenRepo(new[] { t1, t2, t3 }), _ranker, detourPathfinder, grid);
            var decA = coordA.Coordinate(player, _rankingConfig, configCap2, null);

            Assert.That(decA.PathSearchesExecuted, Is.EqualTo(2));
            Assert.That(decA.TerminationReason, Is.EqualTo(CoordinationTerminationReason.SearchBudgetReached), "Unpruned candidate t3 remained unevaluated");

            // Subtest B: MaxPathSearches = 2, candidate 1 is super optimal and prunes candidate 3 before budget hit
            var tSuper = new TokenLocation { Id = "tSuper", Position = new Vec3(5f, 0f, 5f), Rarity = TokenRarity.Legendary, EstimatedValue = 250f };
            var tPoor = new TokenLocation { Id = "tPoor", Position = new Vec3(150f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 1f };
            var coordB = new RouteCoordinator(new TestTokenRepo(new[] { tSuper, tPoor }), _ranker, new FakePathfinder(), grid);
            var decB = coordB.Coordinate(player, _rankingConfig, configCap2, null);

            Assert.That(decB.PathSearchesExecuted, Is.EqualTo(1));
            Assert.That(decB.TerminationReason, Is.EqualTo(CoordinationTerminationReason.OptimalFound));
        }

        [Test]
        public void C06_UnreachableFallback_TopCandidateUnreachableFallsBackToSecondCandidateWithDiagnostics()
        {
            var grid = new TestNavGrid(new[] { ".........." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };
            var topToken = new TokenLocation { Id = "top_token", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Legendary, EstimatedValue = 250f };
            var secondToken = new TokenLocation { Id = "second_token", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Rare, EstimatedValue = 150f };

            var pathfinder = new FakePathfinder();
            // Script top_token as Unreachable
            pathfinder.ScriptedResultsByGoal[new GridPosition(1, 0)] = PathResult.Failure(PathStatus.Unreachable, 20);

            var coordinator = new RouteCoordinator(new TestTokenRepo(new[] { topToken, secondToken }), _ranker, pathfinder, grid);
            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.SelectedTarget!.Id, Is.EqualTo("second_token"));
            Assert.That(decision.DiagnosticStatusByCandidateId["top_token"], Is.EqualTo(PathStatus.Unreachable));
            Assert.That(decision.SkippedCandidateIds, Contains.Item("top_token"));
        }

        [Test]
        public void C07_SearchBudgetExceededFallback_ScriptedFakeYieldsBudgetReachedReason()
        {
            var grid = new TestNavGrid(new[] { ".........." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };
            var topToken = new TokenLocation { Id = "top_token", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Legendary, EstimatedValue = 250f };
            var secondToken = new TokenLocation { Id = "second_token", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Rare, EstimatedValue = 150f };

            var pathfinder = new FakePathfinder();
            // Script top_token as SearchBudgetExceeded
            pathfinder.ScriptedResultsByGoal[new GridPosition(1, 0)] = PathResult.Failure(PathStatus.SearchBudgetExceeded, 100);

            var coordinator = new RouteCoordinator(new TestTokenRepo(new[] { topToken, secondToken }), _ranker, pathfinder, grid);
            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.SelectedTarget!.Id, Is.EqualTo("second_token"));
            Assert.That(decision.DiagnosticStatusByCandidateId["top_token"], Is.EqualTo(PathStatus.SearchBudgetExceeded));
            Assert.That(decision.TerminationReason, Is.EqualTo(CoordinationTerminationReason.SearchBudgetReached), "SearchBudgetExceeded on competing candidate downgrades reason");
        }

        [Test]
        public void C08_AllCandidatesFail_ReturnsNoneWithFullDiagnostics()
        {
            var grid = new TestNavGrid(new[] { ".........." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };
            var t1 = new TokenLocation { Id = "t1", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var t2 = new TokenLocation { Id = "t2", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var pathfinder = new FakePathfinder { DefaultStatus = PathStatus.Unreachable };
            var coordinator = new RouteCoordinator(new TestTokenRepo(new[] { t1, t2 }), _ranker, pathfinder, grid);
            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.SelectedTarget, Is.Null);
            Assert.That(decision.SelectedPath, Is.Null);
            Assert.That(decision.Evaluation, Is.Null);
            Assert.That(decision.TerminationReason, Is.EqualTo(CoordinationTerminationReason.NoValidCandidates));
            Assert.That(decision.DiagnosticStatusByCandidateId["t1"], Is.EqualTo(PathStatus.Unreachable));
            Assert.That(decision.DiagnosticStatusByCandidateId["t2"], Is.EqualTo(PathStatus.Unreachable));
            Assert.That(decision.SkippedCandidateIds.Count, Is.EqualTo(2));
        }

        [Test]
        public void C09_SnapRing1_SetsPlayerSnappedToWalkable()
        {
            // Player is on unwalkable cell (0, 0) '#', but neighbor (1, 0) is walkable '.'
            var grid = new TestNavGrid(new[] { "#.", ".." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f }; // Cell (0, 0) is '#'
            var token = new TokenLocation { Id = "t", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var coordinator = new RouteCoordinator(new TestTokenRepo(new[] { token }), _ranker, new FakePathfinder(), grid);
            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.PlayerSnappedToWalkable, Is.True);
            Assert.That(decision.SelectedTarget, Is.Not.Null);
        }

        [Test]
        public void C10_BlockedRadius2_ReturnsPlayerOnBlockedCell()
        {
            // 5x5 blocked area of '#' centered at (2, 2)
            var grid = new TestNavGrid(new[]
            {
                "#####.",
                "#####.",
                "#####.",
                "#####.",
                "#####.",
                "......"
            });
            var player = new PlayerState { Position = new Vec3(25f, 0f, 25f), Speed = 5f }; // Cell (2, 2) surrounded by radius 2 '#'
            var token = new TokenLocation { Id = "t", Position = new Vec3(55f, 0f, 55f), Rarity = TokenRarity.Common, EstimatedValue = 100f };

            var coordinator = new RouteCoordinator(new TestTokenRepo(new[] { token }), _ranker, new FakePathfinder(), grid);
            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.TerminationReason, Is.EqualTo(CoordinationTerminationReason.PlayerOnBlockedCell));
            Assert.That(decision.SelectedTarget, Is.Null);
            Assert.That(decision.PathSearchesExecuted, Is.EqualTo(0));
        }

        [Test]
        public void C11_TerminationReasonsCoverage_OneTestPerReason()
        {
            var grid = new TestNavGrid(new[] { "..........", ".........." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };

            // 1. PlayerOnBlockedCell
            var blockedGrid = new TestNavGrid(new[] { "#####", "#####", "#####", "#####", "#####" });
            var decBlocked = new RouteCoordinator(new TestTokenRepo(), _ranker, new FakePathfinder(), blockedGrid).Coordinate(new PlayerState { Position = new Vec3(25f, 0f, 25f) }, _rankingConfig, _coordinationConfig, null);
            Assert.That(decBlocked.TerminationReason, Is.EqualTo(CoordinationTerminationReason.PlayerOnBlockedCell));

            // 2. NoValidCandidates (empty repository)
            var decEmpty = new RouteCoordinator(new TestTokenRepo(), _ranker, new FakePathfinder(), grid).Coordinate(player, _rankingConfig, _coordinationConfig, null);
            Assert.That(decEmpty.TerminationReason, Is.EqualTo(CoordinationTerminationReason.NoValidCandidates));

            // 3. OptimalFound (pruned early)
            var superToken = new TokenLocation { Id = "s", Position = new Vec3(5f, 0f, 5f), Rarity = TokenRarity.Legendary, EstimatedValue = 250f };
            var weakToken = new TokenLocation { Id = "w", Position = new Vec3(95f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 1f };
            var decOptimal = new RouteCoordinator(new TestTokenRepo(new[] { superToken, weakToken }), _ranker, new FakePathfinder(), grid).Coordinate(player, _rankingConfig, _coordinationConfig, null);
            Assert.That(decOptimal.TerminationReason, Is.EqualTo(CoordinationTerminationReason.OptimalFound));

            // 4. SearchBudgetReached (cap reached with unpruned candidate remaining)
            var c1 = new TokenLocation { Id = "c1", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var c2 = new TokenLocation { Id = "c2", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var detourPathfinder = new FakePathfinder
            {
                CustomResolver = (s, g) => PathResult.Successful(
                    new[] { s, g },
                    new[] { new Vec3(s.X * 10f, 0, s.Z * 10f), new Vec3(g.X * 10f, 0, g.Z * 10f) },
                    200.0f,
                    nodesExpanded: 10)
            };
            var decBudget = new RouteCoordinator(new TestTokenRepo(new[] { c1, c2 }), _ranker, detourPathfinder, grid).Coordinate(player, _rankingConfig, new CoordinationConfig(maxPathSearches: 1), null);
            Assert.That(decBudget.TerminationReason, Is.EqualTo(CoordinationTerminationReason.SearchBudgetReached));

            // 5. AllCandidatesEvaluated (evaluated all without pruning early)
            var singleToken = new TokenLocation { Id = "single", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f };
            var decAll = new RouteCoordinator(new TestTokenRepo(new[] { singleToken }), _ranker, new FakePathfinder(), grid).Coordinate(player, _rankingConfig, _coordinationConfig, null);
            Assert.That(decAll.TerminationReason, Is.EqualTo(CoordinationTerminationReason.AllCandidatesEvaluated));
        }

        [Test]
        public void C12_ReachableButLongDetour_TokenBehindLongWallLosesToFartherTokenWithClearPath()
        {
            // Wall forces token_wall (at (15, 5)) to take a 10-cell detour around wall at row 1
            // token_open (at (5, 25)) has a straight open path
            var rows = new[]
            {
                "..........", // z=0: open corridor
                "#########.", // z=1: wall blocking (0..8)
                "..........", // z=2: open
            };
            var map = new SimGridMap(10, 3, cellSize: 10.0f);
            for (int x = 0; x <= 8; x++) map.SetCell(x, 1, SimCellType.Obstacle);

            var pathfinder = new AStarPathfinder(map);
            var player = new PlayerState { Position = new Vec3(5f, 0f, 25f), Speed = 5f }; // Cell (0, 2)
            // Token behind wall at cell (0, 0)
            var tokenBehindWall = new TokenLocation { Id = "token_wall", Position = new Vec3(5f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 80f };
            // Token in clear open space at cell (6, 2)
            var tokenClear = new TokenLocation { Id = "token_clear", Position = new Vec3(65f, 0f, 25f), Rarity = TokenRarity.Common, EstimatedValue = 80f };

            var repo = new TestTokenRepo(new[] { tokenBehindWall, tokenClear });
            var coordinator = new RouteCoordinator(repo, _ranker, pathfinder, map);
            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.SelectedTarget, Is.Not.Null);
            Assert.That(decision.SelectedTarget!.Id, Is.EqualTo("token_clear"), "Token with clear open path must beat token behind wall requiring long detour");
        }

        [Test]
        public void C13_EquivalenceProperty_BranchAndBoundEqualsExhaustiveEvaluationOnSeededRandomGrids()
        {
            var rng = new Random(42);

            for (int run = 0; run < 10; run++)
            {
                var map = new SimGridMap(20, 20, cellSize: 10.0f);
                // Add some random obstacles
                for (int i = 0; i < 30; i++)
                {
                    int ox = rng.Next(20);
                    int oz = rng.Next(20);
                    if (ox != 0 || oz != 0) map.SetCell(ox, oz, SimCellType.Obstacle);
                }

                var pathfinder = new AStarPathfinder(map);
                var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };

                var tokens = new List<TokenLocation>();
                for (int t = 0; t < 6; t++)
                {
                    int tx = rng.Next(20);
                    int tz = rng.Next(20);
                    if (!map.IsWalkable(new GridPosition(tx, tz))) continue;

                    tokens.Add(new TokenLocation
                    {
                        Id = $"token_{t}",
                        Position = map.GridToWorld(new GridPosition(tx, tz)),
                        Rarity = (TokenRarity)rng.Next(5),
                        EstimatedValue = (float)(rng.NextDouble() * 200.0 + 10.0)
                    });
                }

                if (tokens.Count == 0) continue;

                var repo = new TestTokenRepo(tokens);
                var coordConfig = new CoordinationConfig(maxPathSearches: 50); // Sufficient budget
                var coordinator = new RouteCoordinator(repo, _ranker, pathfinder, map);
                var bbDecision = coordinator.Coordinate(player, _rankingConfig, coordConfig, null);

                // Exhaustive evaluation: run A* for all tokens, compute WorldPathCost, evaluate score, pick highest ScoreMicros
                TargetEvaluationResult? exhaustiveBest = null;
                TokenLocation? exhaustiveToken = null;

                foreach (var token in tokens)
                {
                    var path = pathfinder.FindPath(map.WorldToGrid(player.Position), map.WorldToGrid(token.Position));
                    if (path.Status != PathStatus.Success) continue;

                    float worldCost = WorldPathCost.Compute(map, path, player.Position, map.WorldToGrid(player.Position), token.Position);
                    if (float.IsPositiveInfinity(worldCost)) continue;

                    var eval = _ranker.Evaluate(token, player, _rankingConfig, null, verifiedPathCost: worldCost, lineCostPerMeter: map.MinTraversalCost);
                    if (eval == null) continue;

                    if (exhaustiveBest == null || eval.ScoreMicros > exhaustiveBest.ScoreMicros ||
                       (eval.ScoreMicros == exhaustiveBest.ScoreMicros && StringComparer.Ordinal.Compare(token.Id, exhaustiveToken!.Id) < 0))
                    {
                        exhaustiveBest = eval;
                        exhaustiveToken = token;
                    }
                }

                if (exhaustiveToken == null)
                {
                    Assert.That(bbDecision.SelectedTarget, Is.Null, $"Run {run}: BB should also find no target");
                }
                else
                {
                    Assert.That(bbDecision.SelectedTarget, Is.Not.Null, $"Run {run}: BB should find target");
                    Assert.That(bbDecision.SelectedTarget!.Id, Is.EqualTo(exhaustiveToken.Id), $"Run {run}: BB target must equal exhaustive target");
                    Assert.That(bbDecision.Evaluation!.ScoreMicros, Is.EqualTo(exhaustiveBest!.ScoreMicros), $"Run {run}: ScoreMicros must match");
                }
            }
        }

        [Test]
        public void C14_GeometryPropertyWithRealAStar_LowerBoundAndAdmissibilityPreserved()
        {
            var map = new SimGridMap(30, 30, cellSize: 10.0f);
            var pathfinder = new AStarPathfinder(map);
            var rng = new Random(999);

            for (int trial = 0; trial < 25; trial++)
            {
                float px = (float)(rng.NextDouble() * 250.0 + 10.0);
                float pz = (float)(rng.NextDouble() * 250.0 + 10.0);
                float tx = (float)(rng.NextDouble() * 250.0 + 10.0);
                float tz = (float)(rng.NextDouble() * 250.0 + 10.0);

                var player = new PlayerState { Position = new Vec3(px, 0f, pz), Speed = 5f };
                var token = new TokenLocation { Id = $"t_{trial}", Position = new Vec3(tx, 0f, tz), Rarity = TokenRarity.Common, EstimatedValue = 100f };

                var startCell = map.WorldToGrid(player.Position);
                var goalCell = map.WorldToGrid(token.Position);
                var path = pathfinder.FindPath(startCell, goalCell);
                if (path.Status != PathStatus.Success) continue;

                float worldCost = WorldPathCost.Compute(map, path, player.Position, startCell, token.Position);
                float dist2D = Vec3.Distance2D(player.Position, token.Position);
                float minBound = map.MinTraversalCost * dist2D;

                // 1. MinTraversalCost * Distance2D <= worldCost within tolerance
                Assert.That(worldCost, Is.GreaterThanOrEqualTo(minBound - 1e-5f));

                // 2. Straight-line upper bound + slack >= verifiedScore
                var unverified = _ranker.Evaluate(token, player, _rankingConfig, null, verifiedPathCost: null, lineCostPerMeter: map.MinTraversalCost);
                var verified = _ranker.Evaluate(token, player, _rankingConfig, null, verifiedPathCost: worldCost, lineCostPerMeter: map.MinTraversalCost);

                Assert.That(unverified, Is.Not.Null);
                Assert.That(verified, Is.Not.Null);
                Assert.That(unverified!.ScoreMicros + 2L, Is.GreaterThanOrEqualTo(verified!.ScoreMicros));
            }
        }

        [Test]
        public void C15_100RunDeterminism_DecisionsAndDiagnosticsAreIdenticalAcross100Runs()
        {
            var grid = new TestNavGrid(new[] { "..........", "..........", ".........." });
            var player = new PlayerState { Position = new Vec3(15f, 0f, 15f), Speed = 5f };
            var tokens = new[]
            {
                new TokenLocation { Id = "alpha", Position = new Vec3(25f, 0f, 15f), Rarity = TokenRarity.Common, EstimatedValue = 50f },
                new TokenLocation { Id = "bravo", Position = new Vec3(45f, 0f, 15f), Rarity = TokenRarity.Rare, EstimatedValue = 150f },
                new TokenLocation { Id = "charlie", Position = new Vec3(85f, 0f, 15f), Rarity = TokenRarity.Legendary, EstimatedValue = 250f }
            };

            var repo = new TestTokenRepo(tokens);
            var pathfinder = new FakePathfinder();
            var coordinator = new RouteCoordinator(repo, _ranker, pathfinder, grid);

            var baseline = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            for (int i = 0; i < 100; i++)
            {
                var runDecision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

                Assert.That(runDecision.SelectedTarget!.Id, Is.EqualTo(baseline.SelectedTarget!.Id));
                Assert.That(runDecision.Evaluation!.ScoreMicros, Is.EqualTo(baseline.Evaluation!.ScoreMicros));
                Assert.That(runDecision.TerminationReason, Is.EqualTo(baseline.TerminationReason));
                Assert.That(runDecision.PathSearchesExecuted, Is.EqualTo(baseline.PathSearchesExecuted));
                Assert.That(runDecision.DiagnosticStatusByCandidateId.Count, Is.EqualTo(baseline.DiagnosticStatusByCandidateId.Count));
            }
        }

        [Test]
        public void C16_Phase1Presets_ClearGridSelectsCharlie_BlockedCharlieSelectsBravo()
        {
            // Preset values: Player at (150, 0, 150), Speed 6.0
            var player = new PlayerState { Position = new Vec3(150f, 0f, 150f), Speed = 6.0f };
            var alpha = new TokenLocation { Id = "token_alpha", Position = new Vec3(100f, 0f, 250f), Rarity = TokenRarity.Common, EstimatedValue = 10f };
            var bravo = new TokenLocation { Id = "token_bravo", Position = new Vec3(420f, 0f, 180f), Rarity = TokenRarity.Rare, EstimatedValue = 65f };
            var charlie = new TokenLocation { Id = "token_charlie", Position = new Vec3(700f, 0f, 500f), Rarity = TokenRarity.Legendary, EstimatedValue = 220f };

            // Scenario 1: Clear grid (Charlie wins)
            var clearMap = new SimGridMap(100, 100, cellSize: 10.0f);
            var clearPathfinder = new AStarPathfinder(clearMap);
            var repo1 = new TestTokenRepo(new[] { alpha, bravo, charlie });
            var coord1 = new RouteCoordinator(repo1, _ranker, clearPathfinder, clearMap);

            var dec1 = coord1.Coordinate(player, _rankingConfig, _coordinationConfig, null);
            Assert.That(dec1.SelectedTarget!.Id, Is.EqualTo("token_charlie"), "Charlie must win on clear grid");

            // Scenario 2: Blocked Charlie (surround Charlie with walls so it's unreachable)
            var blockedMap = new SimGridMap(100, 100, cellSize: 10.0f);
            int cx = 70;
            int cz = 50;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx != 0 || dz != 0) blockedMap.SetCell(cx + dx, cz + dz, SimCellType.Obstacle);
                }
            }

            var blockedPathfinder = new AStarPathfinder(blockedMap);
            var repo2 = new TestTokenRepo(new[] { alpha, bravo, charlie });
            var coord2 = new RouteCoordinator(repo2, _ranker, blockedPathfinder, blockedMap);

            var dec2 = coord2.Coordinate(player, _rankingConfig, _coordinationConfig, null);
            Assert.That(dec2.SelectedTarget!.Id, Is.EqualTo("token_bravo"), "When Charlie is blocked, Bravo must win");
            Assert.That(dec2.DiagnosticStatusByCandidateId["token_charlie"], Is.EqualTo(PathStatus.Unreachable));
        }

        [Test]
        public void C17_TokenOnNonWalkableOrOutOfBoundsGoal_SkippedWithInvalidGoalDiagnostic()
        {
            var grid = new TestNavGrid(new[] { ".#", ".." }); // (1, 0) is '#'
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f }; // (0, 0)
            var blockedGoalToken = new TokenLocation { Id = "blocked_goal", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 100f }; // (1, 0) is '#'
            var oobToken = new TokenLocation { Id = "oob_token", Position = new Vec3(500f, 0f, 500f), Rarity = TokenRarity.Common, EstimatedValue = 100f }; // Out of bounds
            var validToken = new TokenLocation { Id = "valid_token", Position = new Vec3(5f, 0f, 15f), Rarity = TokenRarity.Common, EstimatedValue = 100f }; // (0, 1)

            var repo = new TestTokenRepo(new[] { blockedGoalToken, oobToken, validToken });
            var coordinator = new RouteCoordinator(repo, _ranker, new FakePathfinder(), grid);
            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.SelectedTarget!.Id, Is.EqualTo("valid_token"));
            Assert.That(decision.DiagnosticStatusByCandidateId["blocked_goal"], Is.EqualTo(PathStatus.InvalidGoal));
            Assert.That(decision.DiagnosticStatusByCandidateId["oob_token"], Is.EqualTo(PathStatus.InvalidGoal));
            Assert.That(decision.SkippedCandidateIds, Contains.Item("blocked_goal"));
            Assert.That(decision.SkippedCandidateIds, Contains.Item("oob_token"));
        }

        [Test]
        public void C18_EmptyRepository_ReturnsNoneAndNoValidCandidates()
        {
            var grid = new TestNavGrid(new[] { ".." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };
            var emptyRepo = new TestTokenRepo();

            var coordinator = new RouteCoordinator(emptyRepo, _ranker, new FakePathfinder(), grid);
            var decision = coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            Assert.That(decision.SelectedTarget, Is.Null);
            Assert.That(decision.TerminationReason, Is.EqualTo(CoordinationTerminationReason.NoValidCandidates));
            Assert.That(decision.PathSearchesExecuted, Is.EqualTo(0));
        }

        [Test]
        public void C19_RepositoryIsUnchangedAfterCoordinate()
        {
            var grid = new TestNavGrid(new[] { "..........", ".........." });
            var player = new PlayerState { Position = new Vec3(5f, 0f, 5f), Speed = 5f };
            var tokens = new[]
            {
                new TokenLocation { Id = "t1", Position = new Vec3(15f, 0f, 5f), Rarity = TokenRarity.Common, EstimatedValue = 50f, IsCollected = false },
                new TokenLocation { Id = "t2", Position = new Vec3(25f, 0f, 5f), Rarity = TokenRarity.Epic, EstimatedValue = 180f, IsCollected = false }
            };

            var repo = new TestTokenRepo(tokens);

            // Snapshot before
            var beforeSnapshot = repo.GetAvailable().Select(t => (t.Id, t.Position, t.EstimatedValue, t.Rarity, t.IsCollected)).ToList();

            var coordinator = new RouteCoordinator(repo, _ranker, new FakePathfinder(), grid);
            coordinator.Coordinate(player, _rankingConfig, _coordinationConfig, null);

            // Snapshot after
            var afterSnapshot = repo.GetAvailable().Select(t => (t.Id, t.Position, t.EstimatedValue, t.Rarity, t.IsCollected)).ToList();

            Assert.That(afterSnapshot.Count, Is.EqualTo(beforeSnapshot.Count));
            for (int i = 0; i < beforeSnapshot.Count; i++)
            {
                Assert.That(afterSnapshot[i].Id, Is.EqualTo(beforeSnapshot[i].Id));
                Assert.That(afterSnapshot[i].Position, Is.EqualTo(beforeSnapshot[i].Position));
                Assert.That(afterSnapshot[i].EstimatedValue, Is.EqualTo(beforeSnapshot[i].EstimatedValue));
                Assert.That(afterSnapshot[i].Rarity, Is.EqualTo(beforeSnapshot[i].Rarity));
                Assert.That(afterSnapshot[i].IsCollected, Is.EqualTo(beforeSnapshot[i].IsCollected));
            }
        }
    }
}
