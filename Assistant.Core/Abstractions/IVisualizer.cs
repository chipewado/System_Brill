using System.Collections.Generic;
using Assistant.Core.Data;
using Assistant.Core.Pathfinding;

namespace Assistant.Core.Abstractions
{
    public interface IVisualizer
    {
        void HighlightToken(TokenLocation t, TargetPriority priority);
        void ShowRoute(PathResult path);
        void UpdateMinimap(IReadOnlyList<TokenLocation> tokens, PlayerState player);
    }
}
