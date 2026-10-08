using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface IRouteOptimizer
    {
        IReadOnlyList<TokenLocation> Plan(PlayerState p, IReadOnlyList<TokenLocation> candidates, int max);
    }
}
