using System;

namespace Assistant.Core.Data
{
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => new Vec3(0, 0, 0);

        public static float Distance(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static float DistanceSquared(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public static float Distance2D(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public bool IsFinite() =>
            !float.IsNaN(X) && !float.IsInfinity(X) &&
            !float.IsNaN(Y) && !float.IsInfinity(Y) &&
            !float.IsNaN(Z) && !float.IsInfinity(Z);

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";

        public static bool operator ==(Vec3 left, Vec3 right) => left.Equals(right);
        public static bool operator !=(Vec3 left, Vec3 right) => !left.Equals(right);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float d) => new Vec3(a.X * d, a.Y * d, a.Z * d);
    }
}
