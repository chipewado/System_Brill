using System;
using System.Collections.Generic;
using System.Linq;

namespace Assistant.Core.Data
{
    public class Path
    {
        public IReadOnlyList<Vec3> Waypoints { get; set; } = Array.Empty<Vec3>();
        public float TotalDistance { get; set; }

        public Path() { }

        public Path(IEnumerable<Vec3> waypoints)
        {
            var list = waypoints.ToList();
            Waypoints = list;
            TotalDistance = CalculateDistance(list);
        }

        private static float CalculateDistance(IReadOnlyList<Vec3> points)
        {
            if (points == null || points.Count < 2) return 0f;
            float sum = 0f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                sum += Vec3.Distance(points[i], points[i + 1]);
            }
            return sum;
        }

        public override string ToString() => $"Path[Points={Waypoints.Count}, Dist={TotalDistance:F1}]";
    }
}
