using System;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;
using Assistant.Sim;
using NUnit.Framework;

namespace Assistant.Tests.Pathfinding
{
    [TestFixture]
    public class NavGridContractTests
    {
        [Test]
        public void A12_NavGridContract_SimGridMapSatisfiesAllDocumentedContractInvariants()
        {
            // 1. Default Map Dimensions & Defaults
            var map = new SimGridMap();
            Assert.That(map.GridWidth, Is.EqualTo(100));
            Assert.That(map.GridHeight, Is.EqualTo(100));
            Assert.That(map.CellSize, Is.EqualTo(10.0f));
            Assert.That(map.MinTraversalCost, Is.EqualTo(1.0f));

            // 2. Invariant: Dimensions and CellSize bounds validation
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimGridMap(width: 0, height: 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimGridMap(width: -10, height: 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimGridMap(width: 100, height: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimGridMap(width: 100, height: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimGridMap(cellSize: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimGridMap(cellSize: -5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimGridMap(cellSize: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimGridMap(cellSize: float.PositiveInfinity));

            // 3. Traversal Cost and Walkability Invariants
            // Walkable cell
            var walkablePos = new GridPosition(5, 5);
            map.SetCell(walkablePos, SimCellType.Walkable);
            Assert.That(map.IsInBounds(walkablePos), Is.True);
            Assert.That(map.IsWalkable(walkablePos), Is.True);
            Assert.That(map.GetTraversalCost(walkablePos), Is.EqualTo(1.0f));
            Assert.That(map.GetTraversalCost(walkablePos), Is.GreaterThanOrEqualTo(map.MinTraversalCost));

            // Rough terrain
            map.SetCell(walkablePos, SimCellType.RoughTerrain);
            Assert.That(map.IsWalkable(walkablePos), Is.True);
            Assert.That(map.GetTraversalCost(walkablePos), Is.EqualTo(2.5f));

            // Danger zone
            map.SetCell(walkablePos, SimCellType.DangerZone);
            Assert.That(map.IsWalkable(walkablePos), Is.True);
            Assert.That(map.GetTraversalCost(walkablePos), Is.EqualTo(5.0f));

            // Obstacle cell
            var obstaclePos = new GridPosition(10, 10);
            map.SetCell(obstaclePos, SimCellType.Obstacle);
            Assert.That(map.IsInBounds(obstaclePos), Is.True);
            Assert.That(map.IsWalkable(obstaclePos), Is.False);
            Assert.That(map.GetTraversalCost(obstaclePos), Is.EqualTo(float.PositiveInfinity));

            // Out-of-bounds cells
            var outOfBounds = new[]
            {
                new GridPosition(-1, 0),
                new GridPosition(0, -1),
                new GridPosition(100, 50),
                new GridPosition(50, 100),
                new GridPosition(-999, -999),
                new GridPosition(1000, 1000)
            };

            foreach (var oob in outOfBounds)
            {
                Assert.That(map.IsInBounds(oob), Is.False, $"Cell {oob} must be out of bounds");
                Assert.That(map.IsWalkable(oob), Is.False, $"Out-of-bounds cell {oob} must not be walkable");
                Assert.That(map.GetTraversalCost(oob), Is.EqualTo(float.PositiveInfinity),
                    $"Out-of-bounds cell {oob} must return PositiveInfinity traversal cost");
            }

            // 4. Coordinate Conversion: GridToWorld returns horizontal center with Y = 0
            var originCenter = map.GridToWorld(new GridPosition(0, 0));
            Assert.That(originCenter.X, Is.EqualTo(5.0f)); // (0 + 0.5) * 10
            Assert.That(originCenter.Y, Is.EqualTo(0.0f));
            Assert.That(originCenter.Z, Is.EqualTo(5.0f)); // (0 + 0.5) * 10

            // 5. Bidirectional Coordinate Consistency
            for (int x = 0; x < map.GridWidth; x += 15)
            {
                for (int z = 0; z < map.GridHeight; z += 15)
                {
                    var gridPos = new GridPosition(x, z);
                    var worldPos = map.GridToWorld(gridPos);
                    var roundTrip = map.WorldToGrid(worldPos);
                    Assert.That(roundTrip, Is.EqualTo(gridPos), $"Round trip failed for {gridPos}");
                }
            }

            // 6. WorldToGrid Floor & Boundary Handling
            Assert.That(map.WorldToGrid(new Vec3(0.0f, 0f, 0.0f)), Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(map.WorldToGrid(new Vec3(9.999f, 0f, 9.999f)), Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(map.WorldToGrid(new Vec3(10.0f, 0f, 10.0f)), Is.EqualTo(new GridPosition(1, 1)));
            Assert.That(map.WorldToGrid(new Vec3(-0.001f, 0f, 0f)), Is.EqualTo(new GridPosition(-1, 0)));

            // NaN / Infinity handling in WorldToGrid
            var nanPos = map.WorldToGrid(new Vec3(float.NaN, 0f, 10f));
            Assert.That(map.IsInBounds(nanPos), Is.False);

            var infPos = map.WorldToGrid(new Vec3(float.PositiveInfinity, 0f, 0f));
            Assert.That(map.IsInBounds(infPos), Is.False);

            var negInfPos = map.WorldToGrid(new Vec3(0f, 0f, float.NegativeInfinity));
            Assert.That(map.IsInBounds(negInfPos), Is.False);
        }
    }
}
