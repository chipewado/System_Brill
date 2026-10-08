using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface IVisualizer
    {
        void HighlightToken(TokenLocation t, TargetPriority priority);
        void ShowRoute(Path path);
        void UpdateMinimap(IReadOnlyList<TokenLocation> tokens, PlayerState player);
    }
}
