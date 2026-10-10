using System;
using System.Globalization;

namespace Assistant.Core.Data
{
    /// <summary>
    /// Pure RGBA color representation (32-bit byte channel) with no external or Unity dependencies.
    /// </summary>
    public readonly struct ColorRgba : IEquatable<ColorRgba>
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public byte A { get; }

        public ColorRgba(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static ColorRgba FromHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return White;
            hex = hex.Trim().TrimStart('#');

            if (hex.Length == 6)
            {
                if (byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                    byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                    byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                {
                    return new ColorRgba(r, g, b, 255);
                }
            }
            else if (hex.Length == 8)
            {
                if (byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                    byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                    byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b) &&
                    byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a))
                {
                    return new ColorRgba(r, g, b, a);
                }
            }

            return White;
        }

        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";

        public static readonly ColorRgba White = new ColorRgba(255, 255, 255, 255);
        public static readonly ColorRgba Yellow = new ColorRgba(255, 215, 0, 255);
        public static readonly ColorRgba Purple = new ColorRgba(160, 32, 240, 255);
        public static readonly ColorRgba Orange = new ColorRgba(255, 140, 0, 255);
        public static readonly ColorRgba Cyan = new ColorRgba(0, 255, 255, 255);
        public static readonly ColorRgba Red = new ColorRgba(255, 0, 0, 255);

        public bool Equals(ColorRgba other) => R == other.R && G == other.G && B == other.B && A == other.A;

        public override bool Equals(object? obj) => obj is ColorRgba other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(R, G, B, A);

        public override string ToString() => ToHex();

        public static bool operator ==(ColorRgba left, ColorRgba right) => left.Equals(right);

        public static bool operator !=(ColorRgba left, ColorRgba right) => !left.Equals(right);
    }
}
