using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface ITokenDetector
    {
        List<TokenLocation> FindTokens(MapData map);
    }
}
