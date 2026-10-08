using System;

namespace Assistant.Core.Data
{
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public int X { get; }
        public int Z { get; }

        public GridPosition(int x, int z)
        {
            X = x;
            Z = z;
        }

        public static GridPosition Zero => new GridPosition(0, 0);

        public bool Equals(GridPosition other) => X == other.X && Z == other.Z;

        public override bool Equals(object? obj) => obj is GridPosition other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z);

        public override string ToString() => $"({X}, {Z})";

        public static bool operator ==(GridPosition left, GridPosition right) => left.Equals(right);
        public static bool operator !=(GridPosition left, GridPosition right) => !left.Equals(right);

        public void Deconstruct(out int x, out int z)
        {
            x = X;
            z = Z;
        }
    }
}
