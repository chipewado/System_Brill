using System;
using NUnit.Framework;
using Assistant.Core.Data;
using Assistant.Core.Discovery;

namespace Assistant.Tests
{
    [TestFixture]
    public class SpatialHashGridTests
    {
        private static readonly Vec3 Origin = new Vec3(0, 0, 0);

        [Test]
        public void NegativeCoordinates_AreFound()
        {
            var g = new SpatialHashGrid(50f);
            g.Insert("a", new Vec3(-10, 0, -10));
            Assert.That(g.QueryCandidates(Origin, 20f), Does.Contain("a"));
        }

        [Test]
        public void CellBoundary_TokenOnEdge_IsFound()
        {
            var g = new SpatialHashGrid(50f);
            g.Insert("edge", new Vec3(50f, 0, 0)); // belongs to cell (1, 0)
            Assert.That(g.QueryCandidates(new Vec3(49f, 0, 0), 2f), Does.Contain("edge"));
        }

        [Test]
        public void Update_CrossCell_MovesBucket()
        {
            var g = new SpatialHashGrid(50f);
            g.Insert("a", new Vec3(10, 0, 10));
            g.Update("a", new Vec3(120, 0, 120));
            Assert.That(g.QueryCandidates(Origin, 20f), Does.Not.Contain("a"));
            Assert.That(g.QueryCandidates(new Vec3(120, 0, 120), 20f), Does.Contain("a"));
        }

        [Test]
        public void Remove_ClearsBucket()
        {
            var g = new SpatialHashGrid(50f);
            g.Insert("a", new Vec3(10, 0, 10));
            Assert.That(g.Remove("a"), Is.True);
            Assert.That(g.Count, Is.EqualTo(0));
            Assert.That(g.QueryCandidates(Origin, 100f), Is.Empty);
        }

        [Test]
        public void NonFinitePosition_IsRejected()
        {
            var g = new SpatialHashGrid(50f);
            Assert.That(g.Insert("nan", new Vec3(float.NaN, 0, 0)), Is.False);
            Assert.That(g.Count, Is.EqualTo(0));
        }

        [Test, CancelAfter(2000)]
        public void HugeRadius_DoesNotHang()
        {
            var g = new SpatialHashGrid(50f);
            g.Insert("a", new Vec3(10, 0, 10));
            Assert.That(g.QueryCandidates(Origin, float.MaxValue), Does.Contain("a"));
            Assert.That(g.QueryCandidates(Origin, float.PositiveInfinity), Does.Contain("a"));
        }

        [Test]
        public void RandomData_CandidatesAreSupersetOfBruteForce()
        {
            var rng = new Random(12345);
            var g = new SpatialHashGrid(50f);
            var pts = new Vec3[2000];
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = new Vec3((float)(rng.NextDouble() * 4000 - 2000), 0, (float)(rng.NextDouble() * 4000 - 2000));
                g.Insert("t" + i, pts[i]);
            }

            for (int q = 0; q < 200; q++)
            {
                var c = new Vec3((float)(rng.NextDouble() * 4000 - 2000), 0, (float)(rng.NextDouble() * 4000 - 2000));
                float r = (float)(rng.NextDouble() * 300);
                var cand = g.QueryCandidates(c, r);
                for (int i = 0; i < pts.Length; i++)
                {
                    float dx = pts[i].X - c.X, dz = pts[i].Z - c.Z;
                    if (dx * dx + dz * dz <= r * r)
                    {
                        Assert.That(cand, Does.Contain("t" + i));
                    }
                }
            }
        }
    }
}
