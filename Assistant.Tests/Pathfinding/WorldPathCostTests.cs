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
    public class WorldPathCostTests
    {
        private SimGridMap _map = null!;
        private AStarPathfinder _pathfinder = null!;

        [SetUp]
        public void SetUp()
        {
            // Default 100x100 map, CellSize = 10.0m, MinTraversalCost = 1.0f
            _map = new SimGridMap(100, 100, cellSize: 10.0f);
            _pathfinder = new AStarPathfinder(_map);
        }

        [Test]
        public void WorldPathCost_SameCellEndpoints_At9Point8Meters_GridCostZero_ResultExceedsMinTraversalCostDistance()
        {
            // Both player and token are inside cell (5, 5) whose center is (55.0, 0, 55.0)
            // Cell bounds: [50.0, 60.0] in X and Z
            // Player at corner (50.1, 0, 50.1), Token at opposite corner (59.9, 0, 50.1) => dx = 9.8m, dz = 0m => dist = 9.8m
            var playerPos = new Vec3(50.1f, 0f, 50.1f);
            var tokenPos = new Vec3(59.9f, 0f, 50.1f);
            float dist2D = Vec3.Distance2D(playerPos, tokenPos);
            Assert.That(dist2D, Is.EqualTo(9.8f).Within(1e-4f));

            var startCell = _map.WorldToGrid(playerPos);
            var goalCell = _map.WorldToGrid(tokenPos);
            Assert.That(startCell, Is.EqualTo(goalCell), "Endpoints must be in the same cell");

            var path = _pathfinder.FindPath(startCell, goalCell);
            Assert.That(path.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(path.GridCost, Is.EqualTo(0f), "Discrete GridCost is 0 when start == goal");

            float worldCost = WorldPathCost.Compute(_map, path, playerPos, startCell, tokenPos);

            // Invariant: worldCost >= MinTraversalCost * Distance2D(p, t)
            float minBound = _map.MinTraversalCost * dist2D;
            Assert.That(worldCost, Is.GreaterThanOrEqualTo(minBound - 1e-6f),
                $"Same-cell composite cost ({worldCost}) must be >= minBound ({minBound})");
        }

        [Test]
        public void WorldPathCost_SnappedStart_PreservesPlayerOffsetAndTriangleInequality()
        {
            // Suppose player is at continuous position (5.0, 0, 5.0).
            // Even if search starts from a snapped cell (e.g. (1, 0)),
            // WorldPathCost uses player's actual continuous position to measure start connector.
            var playerPos = new Vec3(5.0f, 0f, 5.0f);
            var snappedCell = new GridPosition(1, 0); // Center at (15.0, 0, 5.0)
            var tokenPos = new Vec3(25.0f, 0f, 5.0f); // In cell (2, 0), center at (25.0, 0, 5.0)

            var path = _pathfinder.FindPath(snappedCell, _map.WorldToGrid(tokenPos));
            Assert.That(path.Status, Is.EqualTo(PathStatus.Success));

            float worldCost = WorldPathCost.Compute(_map, path, playerPos, snappedCell, tokenPos);
            float dist2D = Vec3.Distance2D(playerPos, tokenPos);

            Assert.That(worldCost, Is.GreaterThanOrEqualTo(_map.MinTraversalCost * dist2D - 1e-6f),
                "Triangle inequality must hold strictly for snapped start");
        }

        [Test]
        public void WorldPathCost_AdjacentCells_IncludesGridTransitionAndBothConnectors()
        {
            // Player in cell (5, 5) at (52.0, 0, 55.0); Token in cell (6, 5) at (68.0, 0, 55.0)
            var playerPos = new Vec3(52.0f, 0f, 55.0f);
            var tokenPos = new Vec3(68.0f, 0f, 55.0f);
            var startCell = _map.WorldToGrid(playerPos);
            var goalCell = _map.WorldToGrid(tokenPos);

            var path = _pathfinder.FindPath(startCell, goalCell);
            Assert.That(path.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(path.GridCost, Is.EqualTo(10.0f), "Grid transition cost for 1 cardinal step of cell size 10");

            float worldCost = WorldPathCost.Compute(_map, path, playerPos, startCell, tokenPos);
            float dist2D = Vec3.Distance2D(playerPos, tokenPos); // 16.0m

            Assert.That(worldCost, Is.GreaterThanOrEqualTo(_map.MinTraversalCost * dist2D - 1e-6f));
        }

        [Test]
        public void WorldPathCost_TokenNearCellBorder_ConsistentAndAdmissible()
        {
            // Token is 0.01m from cell border
            var playerPos = new Vec3(55.0f, 0f, 55.0f);
            var tokenPos = new Vec3(79.99f, 0f, 55.0f); // Very close to boundary between cell 7 and 8 (80.0)
            var startCell = _map.WorldToGrid(playerPos);
            var goalCell = _map.WorldToGrid(tokenPos);

            var path = _pathfinder.FindPath(startCell, goalCell);
            Assert.That(path.Status, Is.EqualTo(PathStatus.Success));

            float worldCost = WorldPathCost.Compute(_map, path, playerPos, startCell, tokenPos);
            float dist2D = Vec3.Distance2D(playerPos, tokenPos);

            Assert.That(worldCost, Is.GreaterThanOrEqualTo(_map.MinTraversalCost * dist2D - 1e-6f));
        }

        [Test]
        public void WorldPathCost_UnreachablePath_ReturnsPositiveInfinity()
        {
            var unreachablePath = PathResult.Failure(PathStatus.Unreachable, 50);
            float cost = WorldPathCost.Compute(_map, unreachablePath, Vec3.Zero, new GridPosition(0, 0), new Vec3(10, 0, 10));

            Assert.That(float.IsPositiveInfinity(cost), Is.True, "Unreachable path must yield +Infinity world cost");
        }

        [Test]
        public void PropertyTest_RealAStar_SeededRandomMaps_WorldCostAlwaysExceedsStraightLineLowerBound()
        {
            var rng = new Random(12345);

            for (int trial = 0; trial < 50; trial++)
            {
                // Create random walkable positions
                float px = (float)(rng.NextDouble() * 800.0 + 50.0);
                float pz = (float)(rng.NextDouble() * 800.0 + 50.0);
                float tx = (float)(rng.NextDouble() * 800.0 + 50.0);
                float tz = (float)(rng.NextDouble() * 800.0 + 50.0);

                var player = new Vec3(px, 0f, pz);
                var token = new Vec3(tx, 0f, tz);

                var startCell = _map.WorldToGrid(player);
                var goalCell = _map.WorldToGrid(token);

                var path = _pathfinder.FindPath(startCell, goalCell);
                if (path.Status != PathStatus.Success)
                    continue;

                float worldCost = WorldPathCost.Compute(_map, path, player, startCell, token);
                float dist2D = Vec3.Distance2D(player, token);
                float minBound = _map.MinTraversalCost * dist2D;

                Assert.That(worldCost, Is.GreaterThanOrEqualTo(minBound - 1e-5f),
                    $"Trial {trial}: Invariant violated! WorldCost {worldCost} < minBound {minBound} (diff: {minBound - worldCost})");
            }
        }

        [Test]
        public void WorldPathCost_LongStraightPath_85Cells_DoesNotTripRelativeToleranceCheck()
        {
            // Open 100x100 grid, CellSize = 10.0f.
            // Start at cell (5, 50), Goal at cell (90, 50) -> 85 cells along X-axis
            var startCell = new GridPosition(5, 50);
            var goalCell = new GridPosition(90, 50);
            var playerPos = _map.GridToWorld(startCell); // (55.0, 0, 505.0)
            var tokenPos = _map.GridToWorld(goalCell);   // (905.0, 0, 505.0)

            var path = _pathfinder.FindPath(startCell, goalCell);
            Assert.That(path.Status, Is.EqualTo(PathStatus.Success));
            Assert.That(path.Steps.Count, Is.EqualTo(86));

            float worldCost = 0f;
            Assert.DoesNotThrow(() =>
            {
                worldCost = WorldPathCost.Compute(_map, path, playerPos, startCell, tokenPos);
            });

            float expectedCost = 85.0f * _map.CellSize * _map.MinTraversalCost;
            Assert.That(worldCost, Is.EqualTo(expectedCost).Within(1e-4f));
        }

        [Test]
        public void WorldPathCost_DeliberateGrossViolation_ThrowsInvalidOperationException()
        {
            var startCell = new GridPosition(10, 10);
            var goalCell = new GridPosition(50, 10);
            var playerPos = _map.GridToWorld(startCell);
            var tokenPos = _map.GridToWorld(goalCell);
            float dist2D = Vec3.Distance2D(playerPos, tokenPos);
            float minLowerBound = _map.MinTraversalCost * dist2D;

            // Deliberate gross violation: path cost is 0.5 * lower bound
            var corruptedPath = PathResult.Successful(
                new[] { startCell, goalCell },
                new[] { playerPos, tokenPos },
                gridCost: 0.5f * minLowerBound,
                nodesExpanded: 2);

            Assert.Throws<InvalidOperationException>(() =>
                WorldPathCost.Compute(_map, corruptedPath, playerPos, startCell, tokenPos));
        }
    }
}
