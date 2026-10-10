using Assistant.Core.Data;

namespace Assistant.Core.Pathfinding
{
    public interface IPathfinder
    {
        PathResult FindPath(GridPosition start, GridPosition goal, int maxExpansions = 10000);
        PathResult FindPath(Vec3 startWorld, Vec3 goalWorld, int maxExpansions = 10000);
    }
}
