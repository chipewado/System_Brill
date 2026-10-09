using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    /// <summary>
    /// Pure parsing interface: converts RawMapObject into TokenLocation without owning time.
    /// </summary>
    public interface ITokenParser
    {
        bool TryParseToken(RawMapObject raw, out TokenLocation? token, out string? failureReason);
        IReadOnlyList<TokenLocation> ParseMany(IEnumerable<RawMapObject> rawObjects);
    }
}
