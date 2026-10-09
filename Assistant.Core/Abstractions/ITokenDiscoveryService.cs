using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface ITokenDiscoveryService
    {
        IClock Clock { get; }
        float TtlSeconds { get; set; }
        TimeSpan ScanInterval { get; set; }
        float ScanRadius { get; set; }

        int ScanRegion(Region region);
        int ScanAll();
        void Tick();
        int PruneStaleTokens();
        void HandleMapChanged(MapChangedEventArgs args);

        /// <summary>
        /// Fires when NEW tokens are discovered or when existing tokens have changed properties (Position, Rarity, Value).
        /// Does NOT fire on timestamp-only confirmations.
        /// </summary>
        event Action<IReadOnlyList<TokenLocation>>? TokensDiscovered;

        /// <summary>
        /// Fires when stale tokens are evicted by coverage-based TTL pruning.
        /// </summary>
        event Action<IReadOnlyList<string>>? TokensExpired;
    }
}
