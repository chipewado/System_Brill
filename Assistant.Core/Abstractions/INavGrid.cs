using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    /// <summary>
    /// Abstraction for 2.5D planar navigation grid providing geometry, bounds, and traversal cost lookups.
    /// </summary>
    public interface INavGrid
    {
        int GridWidth { get; }
        int GridHeight { get; }
        float CellSize { get; }
        float MinTraversalCost { get; }

        bool IsInBounds(GridPosition gridPos);
        bool IsWalkable(GridPosition gridPos);
        float GetTraversalCost(GridPosition gridPos);

        GridPosition WorldToGrid(Vec3 worldPos);
        Vec3 GridToWorld(GridPosition gridPos);
    }
}
