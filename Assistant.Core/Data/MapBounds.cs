namespace Assistant.Core.Data
{
    public class MapBounds
    {
        public Vec3 Min { get; set; }
        public Vec3 Max { get; set; }

        public MapBounds()
        {
            Min = Vec3.Zero;
            Max = Vec3.Zero;
        }

        public MapBounds(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public bool Contains(Vec3 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Y >= Min.Y && point.Y <= Max.Y &&
                   point.Z >= Min.Z && point.Z <= Max.Z;
        }

        public bool Contains2D(Vec3 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Z >= Min.Z && point.Z <= Max.Z;
        }

        public override string ToString() => $"Bounds[Min={Min}, Max={Max}]";
    }
}
