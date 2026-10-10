using System;
using Assistant.Core.Data;

namespace Assistant.Core.Minimap
{
    /// <summary>
    /// Pure mathematical functions for 2D minimap / radar projection and ray-preserving clamping.
    ///
    /// CONVENTIONS:
    /// 1. World Coordinates:
    ///    - +X = East
    ///    - +Z = North
    ///    - +Y = Up (vertical / altitude, ignored in 2D minimap projection)
    ///    - Heading h = clockwise yaw in degrees from North (0° = North, 90° = East, 180° = South, 270° = West).
    ///
    /// 2. Screen / Minimap Coordinates:
    ///    - Origin (0, 0) is at the exact center of the minimap UI widget.
    ///    - +X_screen = Right
    ///    - +Y_screen = UP (presentation layers such as Unity convert to their own local UI coordinates).
    ///
    /// 3. Transformation & Rotation:
    ///    - World offset d = (target.X - center.X, target.Z - center.Z).
    ///    - If RotateWithPlayer is true:
    ///      screen = rotateCCW(d, h) so player heading points UP (+Y_screen).
    ///      Rotation matrix:
    ///        rx = dx * cos(h) - dz * sin(h)
    ///        ry = dx * sin(h) + dz * cos(h)
    ///      Test vectors (at scale 1):
    ///        h = 0°:   East (1, 0) -> (1, 0);  North (0, 1) -> (0, 1)
    ///        h = 90°:  East (1, 0) -> (0, 1) UP; North (0, 1) -> (-1, 0)
    ///        h = 180°: North (0, 1) -> (0, -1)
    ///    - Pixel scaling: divide by WorldUnitsPerPixel (= BaseWorldUnitsPerPixel / OverlayConfig.MinimapZoom).
    ///
    /// 4. Clamping:
    ///    - Padded rectangle: half extents minus EdgePaddingPx.
    ///    - IsOffScreen = strictly outside the padded rectangle.
    ///    - Ray-preserving clamping scales the (rx, ry) vector by a single uniform scalar:
    ///        scale = min((halfW - pad) / |rx|, (halfH - pad) / |ry|)
    ///      preserving DirectionAngleDegrees exactly without distortion.
    ///
    /// 5. Direction Angle & Distance:
    ///    - DirectionAngleDegrees = clockwise bearing from screen-up in [0, 360).
    ///    - DistanceWorld = unscaled 2D Euclidean distance in world units (X-Z plane).
    ///
    /// ALLOCATIONS:
    /// Zero heap allocations: pure static method returning readonly struct.
    /// </summary>
    public static class MinimapMath
    {
        private const double RadToDeg = 180.0 / Math.PI;
        private const double DegToRad = Math.PI / 180.0;

        /// <summary>
        /// Attempts to transform a 3D world position into 2D minimap widget coordinates.
        /// Returns false if viewport is null or invalid, avoiding exceptions in presentation/UI loops.
        /// </summary>
        public static bool TryWorldToMinimap(Vec3 targetWorld, MinimapViewport? viewport, out MinimapCoordinateResult result)
        {
            if (viewport == null || !viewport.IsValid)
            {
                result = default;
                return false;
            }

            result = WorldToMinimap(targetWorld, viewport);
            return true;
        }

        /// <summary>
        /// Transforms a 3D world position into 2D minimap widget coordinates with ray clamping.
        /// </summary>
        /// <param name="targetWorld">Target point in world space.</param>
        /// <param name="viewport">Minimap geometry and camera settings.</param>
        /// <returns>Transformed and clamped minimap coordinate result.</returns>
        /// <exception cref="ArgumentNullException">Thrown if viewport is null.</exception>
        /// <exception cref="ArgumentException">Thrown if viewport dimensions, scale, padding, or parameters are invalid or non-finite.</exception>
        public static MinimapCoordinateResult WorldToMinimap(Vec3 targetWorld, MinimapViewport viewport)
        {
            if (viewport == null)
            {
                throw new ArgumentNullException(nameof(viewport));
            }

            // Viewport configuration validation: non-positive dimensions, invalid scale, or non-finite settings throw ArgumentException
            if (viewport.ViewportWidthPx <= 0f || float.IsNaN(viewport.ViewportWidthPx) || float.IsInfinity(viewport.ViewportWidthPx))
            {
                throw new ArgumentException("ViewportWidthPx must be positive and finite.", nameof(viewport));
            }

            if (viewport.ViewportHeightPx <= 0f || float.IsNaN(viewport.ViewportHeightPx) || float.IsInfinity(viewport.ViewportHeightPx))
            {
                throw new ArgumentException("ViewportHeightPx must be positive and finite.", nameof(viewport));
            }

            if (viewport.WorldUnitsPerPixel <= 0f || float.IsNaN(viewport.WorldUnitsPerPixel) || float.IsInfinity(viewport.WorldUnitsPerPixel))
            {
                throw new ArgumentException("WorldUnitsPerPixel must be positive and finite.", nameof(viewport));
            }

            if (viewport.EdgePaddingPx < 0f || float.IsNaN(viewport.EdgePaddingPx) || float.IsInfinity(viewport.EdgePaddingPx))
            {
                throw new ArgumentException("EdgePaddingPx must be non-negative and finite.", nameof(viewport));
            }

            double halfW = (double)viewport.ViewportWidthPx * 0.5;
            double halfH = (double)viewport.ViewportHeightPx * 0.5;
            double pad = (double)viewport.EdgePaddingPx;

            if (pad >= halfW || pad >= halfH)
            {
                throw new ArgumentException("EdgePaddingPx must be strictly less than viewport half-extents.", nameof(viewport));
            }

            if (float.IsNaN(viewport.HeadingDegrees) || float.IsInfinity(viewport.HeadingDegrees))
            {
                throw new ArgumentException("HeadingDegrees must be finite.", nameof(viewport));
            }

            if (float.IsNaN(viewport.CenterWorld.X) || float.IsInfinity(viewport.CenterWorld.X) ||
                float.IsNaN(viewport.CenterWorld.Y) || float.IsInfinity(viewport.CenterWorld.Y))
            {
                throw new ArgumentException("CenterWorld coordinates must be finite.", nameof(viewport));
            }

            // Non-finite target coordinates return safe default off-screen result without throwing
            if (float.IsNaN(targetWorld.X) || float.IsInfinity(targetWorld.X) ||
                float.IsNaN(targetWorld.Y) || float.IsInfinity(targetWorld.Y) ||
                float.IsNaN(targetWorld.Z) || float.IsInfinity(targetWorld.Z))
            {
                return new MinimapCoordinateResult(Vec2.Zero, true, 0f, 0f);
            }

            // Use double precision for intermediate math to safely handle very large coordinates without intermediate overflow
            double dx = (double)targetWorld.X - (double)viewport.CenterWorld.X;
            double dz = (double)targetWorld.Z - (double)viewport.CenterWorld.Y;

            double distWorldD = Math.Sqrt(dx * dx + dz * dz);
            float distanceWorld = double.IsInfinity(distWorldD) || distWorldD > float.MaxValue
                ? float.MaxValue
                : (float)distWorldD;

            // Target exactly at center
            if (dx == 0.0 && dz == 0.0)
            {
                return new MinimapCoordinateResult(Vec2.Zero, false, 0f, 0f);
            }

            // Unrotated screen pixel offsets (+X = East, +Y = North)
            double scale = (double)viewport.WorldUnitsPerPixel;
            double px = dx / scale;
            double py = dz / scale;

            double rx;
            double ry;

            if (viewport.RotateWithPlayer)
            {
                // Counter-clockwise rotation by heading degrees so heading points UP (+Y_screen)
                double rad = (double)viewport.HeadingDegrees * DegToRad;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);

                rx = px * cos - py * sin;
                ry = px * sin + py * cos;
            }
            else
            {
                rx = px;
                ry = py;
            }

            // Clockwise bearing from screen-up (+Y_screen) in [0, 360)
            // Screen vector is (rx, ry) where +X is Right and +Y is Up
            double bearingDeg = Math.Atan2(rx, ry) * RadToDeg;
            bearingDeg = (bearingDeg % 360.0 + 360.0) % 360.0;
            float bearing = (float)bearingDeg;

            // Clamping boundaries
            double limitX = halfW - pad;
            double limitY = halfH - pad;

            double absX = Math.Abs(rx);
            double absY = Math.Abs(ry);

            // Points strictly outside boundary are off-screen; points exactly on boundary are on-screen
            bool isOffScreen = absX > limitX || absY > limitY;

            double clampedX = rx;
            double clampedY = ry;

            if (isOffScreen)
            {
                double clampScale;

                if (absX == 0.0)
                {
                    // Vertical ray
                    clampScale = limitY / absY;
                }
                else if (absY == 0.0)
                {
                    // Horizontal ray
                    clampScale = limitX / absX;
                }
                else
                {
                    double scaleX = limitX / absX;
                    double scaleY = limitY / absY;
                    clampScale = Math.Min(scaleX, scaleY);
                }

                clampedX = rx * clampScale;
                clampedY = ry * clampScale;

                // Guard against minute floating-point rounding errors
                clampedX = Math.Clamp(clampedX, -limitX, limitX);
                clampedY = Math.Clamp(clampedY, -limitY, limitY);
            }

            var screenPos = new Vec2((float)clampedX, (float)clampedY);
            return new MinimapCoordinateResult(screenPos, isOffScreen, bearing, distanceWorld);
        }
    }
}
