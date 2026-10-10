using System;
using Assistant.Core.Data;
using Assistant.Core.Minimap;
using NUnit.Framework;

namespace Assistant.Tests.Minimap
{
    [TestFixture]
    public class MinimapMathTests
    {
        private MinimapViewport _defaultViewport = null!;

        [SetUp]
        public void SetUp()
        {
            _defaultViewport = new MinimapViewport
            {
                CenterWorld = new Vec2(0f, 0f),
                ViewportWidthPx = 200f,
                ViewportHeightPx = 200f,
                WorldUnitsPerPixel = 1.0f,
                HeadingDegrees = 0f,
                RotateWithPlayer = true,
                EdgePaddingPx = 4f
            };
        }

        #region Contract Tests (Section 6.2)

        [Test]
        public void WorldToMinimap_TargetAtPlayerPosition_ReturnsZeroOffsetAndNotOffScreen()
        {
            var target = new Vec3(0f, 10f, 0f); // Y altitude is ignored in 2D minimap
            var result = MinimapMath.WorldToMinimap(target, _defaultViewport);

            Assert.That(result.ScreenPositionPx, Is.EqualTo(Vec2.Zero));
            Assert.That(result.IsOffScreen, Is.False);
            Assert.That(result.DirectionAngleDegrees, Is.EqualTo(0f));
            Assert.That(result.DistanceWorld, Is.EqualTo(0f));
        }

        [Test]
        public void WorldToMinimap_TargetOutsideBounds_ClampsPreservingRayDirection()
        {
            // Padded limit is 100 - 4 = 96px
            // Target is far at (300, 0, 600)
            var target = new Vec3(300f, 0f, 600f);
            var result = MinimapMath.WorldToMinimap(target, _defaultViewport);

            Assert.That(result.IsOffScreen, Is.True);

            float limitX = 96f;
            float limitY = 96f;
            Assert.That(Math.Abs(result.ScreenPositionPx.X), Is.LessThanOrEqualTo(limitX + 0.001f));
            Assert.That(Math.Abs(result.ScreenPositionPx.Y), Is.LessThanOrEqualTo(limitY + 0.001f));

            // Verify ray direction preservation: atan2(screenX, screenY) equals atan2(targetX, targetZ)
            float originalBearing = MathF.Atan2(target.X, target.Z) * (180f / MathF.PI);
            if (originalBearing < 0f) originalBearing += 360f;

            float resultBearing = MathF.Atan2(result.ScreenPositionPx.X, result.ScreenPositionPx.Y) * (180f / MathF.PI);
            if (resultBearing < 0f) resultBearing += 360f;

            Assert.That(result.DirectionAngleDegrees, Is.EqualTo(originalBearing).Within(0.01f));
            Assert.That(resultBearing, Is.EqualTo(originalBearing).Within(0.01f));
        }

        [Test]
        public void WorldToMinimap_RotationActive_RotatesCoordinatesCounterClockwiseByHeading()
        {
            _defaultViewport.HeadingDegrees = 45f;
            _defaultViewport.RotateWithPlayer = true;

            // Target straight North in world (0, 0, 10)
            var targetNorth = new Vec3(0f, 0f, 10f);
            var result = MinimapMath.WorldToMinimap(targetNorth, _defaultViewport);

            // Heading is 45° (North-East). Player faces NE.
            // North is 45° to the player's left, so screen vector should point Top-Left (-X, +Y)
            Assert.That(result.ScreenPositionPx.X, Is.LessThan(0f));
            Assert.That(result.ScreenPositionPx.Y, Is.GreaterThan(0f));

            // Angle from screen-up (+Y) clockwise to screen vector:
            // Heading is 45°, true North is at bearing 315° on screen (45° counter-clockwise from screen UP)
            Assert.That(result.DirectionAngleDegrees, Is.EqualTo(315f).Within(0.01f));
        }

        [Test]
        public void WorldToMinimap_RotationPromptVectors_MatchExactSpecification()
        {
            _defaultViewport.WorldUnitsPerPixel = 1.0f;
            _defaultViewport.RotateWithPlayer = true;

            // Test Vector 1: h = 0°, East (1, 0) -> (1, 0) Right
            _defaultViewport.HeadingDegrees = 0f;
            var eastResH0 = MinimapMath.WorldToMinimap(new Vec3(1f, 0f, 0f), _defaultViewport);
            Assert.That(eastResH0.ScreenPositionPx.X, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(eastResH0.ScreenPositionPx.Y, Is.EqualTo(0f).Within(0.0001f));

            // North (0, 1) -> (0, 1) Up
            var northResH0 = MinimapMath.WorldToMinimap(new Vec3(0f, 0f, 1f), _defaultViewport);
            Assert.That(northResH0.ScreenPositionPx.X, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(northResH0.ScreenPositionPx.Y, Is.EqualTo(1f).Within(0.0001f));

            // Test Vector 2: h = 90°, East (1, 0) -> (0, 1) Up; North (0, 1) -> (-1, 0) Left
            _defaultViewport.HeadingDegrees = 90f;
            var eastResH90 = MinimapMath.WorldToMinimap(new Vec3(1f, 0f, 0f), _defaultViewport);
            Assert.That(eastResH90.ScreenPositionPx.X, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(eastResH90.ScreenPositionPx.Y, Is.EqualTo(1f).Within(0.0001f));

            var northResH90 = MinimapMath.WorldToMinimap(new Vec3(0f, 0f, 1f), _defaultViewport);
            Assert.That(northResH90.ScreenPositionPx.X, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(northResH90.ScreenPositionPx.Y, Is.EqualTo(0f).Within(0.0001f));

            // Test Vector 3: h = 180°, North (0, 1) -> (0, -1) Down
            _defaultViewport.HeadingDegrees = 180f;
            var northResH180 = MinimapMath.WorldToMinimap(new Vec3(0f, 0f, 1f), _defaultViewport);
            Assert.That(northResH180.ScreenPositionPx.X, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(northResH180.ScreenPositionPx.Y, Is.EqualTo(-1f).Within(0.0001f));
        }

        [Test]
        public void WorldToMinimap_NonRotated_MaintainsNorthUpOrientation()
        {
            _defaultViewport.HeadingDegrees = 120f;
            _defaultViewport.RotateWithPlayer = false;

            // Target straight North (0, 0, 50)
            var targetNorth = new Vec3(0f, 0f, 50f);
            var resultNorth = MinimapMath.WorldToMinimap(targetNorth, _defaultViewport);
            Assert.That(resultNorth.ScreenPositionPx.X, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(resultNorth.ScreenPositionPx.Y, Is.EqualTo(50f).Within(0.0001f));
            Assert.That(resultNorth.DirectionAngleDegrees, Is.EqualTo(0f).Within(0.0001f));

            // Target straight East (50, 0, 0)
            var targetEast = new Vec3(50f, 0f, 0f);
            var resultEast = MinimapMath.WorldToMinimap(targetEast, _defaultViewport);
            Assert.That(resultEast.ScreenPositionPx.X, Is.EqualTo(50f).Within(0.0001f));
            Assert.That(resultEast.ScreenPositionPx.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(resultEast.DirectionAngleDegrees, Is.EqualTo(90f).Within(0.0001f));
        }

        [Test]
        public void WorldToMinimap_ZoomFactor_ScalesPixelOffsetsProportionally()
        {
            var config = new OverlayConfig();
            float baseUnitsPerPixel = 1.0f;

            // Zoom 1.0x -> WorldUnitsPerPixel = 1.0 / 1.0 = 1.0
            config.MinimapZoom = 1.0f;
            _defaultViewport.WorldUnitsPerPixel = baseUnitsPerPixel / config.MinimapZoom;
            var res1 = MinimapMath.WorldToMinimap(new Vec3(20f, 0f, 20f), _defaultViewport);

            // Zoom 2.0x -> WorldUnitsPerPixel = 1.0 / 2.0 = 0.5 (more zoomed in, so offset in pixels is double)
            config.MinimapZoom = 2.0f;
            _defaultViewport.WorldUnitsPerPixel = baseUnitsPerPixel / config.MinimapZoom;
            var res2 = MinimapMath.WorldToMinimap(new Vec3(20f, 0f, 20f), _defaultViewport);

            Assert.That(res2.ScreenPositionPx.X, Is.EqualTo(res1.ScreenPositionPx.X * 2f).Within(0.001f));
            Assert.That(res2.ScreenPositionPx.Y, Is.EqualTo(res1.ScreenPositionPx.Y * 2f).Within(0.001f));
            Assert.That(res1.DistanceWorld, Is.EqualTo(res2.DistanceWorld));
        }

        [Test]
        public void WorldToMinimap_InvalidScaleOrNegativeDimensions_ThrowsArgumentException()
        {
            // Null viewport throws ArgumentNullException
            Assert.Throws<ArgumentNullException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, null!));

            // Zero or negative WorldUnitsPerPixel throws ArgumentException
            _defaultViewport.WorldUnitsPerPixel = 0f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            _defaultViewport.WorldUnitsPerPixel = -1f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            // Non-finite WorldUnitsPerPixel
            _defaultViewport.WorldUnitsPerPixel = float.NaN;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            _defaultViewport.WorldUnitsPerPixel = float.PositiveInfinity;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            // Non-positive ViewportWidthPx / HeightPx
            _defaultViewport.WorldUnitsPerPixel = 1.0f;
            _defaultViewport.ViewportWidthPx = 0f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            _defaultViewport.ViewportWidthPx = -50f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            _defaultViewport.ViewportWidthPx = 200f;
            _defaultViewport.ViewportHeightPx = 0f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            _defaultViewport.ViewportHeightPx = -100f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));
        }

        [Test]
        public void WorldToMinimap_InvalidEdgePadding_ThrowsArgumentException()
        {
            // Negative padding throws ArgumentException
            _defaultViewport.EdgePaddingPx = -1f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            // Padding greater than or equal to half extents (100px) throws ArgumentException
            _defaultViewport.EdgePaddingPx = 100f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));

            _defaultViewport.EdgePaddingPx = 150f;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(Vec3.Zero, _defaultViewport));
        }

        [Test]
        public void WorldToMinimap_NaNCoordinates_ReturnsSafeDefaultWithoutThrowing()
        {
            var targetWithNaNX = new Vec3(float.NaN, 0f, 10f);
            var resNaNX = MinimapMath.WorldToMinimap(targetWithNaNX, _defaultViewport);
            Assert.That(resNaNX.ScreenPositionPx, Is.EqualTo(Vec2.Zero));
            Assert.That(resNaNX.IsOffScreen, Is.True);
            Assert.That(resNaNX.DirectionAngleDegrees, Is.EqualTo(0f));
            Assert.That(resNaNX.DistanceWorld, Is.EqualTo(0f));

            var targetWithNaNZ = new Vec3(10f, 0f, float.NaN);
            var resNaNZ = MinimapMath.WorldToMinimap(targetWithNaNZ, _defaultViewport);
            Assert.That(resNaNZ.ScreenPositionPx, Is.EqualTo(Vec2.Zero));
            Assert.That(resNaNZ.IsOffScreen, Is.True);

            var targetWithInf = new Vec3(float.PositiveInfinity, 0f, 10f);
            var resInf = MinimapMath.WorldToMinimap(targetWithInf, _defaultViewport);
            Assert.That(resInf.ScreenPositionPx, Is.EqualTo(Vec2.Zero));
            Assert.That(resInf.IsOffScreen, Is.True);
        }

        [Test]
        public void WorldToMinimap_NaNOrInfiniteViewportCoordinates_ThrowsArgumentException()
        {
            _defaultViewport.CenterWorld = new Vec2(float.NaN, 0f);
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(new Vec3(10, 0, 10), _defaultViewport));

            _defaultViewport.CenterWorld = new Vec2(0f, float.PositiveInfinity);
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(new Vec3(10, 0, 10), _defaultViewport));

            _defaultViewport.CenterWorld = new Vec2(0f, 0f);
            _defaultViewport.HeadingDegrees = float.NaN;
            Assert.Throws<ArgumentException>(() => MinimapMath.WorldToMinimap(new Vec3(10, 0, 10), _defaultViewport));
        }

        #endregion

        #region Invariants, Boundaries, and Edge Cases

        [Test]
        public void WorldToMinimap_CardinalDirections_CalculatesAccurateBearings()
        {
            // North (0, 0, 50) -> 0°
            var north = MinimapMath.WorldToMinimap(new Vec3(0f, 0f, 50f), _defaultViewport);
            Assert.That(north.DirectionAngleDegrees, Is.EqualTo(0f).Within(0.001f));

            // East (50, 0, 0) -> 90°
            var east = MinimapMath.WorldToMinimap(new Vec3(50f, 0f, 0f), _defaultViewport);
            Assert.That(east.DirectionAngleDegrees, Is.EqualTo(90f).Within(0.001f));

            // South (0, 0, -50) -> 180°
            var south = MinimapMath.WorldToMinimap(new Vec3(0f, 0f, -50f), _defaultViewport);
            Assert.That(south.DirectionAngleDegrees, Is.EqualTo(180f).Within(0.001f));

            // West (-50, 0, 0) -> 270°
            var west = MinimapMath.WorldToMinimap(new Vec3(-50f, 0f, 0f), _defaultViewport);
            Assert.That(west.DirectionAngleDegrees, Is.EqualTo(270f).Within(0.001f));
        }

        [Test]
        public void WorldToMinimap_PointsExactlyOnMinimapBoundary_AreNotOffScreen()
        {
            // Half extent is 100px. Edge padding is 4px. Limit is exactly 96px.
            // With WorldUnitsPerPixel = 1.0, world coordinate 96 is exactly on the boundary.
            var onRightBoundary = new Vec3(96f, 0f, 0f);
            var resRight = MinimapMath.WorldToMinimap(onRightBoundary, _defaultViewport);
            Assert.That(resRight.IsOffScreen, Is.False, "Points exactly on boundary limit must NOT be marked off-screen");
            Assert.That(resRight.ScreenPositionPx.X, Is.EqualTo(96f).Within(0.0001f));

            var onTopBoundary = new Vec3(0f, 0f, 96f);
            var resTop = MinimapMath.WorldToMinimap(onTopBoundary, _defaultViewport);
            Assert.That(resTop.IsOffScreen, Is.False, "Points exactly on boundary limit must NOT be marked off-screen");
            Assert.That(resTop.ScreenPositionPx.Y, Is.EqualTo(96f).Within(0.0001f));

            var justPastBoundary = new Vec3(96.01f, 0f, 0f);
            var resPast = MinimapMath.WorldToMinimap(justPastBoundary, _defaultViewport);
            Assert.That(resPast.IsOffScreen, Is.True, "Points strictly outside boundary limit must be marked off-screen");
            Assert.That(resPast.ScreenPositionPx.X, Is.EqualTo(96f).Within(0.001f));
        }

        [Test]
        public void WorldToMinimap_ZeroComponentRays_HandlesPureHorizontalAndVerticalRaysWithoutDivisionByZero()
        {
            // dx == 0, pure vertical North
            var pureNorth = new Vec3(0f, 0f, 500f);
            var resN = MinimapMath.WorldToMinimap(pureNorth, _defaultViewport);
            Assert.That(resN.IsOffScreen, Is.True);
            Assert.That(resN.ScreenPositionPx.X, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(resN.ScreenPositionPx.Y, Is.EqualTo(96f).Within(0.0001f));
            Assert.That(resN.DirectionAngleDegrees, Is.EqualTo(0f));

            // dx == 0, pure vertical South
            var pureSouth = new Vec3(0f, 0f, -500f);
            var resS = MinimapMath.WorldToMinimap(pureSouth, _defaultViewport);
            Assert.That(resS.IsOffScreen, Is.True);
            Assert.That(resS.ScreenPositionPx.X, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(resS.ScreenPositionPx.Y, Is.EqualTo(-96f).Within(0.0001f));
            Assert.That(resS.DirectionAngleDegrees, Is.EqualTo(180f));

            // dz == 0, pure horizontal East
            var pureEast = new Vec3(500f, 0f, 0f);
            var resE = MinimapMath.WorldToMinimap(pureEast, _defaultViewport);
            Assert.That(resE.IsOffScreen, Is.True);
            Assert.That(resE.ScreenPositionPx.X, Is.EqualTo(96f).Within(0.0001f));
            Assert.That(resE.ScreenPositionPx.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(resE.DirectionAngleDegrees, Is.EqualTo(90f));

            // dz == 0, pure horizontal West
            var pureWest = new Vec3(-500f, 0f, 0f);
            var resW = MinimapMath.WorldToMinimap(pureWest, _defaultViewport);
            Assert.That(resW.IsOffScreen, Is.True);
            Assert.That(resW.ScreenPositionPx.X, Is.EqualTo(-96f).Within(0.0001f));
            Assert.That(resW.ScreenPositionPx.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(resW.DirectionAngleDegrees, Is.EqualTo(270f));
        }

        [Test]
        public void WorldToMinimap_RectangleCorners_PreservesDirectionAngleAcrossAllQuadrants()
        {
            float[] signsX = { 1f, -1f, 1f, -1f };
            float[] signsZ = { 1f, 1f, -1f, -1f };

            for (int i = 0; i < 4; i++)
            {
                var target = new Vec3(signsX[i] * 400f, 0f, signsZ[i] * 400f);
                var result = MinimapMath.WorldToMinimap(target, _defaultViewport);

                Assert.That(result.IsOffScreen, Is.True);
                Assert.That(Math.Abs(result.ScreenPositionPx.X), Is.EqualTo(96f).Within(0.001f));
                Assert.That(Math.Abs(result.ScreenPositionPx.Y), Is.EqualTo(96f).Within(0.001f));

                float expectedBearing = MathF.Atan2(target.X, target.Z) * (180f / MathF.PI);
                if (expectedBearing < 0f) expectedBearing += 360f;

                Assert.That(result.DirectionAngleDegrees, Is.EqualTo(expectedBearing).Within(0.01f));
            }
        }

        [Test]
        public void WorldToMinimap_HeadingNormalizationAndWraparound_HandlesNegativeAndOver360Degrees()
        {
            var target = new Vec3(10f, 0f, 10f);

            _defaultViewport.HeadingDegrees = 45f;
            var res45 = MinimapMath.WorldToMinimap(target, _defaultViewport);

            _defaultViewport.HeadingDegrees = 405f; // 45 + 360
            var res405 = MinimapMath.WorldToMinimap(target, _defaultViewport);

            _defaultViewport.HeadingDegrees = -315f; // 45 - 360
            var resNeg315 = MinimapMath.WorldToMinimap(target, _defaultViewport);

            Assert.That(res405.ScreenPositionPx.X, Is.EqualTo(res45.ScreenPositionPx.X).Within(0.001f));
            Assert.That(res405.ScreenPositionPx.Y, Is.EqualTo(res45.ScreenPositionPx.Y).Within(0.001f));
            Assert.That(res405.DirectionAngleDegrees, Is.EqualTo(res45.DirectionAngleDegrees).Within(0.001f));

            Assert.That(resNeg315.ScreenPositionPx.X, Is.EqualTo(res45.ScreenPositionPx.X).Within(0.001f));
            Assert.That(resNeg315.ScreenPositionPx.Y, Is.EqualTo(res45.ScreenPositionPx.Y).Within(0.001f));
            Assert.That(resNeg315.DirectionAngleDegrees, Is.EqualTo(res45.DirectionAngleDegrees).Within(0.001f));
        }

        [Test]
        public void WorldToMinimap_VeryLargeFiniteCoordinates_CalculatesWithoutOverflowOrException()
        {
            var hugeTarget = new Vec3(1e30f, 0f, 1e30f);

            var result = MinimapMath.WorldToMinimap(hugeTarget, _defaultViewport);

            Assert.That(result.IsOffScreen, Is.True);
            Assert.That(float.IsNaN(result.ScreenPositionPx.X), Is.False);
            Assert.That(float.IsNaN(result.ScreenPositionPx.Y), Is.False);
            Assert.That(Math.Abs(result.ScreenPositionPx.X), Is.LessThanOrEqualTo(96.001f));
            Assert.That(Math.Abs(result.ScreenPositionPx.Y), Is.LessThanOrEqualTo(96.001f));
            Assert.That(result.DirectionAngleDegrees, Is.EqualTo(45f).Within(0.01f));
        }

        [Test]
        public void WorldToMinimap_ZeroAllocations_Over50kCalls()
        {
            var target = new Vec3(123.456f, 0f, 789.012f);
            // Warm-up JIT
            for (int i = 0; i < 1_000; i++)
            {
                _ = MinimapMath.WorldToMinimap(target, _defaultViewport);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50_000; i++)
            {
                _ = MinimapMath.WorldToMinimap(target, _defaultViewport);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L), "WorldToMinimap must not allocate any heap memory over 50k calls.");
        }

        [Test]
        public void ViewportFactory_FromOverlayConfig_DerivesWorldUnitsPerPixelCorrectly()
        {
            var config = new OverlayConfig
            {
                MinimapZoom = 2.0f
            };

            var viewport = MinimapViewport.CreateFromConfig(
                config,
                centerWorld: new Vec2(100f, 200f),
                headingDegrees: 90f,
                baseWorldUnitsPerPixel: 1.0f);

            Assert.That(viewport.WorldUnitsPerPixel, Is.EqualTo(0.5f).Within(0.0001f)); // 1.0 / 2.0 = 0.5
            Assert.That(viewport.CenterWorld.X, Is.EqualTo(100f));
            Assert.That(viewport.CenterWorld.Y, Is.EqualTo(200f));
            Assert.That(viewport.HeadingDegrees, Is.EqualTo(90f));
            Assert.That(viewport.IsValid, Is.True);

            // Also test extension/method directly on config
            var fromOverlay = config.CreateViewport(new Vec2(10f, 20f), 0f, 2.0f);
            Assert.That(fromOverlay.WorldUnitsPerPixel, Is.EqualTo(1.0f).Within(0.0001f)); // 2.0 / 2.0 = 1.0
        }

        [Test]
        public void MinimapViewport_IsValid_DetectsValidAndInvalidSettings()
        {
            Assert.That(_defaultViewport.IsValid, Is.True);

            var invalidWidth = new MinimapViewport { ViewportWidthPx = -10f };
            Assert.That(invalidWidth.IsValid, Is.False);

            var invalidPadding = new MinimapViewport { ViewportWidthPx = 100f, ViewportHeightPx = 100f, EdgePaddingPx = 50f };
            Assert.That(invalidPadding.IsValid, Is.False);

            var nanHeading = new MinimapViewport { HeadingDegrees = float.NaN };
            Assert.That(nanHeading.IsValid, Is.False);
        }

        [Test]
        public void TryWorldToMinimap_ValidAndInvalidViewport_BehavesSafely()
        {
            var target = new Vec3(50f, 0f, 50f);

            bool ok = MinimapMath.TryWorldToMinimap(target, _defaultViewport, out var result);
            Assert.That(ok, Is.True);
            Assert.That(result.DistanceWorld, Is.GreaterThan(0f));

            bool nullOk = MinimapMath.TryWorldToMinimap(target, null, out var nullResult);
            Assert.That(nullOk, Is.False);
            Assert.That(nullResult.ScreenPositionPx, Is.EqualTo(Vec2.Zero));

            var invalidViewport = new MinimapViewport { ViewportWidthPx = -1f };
            bool invalidOk = MinimapMath.TryWorldToMinimap(target, invalidViewport, out var invalidResult);
            Assert.That(invalidOk, Is.False);
        }

        [Test]
        public void WorldToMinimap_DeterministicRepeatedCalls_YieldsIdenticalResults()
        {
            var target = new Vec3(123.456f, 0f, 789.012f);
            var first = MinimapMath.WorldToMinimap(target, _defaultViewport);

            for (int i = 0; i < 50; i++)
            {
                var repeat = MinimapMath.WorldToMinimap(target, _defaultViewport);
                Assert.That(repeat.ScreenPositionPx, Is.EqualTo(first.ScreenPositionPx));
                Assert.That(repeat.IsOffScreen, Is.EqualTo(first.IsOffScreen));
                Assert.That(repeat.DirectionAngleDegrees, Is.EqualTo(first.DirectionAngleDegrees));
                Assert.That(repeat.DistanceWorld, Is.EqualTo(first.DistanceWorld));
            }
        }

        #endregion
    }
}
