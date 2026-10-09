namespace Assistant.Core.Data
{
    public class Region
    {
        public Vec3 Center { get; set; }
        public float Radius { get; set; }

        public Region() { }

        public Region(Vec3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public bool Contains(Vec3 point)
        {
            return Vec3.Distance2D(Center, point) <= Radius;
        }

        public bool Intersects(Region other)
        {
            if (other == null) return false;
            return Vec3.Distance2D(Center, other.Center) <= (Radius + other.Radius);
        }

        public override string ToString() => $"Region[Center={Center}, Radius={Radius:F1}]";
    }
}
