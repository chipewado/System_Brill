using System.Collections.Generic;
using NUnit.Framework;
using Assistant.Core.Data;
using Assistant.Sim;

namespace Assistant.Tests
{
    [TestFixture]
    public class GridPositionTests
    {
        [Test]
        public void Equality_SameCoordinates_AreEqual()
        {
            var p1 = new GridPosition(5, 10);
            var p2 = new GridPosition(5, 10);

            Assert.That(p1 == p2, Is.True);
            Assert.That(p1 != p2, Is.False);
            Assert.That(p1.Equals(p2), Is.True);
            Assert.That(p1.Equals((object)p2), Is.True);
        }

        [Test]
        public void Inequality_DifferentCoordinates_AreNotEqual()
        {
            var p1 = new GridPosition(5, 10);
            var p2 = new GridPosition(6, 10);
            var p3 = new GridPosition(5, 11);

            Assert.That(p1 == p2, Is.False);
            Assert.That(p1 != p2, Is.True);
            Assert.That(p1.Equals(p2), Is.False);

            Assert.That(p1 == p3, Is.False);
            Assert.That(p1 != p3, Is.True);
            Assert.That(p1.Equals(p3), Is.False);
        }

        [Test]
        public void GetHashCode_SameCoordinates_ProduceSameHashCode()
        {
            var p1 = new GridPosition(42, 99);
            var p2 = new GridPosition(42, 99);

            Assert.That(p1.GetHashCode(), Is.EqualTo(p2.GetHashCode()));
        }

        [Test]
        public void UsableInHashSetAndDictionary()
        {
            var set = new HashSet<GridPosition>();
            var p1 = new GridPosition(3, 7);
            var p2 = new GridPosition(3, 7);
            var p3 = new GridPosition(4, 7);

            Assert.That(set.Add(p1), Is.True);
            Assert.That(set.Add(p2), Is.False);
            Assert.That(set.Add(p3), Is.True);
            Assert.That(set.Count, Is.EqualTo(2));

            var dict = new Dictionary<GridPosition, string>();
            dict[p1] = "cell_3_7";
            dict[p3] = "cell_4_7";

            Assert.That(dict[p2], Is.EqualTo("cell_3_7"));
            Assert.That(dict.ContainsKey(new GridPosition(3, 7)), Is.True);
            Assert.That(dict.ContainsKey(new GridPosition(0, 0)), Is.False);
        }

        [Test]
        public void ToString_FormatsCoordinatesCorrectly()
        {
            var p = new GridPosition(12, 34);
            Assert.That(p.ToString(), Is.EqualTo("(12, 34)"));
        }

        [Test]
        public void Deconstruction_UnpacksCoordinates()
        {
            var p = new GridPosition(7, 14);
            var (x, z) = p;

            Assert.That(x, Is.EqualTo(7));
            Assert.That(z, Is.EqualTo(14));
        }

        [Test]
        public void SimGridMap_WorldToGrid_And_GridToWorld_RoundTrip()
        {
            var map = new SimGridMap(width: 50, height: 50, cellSize: 10f);

            var originWorld = new Vec3(25.0f, 0, 75.0f);
            var gridPos = map.WorldToGrid(originWorld);

            Assert.That(gridPos.X, Is.EqualTo(2));
            Assert.That(gridPos.Z, Is.EqualTo(7));

            // Grid center for cell (2, 7) should be at (25, 0, 75)
            var cellCenter = map.GridToWorld(gridPos);
            Assert.That(cellCenter.X, Is.EqualTo(25.0f));
            Assert.That(cellCenter.Z, Is.EqualTo(75.0f));

            var roundTripGrid = map.WorldToGrid(cellCenter);
            Assert.That(roundTripGrid, Is.EqualTo(gridPos));
        }

        [Test]
        public void SimGridMap_NegativeCoordinates_ConvertCorrectly()
        {
            var map = new SimGridMap(width: 50, height: 50, cellSize: 10f);

            // -5.0 / 10.0 = -0.5, Floor(-0.5) = -1
            var negWorld = new Vec3(-5.0f, 0, -15.0f);
            var gridPos = map.WorldToGrid(negWorld);

            Assert.That(gridPos.X, Is.EqualTo(-1));
            Assert.That(gridPos.Z, Is.EqualTo(-2));

            Assert.That(map.IsInBounds(gridPos), Is.False);
            Assert.That(map.GetCell(gridPos), Is.EqualTo(SimCellType.Obstacle));
        }
    }
}
