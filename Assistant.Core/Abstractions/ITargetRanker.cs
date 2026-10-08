using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface ITargetRanker
    {
        TokenLocation? ChooseBest(IReadOnlyList<TokenLocation> tokens, PlayerState player);
    }
}
