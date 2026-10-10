using System;
using Assistant.Core.Data;
using Assistant.Core.Minimap;
using NUnit.Framework;

namespace Assistant.Tests.Minimap
{
    [TestFixture]
    public class MinimapPropertyTests
    {
        [Test]
        public void Property_ClampedPointIsAlwaysInsidePaddedRectangle()
        {
            var rng = new Random(42);

            for (int trial = 0; trial < 500; trial++)
            {
                float width = (float)rng.Next(100, 500);
                float height = (float)rng.Next(100, 500);
                float padding = (float)rng.Next(0, (int)(Math.Min(width, height) * 0.45f));
                float scale = (float)(rng.NextDouble() * 5.0 + 0.1);
                float heading = (float)(rng.NextDouble() * 720.0 - 360.0);
                bool rotate = rng.Next(0, 2) == 1;

                var viewport = new MinimapViewport
                {
                    CenterWorld = new Vec2((float)(rng.NextDouble() * 2000 - 1000), (float)(rng.NextDouble() * 2000 - 1000)),
                    ViewportWidthPx = width,
                    ViewportHeightPx = height,
                    EdgePaddingPx = padding,
                    WorldUnitsPerPixel = scale,
                    HeadingDegrees = heading,
                    RotateWithPlayer = rotate
                };

                float limitX = width * 0.5f - padding;
                float limitY = height * 0.5f - padding;

                // Random target: both near and very far
                var target = new Vec3(
                    (float)(rng.NextDouble() * 20000 - 10000),
                    (float)(rng.NextDouble() * 100 - 50),
                    (float)(rng.NextDouble() * 20000 - 10000)
                );

                var result = MinimapMath.WorldToMinimap(target, viewport);

                Assert.That(Math.Abs(result.ScreenPositionPx.X), Is.LessThanOrEqualTo(limitX + 0.001f),
                    $"Trial {trial}: Clamped X must not exceed padded boundary");
                Assert.That(Math.Abs(result.ScreenPositionPx.Y), Is.LessThanOrEqualTo(limitY + 0.001f),
                    $"Trial {trial}: Clamped Y must not exceed padded boundary");
            }
        }

        [Test]
        public void Property_BearingEqualsAtan2OfScreenVector()
        {
            var rng = new Random(1337);

            for (int trial = 0; trial < 500; trial++)
            {
                var viewport = new MinimapViewport
                {
                    CenterWorld = new Vec2(0f, 0f),
                    ViewportWidthPx = 200f,
                    ViewportHeightPx = 200f,
                    EdgePaddingPx = 5f,
                    WorldUnitsPerPixel = 1.0f,
                    HeadingDegrees = (float)(rng.NextDouble() * 360.0),
                    RotateWithPlayer = rng.Next(0, 2) == 1
                };

                var target = new Vec3(
                    (float)(rng.NextDouble() * 2000 - 1000),
                    0f,
                    (float)(rng.NextDouble() * 2000 - 1000)
                );

                var result = MinimapMath.WorldToMinimap(target, viewport);

                Assert.That(result.DirectionAngleDegrees, Is.GreaterThanOrEqualTo(0f).And.LessThan(360f),
                    $"Trial {trial}: Bearing must be normalized in [0, 360)");

                if (Math.Abs(result.ScreenPositionPx.X) > 0.001f || Math.Abs(result.ScreenPositionPx.Y) > 0.001f)
                {
                    float expectedBearing = MathF.Atan2(result.ScreenPositionPx.X, result.ScreenPositionPx.Y) * (180f / MathF.PI);
                    if (expectedBearing < 0f) expectedBearing += 360f;
                    if (expectedBearing >= 360f) expectedBearing -= 360f;

                    Assert.That(result.DirectionAngleDegrees, Is.EqualTo(expectedBearing).Within(0.05f),
                        $"Trial {trial}: DirectionAngleDegrees must match atan2(screenX, screenY)");
                }
            }
        }

        [Test]
        public void Property_RayDirectionPreservedBeforeAndAfterClamping()
        {
            var rng = new Random(9999);

            for (int trial = 0; trial < 500; trial++)
            {
                var viewport = new MinimapViewport
                {
                    CenterWorld = new Vec2(0f, 0f),
                    ViewportWidthPx = 200f,
                    ViewportHeightPx = 200f,
                    EdgePaddingPx = 4f,
                    WorldUnitsPerPixel = 1.0f,
                    HeadingDegrees = (float)(rng.NextDouble() * 360.0),
                    RotateWithPlayer = true
                };

                // Targets guaranteed to be off-screen
                float dist = (float)(rng.NextDouble() * 5000 + 200);
                float angle = (float)(rng.NextDouble() * Math.PI * 2);
                var target = new Vec3(MathF.Sin(angle) * dist, 0f, MathF.Cos(angle) * dist);

                var result = MinimapMath.WorldToMinimap(target, viewport);
                Assert.That(result.IsOffScreen, Is.True);

                // Unclamped rotated vector
                double rad = viewport.HeadingDegrees * (Math.PI / 180.0);
                double rx = target.X * Math.Cos(rad) - target.Z * Math.Sin(rad);
                double ry = target.X * Math.Sin(rad) + target.Z * Math.Cos(rad);

                double unconstrainedBearing = Math.Atan2(rx, ry) * (180.0 / Math.PI);
                unconstrainedBearing = (unconstrainedBearing % 360.0 + 360.0) % 360.0;

                Assert.That(result.DirectionAngleDegrees, Is.EqualTo((float)unconstrainedBearing).Within(0.05f),
                    $"Trial {trial}: Ray angle must be strictly preserved by clamping");
            }
        }

        [Test]
        public void Property_DistanceWorldMatches2DEuclideanDistance()
        {
            var rng = new Random(777);

            for (int trial = 0; trial < 500; trial++)
            {
                var center = new Vec2((float)(rng.NextDouble() * 1000 - 500), (float)(rng.NextDouble() * 1000 - 500));
                var viewport = new MinimapViewport
                {
                    CenterWorld = center,
                    ViewportWidthPx = 200f,
                    ViewportHeightPx = 200f,
                    EdgePaddingPx = 4f,
                    WorldUnitsPerPixel = 1.0f,
                    HeadingDegrees = 0f,
                    RotateWithPlayer = true
                };

                var target = new Vec3(
                    (float)(rng.NextDouble() * 1000 - 500),
                    (float)(rng.NextDouble() * 50 - 25), // Y ignored
                    (float)(rng.NextDouble() * 1000 - 500)
                );

                var result = MinimapMath.WorldToMinimap(target, viewport);

                float dx = target.X - center.X;
                float dz = target.Z - center.Y;
                float expectedDist = MathF.Sqrt(dx * dx + dz * dz);

                Assert.That(result.DistanceWorld, Is.EqualTo(expectedDist).Within(0.01f),
                    $"Trial {trial}: DistanceWorld must equal 2D Euclidean distance");
            }
        }
    }
}
