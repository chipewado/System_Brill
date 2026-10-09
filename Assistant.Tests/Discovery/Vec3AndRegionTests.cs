using System;
using Assistant.Core.Data;
using NUnit.Framework;

namespace Assistant.Tests.Discovery
{
    [TestFixture]
    public class Vec3AndRegionTests
    {
        [Test]
        public void Vec3_Distance2D_CalculatesEuclideanDistanceOnXZPlaneIgnoringY()
        {
            var p1 = new Vec3(10f, 500f, 20f);
            var p2 = new Vec3(13f, -999f, 24f);

            // dx = 3, dz = 4, Euclidean distance = 5
            float dist2D = Vec3.Distance2D(p1, p2);

            Assert.That(dist2D, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void Region_Contains_ReturnsTrueInsideAndFalseOutside()
        {
            var center = new Vec3(100f, 0, 100f);
            var region = new Region(center, 50f);

            var inside = new Vec3(130f, 10f, 140f); // dx=30, dz=40 -> dist=50 (on boundary)
            var strictlyInside = new Vec3(110f, 0, 110f); // dist ~ 14.14
            var outside = new Vec3(150f, 0, 150f); // dx=50, dz=50 -> dist ~ 70.7

            Assert.That(region.Contains(inside), Is.True);
            Assert.That(region.Contains(strictlyInside), Is.True);
            Assert.That(region.Contains(outside), Is.False);
        }

        [Test]
        public void Region_Intersects_CorrectlyDetectsOverlappingRegions()
        {
            var r1 = new Region(new Vec3(0, 0, 0), 10f);
            var r2 = new Region(new Vec3(15, 0, 0), 10f); // Centers dist = 15 <= 20 -> Intersects
            var r3 = new Region(new Vec3(30, 0, 0), 5f);  // Centers dist = 30 > 15 -> Disjoint

            Assert.That(r1.Intersects(r2), Is.True);
            Assert.That(r2.Intersects(r1), Is.True);
            Assert.That(r1.Intersects(r3), Is.False);
            Assert.That(r1.Intersects(null!), Is.False);
        }
    }
}
