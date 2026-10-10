using System;
using System.Collections.Generic;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;
using Assistant.Core.Pathfinding;
using Assistant.Sim;
using NUnit.Framework;

namespace Assistant.Tests.Pathfinding
{
    [TestFixture]
    public class AStarPathfinderTests
    {
        [Test]
        public void A01_Basic_StraightLinePathOnClearGridMatchesGeometricGridDistance()
        {
            var grid = new SimGridMap(20, 20, 10.0f);
            var pathfinder = new AStarPathfinder(grid);

            // Cardinal path: (0, 0) to (5, 0)
            var start = new GridPosition(0, 0);
            var goal = new GridPosition(5, 0);
            var result = pathfinder.FindPath(start, goal);

            Assert.That(result.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(result.Steps.Count, Is.EqualTo(6));
            Assert.That(result.Waypoints.Count, Is.EqualTo(6));
            Assert.That(result.Steps[0], Is.EqualTo(start));
            Assert.That(result.Steps[5], Is.EqualTo(goal));
            Assert.That(result.GridCost, Is.EqualTo(50.0f).Within(1e-4f));

            // Diagonal path: (0, 0) to (3, 3)
            var diagGoal = new GridPosition(3, 3);
            var diagResult = pathfinder.FindPath(start, diagGoal);

            Assert.That(diagResult.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(diagResult.Steps.Count, Is.EqualTo(4));
            float expectedDiagCost = 3.0f * 10.0f * (float)Math.Sqrt(2);
            Assert.That(diagResult.GridCost, Is.EqualTo(expectedDiagCost).Within(1e-4f));
        }

        [Test]
        public void A02_Obstacle_SingleWallObstacleNavigationCorrectlyRoutesAroundBarrier()
        {
            var grid = new SimGridMap(20, 20, 10.0f);
            // Place a vertical wall at X = 5 from Z = 2 to Z = 7
            for (int z = 2; z <= 7; z++)
            {
                grid.SetCell(5, z, SimCellType.Obstacle);
            }

            var pathfinder = new AStarPathfinder(grid);
            var start = new GridPosition(2, 4);
            var goal = new GridPosition(8, 4);

            var result = pathfinder.FindPath(start, goal);

            Assert.That(result.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(result.Steps.Count, Is.GreaterThan(0));

            // Verify no step touches the wall
            foreach (var step in result.Steps)
            {
                Assert.That(grid.IsWalkable(step), Is.True, $"Step {step} must be walkable");
                Assert.That(step.X == 5 && step.Z >= 2 && step.Z <= 7, Is.False, $"Step {step} cannot intersect barrier");
            }
        }

        [Test]
        public void A03_NoCornerCut_StrictCornerCuttingGuardRejectsDiagonalMovesTouchingObstacles()
        {
            var grid = new SimGridMap(10, 10, 10.0f);
            var pathfinder = new AStarPathfinder(grid);

            // Touching diagonal obstacles: (1, 0) and (0, 1) are blocked
            grid.SetCell(1, 0, SimCellType.Obstacle);
            grid.SetCell(0, 1, SimCellType.Obstacle);

            // Attempt to move from (0, 0) to (1, 1). With corner cutting forbidden, (0, 0) is boxed in!
            var start = new GridPosition(0, 0);
            var goal = new GridPosition(1, 1);

            var result = pathfinder.FindPath(start, goal);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Unreachable), "Diagonal between two orthogonal obstacles must not be cut");

            // One cardinal blocked: (1, 0) blocked, but (0, 1) open
            var grid2 = new SimGridMap(10, 10, 10.0f);
            grid2.SetCell(1, 0, SimCellType.Obstacle);
            var pathfinder2 = new AStarPathfinder(grid2);

            var result2 = pathfinder2.FindPath(start, goal);
            Assert.That(result2.Status, Is.EqualTo(PathStatus.Success));
            // First step from (0, 0) cannot be diagonal (1, 1); must move cardinal to (0, 1) first
            Assert.That(result2.Steps[1], Is.EqualTo(new GridPosition(0, 1)), "Must not cut corner through (1, 0) even if (0, 1) is clear");
        }

        [Test]
        public void A04_TerrainCost_RoughTerrainVsWalkableDetoursThroughCheaperWalkablePath()
        {
            var grid = new SimGridMap(10, 10, 10.0f);
            var pathfinder = new AStarPathfinder(grid);

            // Direct line from (1, 2) to (5, 2) has DangerZone (cost 5.0f) at (2, 2), (3, 2), (4, 2)
            grid.SetCell(2, 2, SimCellType.DangerZone); // cost 5.0
            grid.SetCell(3, 2, SimCellType.DangerZone); // cost 5.0
            grid.SetCell(4, 2, SimCellType.DangerZone); // cost 5.0

            var start = new GridPosition(1, 2);
            var goal = new GridPosition(5, 2);

            var result = pathfinder.FindPath(start, goal);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Success));

            // Verify the path detoured around the danger zone to minimize cost
            foreach (var step in result.Steps)
            {
                Assert.That(grid.GetCell(step), Is.Not.EqualTo(SimCellType.DangerZone),
                    $"Step {step} should avoid DangerZone when cheaper detour exists");
            }
        }

        [Test]
        public void A05_BudgetBoundary_LargeMazeWithBudget50ReturnsSearchBudgetExceededWithExactNodesExpanded()
        {
            var grid = new SimGridMap(50, 50, 10.0f);
            // Create a long serpentine maze
            for (int x = 0; x < 48; x++)
            {
                grid.SetCell(x, 10, SimCellType.Obstacle);
            }

            var pathfinder = new AStarPathfinder(grid);
            var start = new GridPosition(0, 0);
            var goal = new GridPosition(0, 20);

            var result = pathfinder.FindPath(start, goal, maxExpansions: 50);

            Assert.That(result.Status, Is.EqualTo(PathStatus.SearchBudgetExceeded));
            Assert.That(result.NodesExpanded, Is.EqualTo(50));
            Assert.That(result.GridCost, Is.EqualTo(float.PositiveInfinity));
            Assert.That(result.Steps.Count, Is.EqualTo(0));
            Assert.That(result.Waypoints.Count, Is.EqualTo(0));
        }

        [Test]
        public void A06_BudgetBoundaryExact_Amendment2BudgetOneAdjacentSucceedsTwoStepsFails()
        {
            var grid = new SimGridMap(10, 10, 10.0f);
            var pathfinder = new AStarPathfinder(grid);

            var start = new GridPosition(0, 0);
            var adjacentGoal = new GridPosition(1, 0);
            var twoStepGoal = new GridPosition(2, 0);

            // 1. Adjacent goal with maxExpansions = 1 -> Success with NodesExpanded == 1
            var adjResult = pathfinder.FindPath(start, adjacentGoal, maxExpansions: 1);
            Assert.That(adjResult.Status, Is.EqualTo(PathStatus.Success), "Adjacent goal popped before expansion check must succeed");
            Assert.That(adjResult.NodesExpanded, Is.EqualTo(1));
            Assert.That(adjResult.Steps.Count, Is.EqualTo(2));
            Assert.That(adjResult.GridCost, Is.EqualTo(10.0f));

            // 2. Two-step goal with maxExpansions = 1 -> SearchBudgetExceeded with NodesExpanded == 1
            var twoStepResult = pathfinder.FindPath(start, twoStepGoal, maxExpansions: 1);
            Assert.That(twoStepResult.Status, Is.EqualTo(PathStatus.SearchBudgetExceeded), "Two-step goal must hit budget limit before expanding 2nd node");
            Assert.That(twoStepResult.NodesExpanded, Is.EqualTo(1), "Strict invariant: NodesExpanded <= maxExpansions");
        }

        [Test]
        public void A07_EndpointValidation_InvalidStartAndGoalAndZeroCostSelfMatch()
        {
            var grid = new SimGridMap(10, 10, 10.0f);
            grid.SetCell(5, 5, SimCellType.Obstacle);
            var pathfinder = new AStarPathfinder(grid);

            // Out-of-bounds start
            var oobStart = pathfinder.FindPath(new GridPosition(-1, 0), new GridPosition(2, 2));
            Assert.That(oobStart.Status, Is.EqualTo(PathStatus.InvalidStart));
            Assert.That(oobStart.NodesExpanded, Is.EqualTo(0));

            // Obstacle start
            var obstStart = pathfinder.FindPath(new GridPosition(5, 5), new GridPosition(2, 2));
            Assert.That(obstStart.Status, Is.EqualTo(PathStatus.InvalidStart));
            Assert.That(obstStart.NodesExpanded, Is.EqualTo(0));

            // Out-of-bounds goal
            var oobGoal = pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(15, 0));
            Assert.That(oobGoal.Status, Is.EqualTo(PathStatus.InvalidGoal));
            Assert.That(oobGoal.NodesExpanded, Is.EqualTo(0));

            // Obstacle goal
            var obstGoal = pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(5, 5));
            Assert.That(obstGoal.Status, Is.EqualTo(PathStatus.InvalidGoal));
            Assert.That(obstGoal.NodesExpanded, Is.EqualTo(0));

            // Identity check: start == goal
            var same = pathfinder.FindPath(new GridPosition(3, 3), new GridPosition(3, 3));
            Assert.That(same.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(same.GridCost, Is.EqualTo(0f));
            Assert.That(same.NodesExpanded, Is.EqualTo(0));
            Assert.That(same.Steps.Count, Is.EqualTo(1));
            Assert.That(same.Waypoints.Count, Is.EqualTo(1));
            Assert.That(same.Steps[0], Is.EqualTo(new GridPosition(3, 3)));
            Assert.That(same.Waypoints[0], Is.EqualTo(grid.GridToWorld(new GridPosition(3, 3))));

            // Invalid budget
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(1, 1), maxExpansions: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(1, 1), maxExpansions: -5));
        }

        [Test]
        public void A08_InvariantProperty_AStarGridCostEqualsReferenceDijkstraGridCostAcross100RandomPairs()
        {
            var rng = new Random(1337);
            var grid = new SimGridMap(30, 30, 10.0f);
            grid.GenerateProceduralFeatures(seed: 999);
            var pathfinder = new AStarPathfinder(grid);

            int testedPairs = 0;
            while (testedPairs < 100)
            {
                var start = new GridPosition(rng.Next(0, 30), rng.Next(0, 30));
                var goal = new GridPosition(rng.Next(0, 30), rng.Next(0, 30));

                if (!grid.IsWalkable(start) || !grid.IsWalkable(goal))
                    continue;

                var astarResult = pathfinder.FindPath(start, goal, maxExpansions: 10000);
                var (dijkstraStatus, dijkstraCost) = ReferenceDijkstra.FindShortestPath(grid, start, goal);

                Assert.That(astarResult.Status, Is.EqualTo(dijkstraStatus),
                    $"Status mismatch for path from {start} to {goal}");

                if (astarResult.Status == PathStatus.Success)
                {
                    Assert.That(astarResult.GridCost, Is.EqualTo(dijkstraCost).Within(1e-3f),
                        $"A* cost {astarResult.GridCost} must match Dijkstra optimal cost {dijkstraCost} for {start} to {goal}");
                }

                testedPairs++;
            }
        }

        [Test]
        public void A09_Admissibility_OctileHeuristicIsMonotonicConsistentAndAdmissibleAcross500Pairs()
        {
            var rng = new Random(2026);
            var grid = new SimGridMap(40, 40, 10.0f);
            grid.GenerateProceduralFeatures(seed: 42);

            var directions = new[]
            {
                (0, 1, 1.0f, false),
                (1, 1, (float)Math.Sqrt(2), true),
                (1, 0, 1.0f, false),
                (1, -1, (float)Math.Sqrt(2), true),
                (0, -1, 1.0f, false),
                (-1, -1, (float)Math.Sqrt(2), true),
                (-1, 0, 1.0f, false),
                (-1, 1, (float)Math.Sqrt(2), true)
            };

            float ComputeH(GridPosition u, GridPosition g)
            {
                int dx = Math.Abs(u.X - g.X);
                int dz = Math.Abs(u.Z - g.Z);
                int minD = Math.Min(dx, dz);
                int maxD = Math.Max(dx, dz);
                float octile = ((float)Math.Sqrt(2) - 1.0f) * minD + maxD;
                return grid.CellSize * grid.MinTraversalCost * octile;
            }

            for (int i = 0; i < 500; i++)
            {
                var u = new GridPosition(rng.Next(0, 40), rng.Next(0, 40));
                var goal = new GridPosition(rng.Next(0, 40), rng.Next(0, 40));

                float hU = ComputeH(u, goal);

                // Triangle inequality consistency check: h(u) <= c(u, v) + h(v) for all valid transitions v
                foreach (var (dx, dz, factor, isDiag) in directions)
                {
                    var v = new GridPosition(u.X + dx, u.Z + dz);
                    if (!grid.IsInBounds(v) || !grid.IsWalkable(v))
                        continue;

                    if (isDiag)
                    {
                        var c1 = new GridPosition(u.X + dx, u.Z);
                        var c2 = new GridPosition(u.X, u.Z + dz);
                        if (!grid.IsWalkable(c1) || !grid.IsWalkable(c2))
                            continue;
                    }

                    float stepCost = grid.CellSize * factor * grid.GetTraversalCost(v);
                    float hV = ComputeH(v, goal);

                    Assert.That(hU, Is.LessThanOrEqualTo(stepCost + hV + 1e-4f),
                        $"Consistency violated: h({u})={hU} must be <= stepCost({stepCost}) + h({v})={hV}");
                }
            }
        }

        [Test]
        public void A10_DeterminismAndReuse_SameInstance100ConsecutiveRunsProducesBitIdenticalOutputs()
        {
            var grid = new SimGridMap(30, 30, 10.0f);
            grid.GenerateProceduralFeatures(seed: 123);
            var pathfinder = new AStarPathfinder(grid);

            var start = new GridPosition(2, 2);
            var goal = new GridPosition(25, 25);

            var baseline = pathfinder.FindPath(start, goal);
            Assert.That(baseline.Status, Is.EqualTo(PathStatus.Success));

            for (int run = 0; run < 100; run++)
            {
                var runResult = pathfinder.FindPath(start, goal);
                Assert.That(runResult.Status, Is.EqualTo(baseline.Status));
                Assert.That(runResult.GridCost, Is.EqualTo(baseline.GridCost));
                Assert.That(runResult.NodesExpanded, Is.EqualTo(baseline.NodesExpanded));
                Assert.That(runResult.Steps.Count, Is.EqualTo(baseline.Steps.Count));

                for (int i = 0; i < baseline.Steps.Count; i++)
                {
                    Assert.That(runResult.Steps[i], Is.EqualTo(baseline.Steps[i]));
                    Assert.That(runResult.Waypoints[i], Is.EqualTo(baseline.Waypoints[i]));
                }
            }
        }

        [Test]
        public void A11_Immutability_DefensiveCopiesPreventCallerMutation()
        {
            var originalSteps = new[]
            {
                new GridPosition(0, 0),
                new GridPosition(1, 0)
            };

            var originalWaypoints = new[]
            {
                new Vec3(5f, 0f, 5f),
                new Vec3(15f, 0f, 5f)
            };

            var result = PathResult.Successful(originalSteps, originalWaypoints, 10.0f, 1);

            // Mutate the original arrays
            originalSteps[0] = new GridPosition(999, 999);
            originalWaypoints[0] = new Vec3(999f, 999f, 999f);

            // Verify PathResult remains completely unaffected
            Assert.That(result.Steps[0], Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(result.Waypoints[0], Is.EqualTo(new Vec3(5f, 0f, 5f)));

            // Verify IReadOnlyList cannot be cast to mutable array to modify internal state
            Assert.That(result.Steps, Is.Not.SameAs(originalSteps));
            Assert.That(result.Waypoints, Is.Not.SameAs(originalWaypoints));

            // Failure factory validation
            Assert.Throws<ArgumentException>(() => PathResult.Failure(PathStatus.Success, 0));
            var fail = PathResult.Failure(PathStatus.Unreachable, 42);
            Assert.That(fail.Status, Is.EqualTo(PathStatus.Unreachable));
            Assert.That(fail.NodesExpanded, Is.EqualTo(42));
            Assert.That(fail.GridCost, Is.EqualTo(float.PositiveInfinity));
            Assert.That(fail.Steps.Count, Is.EqualTo(0));
            Assert.That(fail.Waypoints.Count, Is.EqualTo(0));
        }

        [Test]
        public void AStar_PathValidityProperty_EveryPathReturnedSatisfiesStructuralAndCostInvariants()
        {
            var cellSizes = new[] { 1.0f, 2.5f, 10.0f };
            var rng = new Random(4242);

            foreach (var cellSize in cellSizes)
            {
                var grid = new SimGridMap(25, 25, cellSize);
                grid.GenerateProceduralFeatures(seed: (int)(cellSize * 100));
                var pathfinder = new AStarPathfinder(grid);

                for (int pair = 0; pair < 30; pair++)
                {
                    var start = new GridPosition(rng.Next(0, 25), rng.Next(0, 25));
                    var goal = new GridPosition(rng.Next(0, 25), rng.Next(0, 25));

                    if (!grid.IsWalkable(start) || !grid.IsWalkable(goal))
                        continue;

                    var result = pathfinder.FindPath(start, goal);
                    if (result.Status != PathStatus.Success)
                        continue;

                    // 1. First step == start cell, last == goal cell
                    Assert.That(result.Steps[0], Is.EqualTo(start), "First step must be start cell");
                    Assert.That(result.Steps[result.Steps.Count - 1], Is.EqualTo(goal), "Last step must be goal cell");

                    float recomputedCost = 0f;

                    // 2. Step-by-step invariants
                    for (int i = 0; i < result.Steps.Count; i++)
                    {
                        var current = result.Steps[i];
                        Assert.That(grid.IsWalkable(current), Is.True, $"Cell {current} at index {i} must be walkable");

                        if (i > 0)
                        {
                            var prev = result.Steps[i - 1];
                            int dx = Math.Abs(current.X - prev.X);
                            int dz = Math.Abs(current.Z - prev.Z);

                            // 8-way adjacency: dx <= 1, dz <= 1, and (dx + dz > 0)
                            Assert.That(dx <= 1 && dz <= 1 && (dx + dz > 0), Is.True,
                                $"Step {prev} -> {current} must be adjacent 8-way neighbor");

                            bool isDiagonal = (dx == 1 && dz == 1);
                            if (isDiagonal)
                            {
                                // Strict no-corner-cutting
                                var c1 = new GridPosition(prev.X + (current.X - prev.X), prev.Z);
                                var c2 = new GridPosition(prev.X, prev.Z + (current.Z - prev.Z));
                                Assert.That(grid.IsWalkable(c1) && grid.IsWalkable(c2), Is.True,
                                    $"Diagonal move {prev} -> {current} must not cut corners");
                            }

                            float stepFactor = isDiagonal ? (float)Math.Sqrt(2) : 1.0f;
                            recomputedCost += grid.CellSize * stepFactor * grid.GetTraversalCost(current);
                        }
                    }

                    // 3. Recomputed cost matches GridCost within relative 1e-4
                    if (result.GridCost > 0)
                    {
                        float relDiff = Math.Abs(recomputedCost - result.GridCost) / result.GridCost;
                        Assert.That(relDiff, Is.LessThan(1e-4f),
                            $"Recomputed cost {recomputedCost} must match GridCost {result.GridCost} within relative 1e-4");
                    }
                    else
                    {
                        Assert.That(recomputedCost, Is.EqualTo(0f));
                    }
                }
            }
        }

        [Test]
        public void A08_InvariantProperty_RandomMaps_AStarGridCostEqualsReferenceDijkstraAcrossCellSizes()
        {
            float[] cellSizes = new[] { 1.0f, 2.5f, 10.0f };
            int[] seeds = new[] { 111, 222, 333 };
            var rng = new Random(5555);

            for (int m = 0; m < cellSizes.Length; m++)
            {
                float cellSize = cellSizes[m];
                int seed = seeds[m];

                var grid = new SimGridMap(20, 20, cellSize);
                grid.GenerateProceduralFeatures(seed);
                var pathfinder = new AStarPathfinder(grid);

                int tested = 0;
                while (tested < 35)
                {
                    var start = new GridPosition(rng.Next(0, 20), rng.Next(0, 20));
                    var goal = new GridPosition(rng.Next(0, 20), rng.Next(0, 20));

                    if (!grid.IsWalkable(start) || !grid.IsWalkable(goal))
                        continue;

                    var astarResult = pathfinder.FindPath(start, goal, maxExpansions: 5000);
                    var (dijkstraStatus, dijkstraCost) = ReferenceDijkstra.FindShortestPath(grid, start, goal);

                    Assert.That(astarResult.Status, Is.EqualTo(dijkstraStatus),
                        $"Status mismatch on seed {seed}, cellSize {cellSize} for {start} -> {goal}");

                    if (astarResult.Status == PathStatus.Success)
                    {
                        if (dijkstraCost > 0)
                        {
                            float relDiff = Math.Abs(astarResult.GridCost - dijkstraCost) / dijkstraCost;
                            Assert.That(relDiff, Is.LessThan(1e-4f),
                                $"A* cost {astarResult.GridCost} vs Dijkstra {dijkstraCost} exceeds 1e-4 relative tolerance on seed {seed}, cellSize {cellSize}");
                        }
                        else
                        {
                            Assert.That(astarResult.GridCost, Is.EqualTo(0f));
                        }
                    }

                    tested++;
                }
            }
        }

        [Test]
        public void AStar_GenerationCounterWraparound_ClearsStampsAndProducesCorrectPath()
        {
            var grid = new SimGridMap(10, 10, 10.0f);
            var pathfinder = new AStarPathfinder(grid);

            // Execute an initial path to populate stamps
            var r1 = pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(2, 2));
            Assert.That(r1.Status, Is.EqualTo(PathStatus.Success));

            // Force generation counter to int.MaxValue
            pathfinder.SetGenerationForTesting(int.MaxValue);
            Assert.That(pathfinder.CurrentGenerationForTesting, Is.EqualTo(int.MaxValue));

            // Find another path, which triggers AdvanceGeneration with wraparound
            var r2 = pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(3, 3));
            Assert.That(r2.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(pathfinder.CurrentGenerationForTesting, Is.EqualTo(1), "Generation should wrap to 1");

            // Verify stamps array was cleared before writing new generation stamps
            int matchCount = 0;
            foreach (var stamp in pathfinder.SeenGenerationForTesting)
            {
                if (stamp == 1) matchCount++;
                else Assert.That(stamp, Is.EqualTo(0), "Non-visited cells must be 0 after wraparound clear");
            }
            Assert.That(matchCount, Is.GreaterThan(0));
        }

        private sealed class ResizableNavGrid : INavGrid
        {
            public int GridWidth { get; set; }
            public int GridHeight { get; set; }
            public float CellSize => 10.0f;
            public float MinTraversalCost => 1.0f;

            public ResizableNavGrid(int w, int h)
            {
                GridWidth = w;
                GridHeight = h;
            }

            public bool IsInBounds(GridPosition p) => p.X >= 0 && p.X < GridWidth && p.Z >= 0 && p.Z < GridHeight;
            public bool IsWalkable(GridPosition p) => IsInBounds(p);
            public float GetTraversalCost(GridPosition p) => IsInBounds(p) ? 1.0f : float.PositiveInfinity;
            public GridPosition WorldToGrid(Vec3 worldPos) => new GridPosition((int)Math.Floor(worldPos.X / CellSize), (int)Math.Floor(worldPos.Z / CellSize));
            public Vec3 GridToWorld(GridPosition gridPos) => new Vec3((gridPos.X + 0.5f) * CellSize, 0f, (gridPos.Z + 0.5f) * CellSize);
        }

        [Test]
        public void AStar_BufferResize_ReallocatesWhenGridDimensionsChange()
        {
            var resizable = new ResizableNavGrid(10, 10);
            var pathfinder = new AStarPathfinder(resizable);

            var r1 = pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(2, 2));
            Assert.That(r1.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(pathfinder.BufferCapacityForTesting, Is.EqualTo(100));

            // Change grid dimensions to larger
            resizable.GridWidth = 20;
            resizable.GridHeight = 15;
            var r2 = pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(18, 14));
            Assert.That(r2.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(pathfinder.BufferCapacityForTesting, Is.EqualTo(300), "Buffers must expand to 20x15 = 300");

            // Change grid dimensions to smaller
            resizable.GridWidth = 8;
            resizable.GridHeight = 8;
            var r3 = pathfinder.FindPath(new GridPosition(0, 0), new GridPosition(5, 5));
            Assert.That(r3.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(pathfinder.BufferCapacityForTesting, Is.EqualTo(64), "Buffers must resize to 8x8 = 64");
        }
    }
}
