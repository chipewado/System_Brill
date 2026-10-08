using System;

namespace Assistant.Core.Data
{
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public float X { get; }
        public float Y { get; }

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => new Vec2(0, 0);

        public static float Distance(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"({X:F2}, {Y:F2})";

        public static bool operator ==(Vec2 left, Vec2 right) => left.Equals(right);
        public static bool operator !=(Vec2 left, Vec2 right) => !left.Equals(right);
    }
}
