using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Pathfinding
{
    public sealed class PathResult
    {
        public PathStatus Status { get; }
        public IReadOnlyList<GridPosition> Steps { get; }
        public IReadOnlyList<Vec3> Waypoints { get; }
        public float GridCost { get; }
        public int NodesExpanded { get; }

        private PathResult(
            PathStatus status,
            IReadOnlyList<GridPosition> steps,
            IReadOnlyList<Vec3> waypoints,
            float gridCost,
            int nodesExpanded)
        {
            Status = status;
            Steps = steps;
            Waypoints = waypoints;
            GridCost = gridCost;
            NodesExpanded = nodesExpanded;
        }

        public static PathResult Successful(
            GridPosition[] steps,
            Vec3[] waypoints,
            float gridCost,
            int nodesExpanded)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));

            // Defensive copies to guarantee immutability against caller mutation
            var defensiveSteps = (GridPosition[])steps.Clone();
            var defensiveWaypoints = (Vec3[])waypoints.Clone();

            return new PathResult(
                PathStatus.Success,
                Array.AsReadOnly(defensiveSteps),
                Array.AsReadOnly(defensiveWaypoints),
                gridCost,
                nodesExpanded);
        }

        public static PathResult Failure(PathStatus status, int nodesExpanded)
        {
            if (status == PathStatus.Success)
                throw new ArgumentException("Cannot create failure with Success status", nameof(status));

            return new PathResult(
                status,
                Array.Empty<GridPosition>(),
                Array.Empty<Vec3>(),
                float.PositiveInfinity,
                nodesExpanded);
        }
    }
}
