using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface IPathFinder
    {
        Path? FindPath(Vec3 start, Vec3 goal);
    }
}
