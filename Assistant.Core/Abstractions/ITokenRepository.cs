using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface ITokenRepository
    {
        void Add(TokenLocation t);
        void Update(TokenLocation t);
        bool Remove(string id);
        TokenLocation? Get(string id);
        IReadOnlyList<TokenLocation> GetAvailable();
        IReadOnlyList<TokenLocation> GetByRarity(TokenRarity r);
        IReadOnlyList<TokenLocation> GetNearest(Vec3 from, int count);
        void ClearCollected();
    }
}
