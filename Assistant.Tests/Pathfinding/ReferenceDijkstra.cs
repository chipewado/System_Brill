using System;
using System.Collections.Generic;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;
using Assistant.Core.Pathfinding;

namespace Assistant.Tests.Pathfinding
{
    public static class ReferenceDijkstra
    {
        private struct NeighborDir
        {
            public int Dx;
            public int Dz;
            public float StepFactor;
            public bool IsDiagonal;
        }

        private static readonly NeighborDir[] Directions = new[]
        {
            new NeighborDir { Dx = 0,  Dz = 1,  StepFactor = 1.0f,               IsDiagonal = false }, // N
            new NeighborDir { Dx = 1,  Dz = 1,  StepFactor = (float)Math.Sqrt(2), IsDiagonal = true },  // NE
            new NeighborDir { Dx = 1,  Dz = 0,  StepFactor = 1.0f,               IsDiagonal = false }, // E
            new NeighborDir { Dx = 1,  Dz = -1, StepFactor = (float)Math.Sqrt(2), IsDiagonal = true },  // SE
            new NeighborDir { Dx = 0,  Dz = -1, StepFactor = 1.0f,               IsDiagonal = false }, // S
            new NeighborDir { Dx = -1, Dz = -1, StepFactor = (float)Math.Sqrt(2), IsDiagonal = true },  // SW
            new NeighborDir { Dx = -1, Dz = 0,  StepFactor = 1.0f,               IsDiagonal = false }, // W
            new NeighborDir { Dx = -1, Dz = 1,  StepFactor = (float)Math.Sqrt(2), IsDiagonal = true }   // NW
        };

        public static (PathStatus Status, float Cost) FindShortestPath(INavGrid grid, GridPosition start, GridPosition goal)
        {
            if (!grid.IsInBounds(start) || !grid.IsWalkable(start))
                return (PathStatus.InvalidStart, float.PositiveInfinity);

            if (!grid.IsInBounds(goal) || !grid.IsWalkable(goal))
                return (PathStatus.InvalidGoal, float.PositiveInfinity);

            if (start.X == goal.X && start.Z == goal.Z)
                return (PathStatus.Success, 0f);

            var dist = new Dictionary<GridPosition, float>();
            var visited = new HashSet<GridPosition>();

            // Sorted set for priority queue: (cost, x, z)
            var pq = new SortedSet<(float Cost, int X, int Z, long Seq)>(
                Comparer<(float Cost, int X, int Z, long Seq)>.Create((a, b) =>
                {
                    int c = a.Cost.CompareTo(b.Cost);
                    if (c != 0) return c;
                    c = a.X.CompareTo(b.X);
                    if (c != 0) return c;
                    c = a.Z.CompareTo(b.Z);
                    if (c != 0) return c;
                    return a.Seq.CompareTo(b.Seq);
                }));

            long seq = 0;
            dist[start] = 0f;
            pq.Add((0f, start.X, start.Z, ++seq));

            while (pq.Count > 0)
            {
                var min = pq.Min;
                pq.Remove(min);

                var u = new GridPosition(min.X, min.Z);
                if (visited.Contains(u))
                    continue;

                visited.Add(u);

                if (u.X == goal.X && u.Z == goal.Z)
                    return (PathStatus.Success, min.Cost);

                float currentCost = dist[u];

                foreach (var dir in Directions)
                {
                    var v = new GridPosition(u.X + dir.Dx, u.Z + dir.Dz);
                    if (!grid.IsInBounds(v) || !grid.IsWalkable(v))
                        continue;

                    // Strict no-corner-cutting check
                    if (dir.IsDiagonal)
                    {
                        var card1 = new GridPosition(u.X + dir.Dx, u.Z);
                        var card2 = new GridPosition(u.X, u.Z + dir.Dz);
                        if (!grid.IsWalkable(card1) || !grid.IsWalkable(card2))
                            continue;
                    }

                    if (visited.Contains(v))
                        continue;

                    float stepCost = grid.CellSize * dir.StepFactor * grid.GetTraversalCost(v);
                    float newCost = currentCost + stepCost;

                    if (!dist.TryGetValue(v, out float oldCost) || newCost < oldCost)
                    {
                        dist[v] = newCost;
                        pq.Add((newCost, v.X, v.Z, ++seq));
                    }
                }
            }

            return (PathStatus.Unreachable, float.PositiveInfinity);
        }
    }
}
