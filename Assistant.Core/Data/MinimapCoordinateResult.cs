using System;

namespace Assistant.Core.Data
{
    /// <summary>
    /// Immutable transformation output representing a world position mapped into minimap coordinate space.
    /// Allocated entirely on the stack (readonly struct).
    /// </summary>
    public readonly struct MinimapCoordinateResult : IEquatable<MinimapCoordinateResult>
    {
        /// <summary>
        /// Pixel offset from the center of the minimap UI widget (+X = Right, +Y = Up).
        /// If off-screen, this point is clamped along the directional ray onto the padded boundary.
        /// </summary>
        public Vec2 ScreenPositionPx { get; }

        /// <summary>
        /// True if the unconstrained world position fell outside the padded viewport rectangle.
        /// </summary>
        public bool IsOffScreen { get; }

        /// <summary>
        /// Direction bearing in degrees normalized to [0, 360) clockwise from screen-up.
        /// Preserved identically before and after ray clamping.
        /// </summary>
        public float DirectionAngleDegrees { get; }

        /// <summary>
        /// Unscaled 2D Euclidean distance in world units (X-Z plane) between viewport center and target.
        /// </summary>
        public float DistanceWorld { get; }

        public MinimapCoordinateResult(Vec2 screenPos, bool isOffScreen, float angleDegrees, float distanceWorld)
        {
            ScreenPositionPx = screenPos;
            IsOffScreen = isOffScreen;
            DirectionAngleDegrees = angleDegrees;
            DistanceWorld = distanceWorld;
        }

        public bool Equals(MinimapCoordinateResult other) =>
            ScreenPositionPx.Equals(other.ScreenPositionPx) &&
            IsOffScreen == other.IsOffScreen &&
            DirectionAngleDegrees.Equals(other.DirectionAngleDegrees) &&
            DistanceWorld.Equals(other.DistanceWorld);

        public override bool Equals(object? obj) => obj is MinimapCoordinateResult other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(ScreenPositionPx, IsOffScreen, DirectionAngleDegrees, DistanceWorld);

        public static bool operator ==(MinimapCoordinateResult left, MinimapCoordinateResult right) => left.Equals(right);
        public static bool operator !=(MinimapCoordinateResult left, MinimapCoordinateResult right) => !left.Equals(right);

        public override string ToString() =>
            $"MinimapCoord[Screen={ScreenPositionPx}, OffScreen={IsOffScreen}, Angle={DirectionAngleDegrees:F1}°, Dist={DistanceWorld:F1}]";
    }
}
