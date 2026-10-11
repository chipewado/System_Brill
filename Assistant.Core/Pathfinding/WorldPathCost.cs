using System;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Core.Pathfinding
{
    /// <summary>
    /// Pure, static helper for computing the composite world path cost including discrete A* grid cost
    /// and continuous endpoint connectors between player/token positions and cell centers.
    /// Invariant: For any walkable path, WorldPathCost >= MinTraversalCost * Distance2D(player, token) - tol,
    /// where tol = 1e-5 * max(1.0, lineLowerBound) + 1e-6 (relative tolerance covering float accumulation noise).
    /// </summary>
    public static class WorldPathCost
    {
        /// <summary>
        /// Computes the composite world path cost in world units.
        /// If the path is not successful, returns <see cref="float.PositiveInfinity"/>.
        /// Clamps results within relative tolerance below the lower bound to the lower bound (float noise),
        /// and throws <see cref="InvalidOperationException"/> for larger violations (geometry bug).
        /// Note: PathResult.GridCost is maintained as float, and relative tolerance covers float accumulation noise.
        /// </summary>
        public static float Compute(
            INavGrid grid,
            PathResult path,
            Vec3 playerPosition,
            GridPosition startCell,
            Vec3 tokenPosition)
        {
            return (float)ComputeDouble(grid, path, playerPosition, startCell, tokenPosition);
        }

        /// <summary>
        /// Computes the composite world path cost in double precision.
        /// If the path is not successful, returns <see cref="double.PositiveInfinity"/>.
        /// </summary>
        public static double ComputeDouble(
            INavGrid grid,
            PathResult path,
            Vec3 playerPosition,
            GridPosition startCell,
            Vec3 tokenPosition)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (path == null) throw new ArgumentNullException(nameof(path));

            if (path.Status != PathStatus.Success)
            {
                return double.PositiveInfinity;
            }

            Vec3 startCenter = grid.GridToWorld(startCell);
            GridPosition goalCell = grid.WorldToGrid(tokenPosition);
            Vec3 goalCenter = grid.GridToWorld(goalCell);

            double dStart = Vec3.Distance2DDouble(playerPosition, startCenter);
            double dGoal = Vec3.Distance2DDouble(goalCenter, tokenPosition);

            double cost = (double)path.GridCost + (double)grid.MinTraversalCost * (dStart + dGoal);

            double dist2D = Vec3.Distance2DDouble(playerPosition, tokenPosition);
            double lineLowerBound = (double)grid.MinTraversalCost * dist2D;
            double tol = 1e-5 * Math.Max(1.0, lineLowerBound) + 1e-6;

            if (cost < lineLowerBound - tol)
            {
                throw new InvalidOperationException(
                    $"World path cost ({cost}) violates geometric lower bound ({lineLowerBound}) beyond relative tolerance ({tol}).");
            }

            if (cost < lineLowerBound)
            {
                cost = lineLowerBound;
            }

            return cost;
        }
    }
}
