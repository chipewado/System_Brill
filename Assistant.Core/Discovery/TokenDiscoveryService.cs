using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Core.Discovery
{
    /// <summary>
    /// Discovery engine orchestrating pull-based spatial scans, coverage-based TTL eviction,
    /// bounded coverage history tracking, and region-aware invalidation.
    ///
    /// LOCK HIERARCHY & THREADING MODEL:
    /// 1. Service Lock (_syncLock): Acquired first by TokenDiscoveryService methods.
    /// 2. Repository Lock (TokenRepository._lock): Acquired second when TokenRepository operations are invoked.
    /// Lock order is strictly: TokenDiscoveryService._syncLock -> TokenRepository._lock.
    /// Deadlock prevention guarantees:
    /// - TokenRepository never holds or requests TokenDiscoveryService locks.
    /// - TokenRepository contains no external callbacks or delegate invocations.
    /// - All TokenDiscoveryService events (TokensDiscovered, TokensExpired) are strictly raised
    ///   OUTSIDE the _syncLock boundary.
    /// - Any subscriber callback exception is caught and suppressed to prevent re-entrancy issues
    ///   and preserve engine stability.
    ///
    /// EVENT CONTRACT:
    /// Only TokensDiscovered and TokensExpired events are raised by this service as specified in the contract.
    /// Removal events (e.g. manual removal or collection) are not raised; consumers re-read repository snapshots.
    /// </summary>
    public class TokenDiscoveryService : ITokenDiscoveryService, IDisposable
    {
        public const float PositionChangeEpsilon = 0.001f;
        public const int MaxCoverageHistoryCapacity = 1000;

        private readonly object _syncLock = new object();
        private readonly IMapProvider _mapProvider;
        private readonly ITokenRepository _repository;
        private readonly ITokenParser _parser;
        private readonly TokenValidator _validator;
        private readonly IClock _clock;
        private readonly IGameApi? _gameApi;

        private readonly List<CoverageRecord> _coverageHistory = new List<CoverageRecord>();

        public IClock Clock => _clock;
        public float TtlSeconds { get; set; }
        public TimeSpan ScanInterval { get; set; }
        public float ScanRadius { get; set; }

        internal int CoverageHistoryCount
        {
            get
            {
                lock (_syncLock)
                {
                    return _coverageHistory.Count;
                }
            }
        }

        public event Action<IReadOnlyList<TokenLocation>>? TokensDiscovered;
        public event Action<IReadOnlyList<string>>? TokensExpired;

        private DateTimeOffset? _lastTickScanTime;

        public TokenDiscoveryService(
            IMapProvider mapProvider,
            ITokenRepository repository,
            ITokenParser parser,
            TokenValidator validator,
            IClock clock,
            IGameApi? gameApi = null,
            float ttlSeconds = 30f,
            float scanRadius = 100f,
            TimeSpan? scanInterval = null)
        {
            _mapProvider = mapProvider ?? throw new ArgumentNullException(nameof(mapProvider));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _gameApi = gameApi;

            TtlSeconds = ttlSeconds;
            ScanRadius = scanRadius;
            ScanInterval = scanInterval ?? TimeSpan.FromSeconds(1.0);

            if (_gameApi != null)
            {
                _gameApi.TokenCollected += OnTokenCollected;
                _gameApi.TokenRemoved += OnTokenRemoved;
                _gameApi.MapChanged += OnMapChanged;
            }
        }

        public int ScanRegion(Region region)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));

            DateTimeOffset now = _clock.UtcNow;
            var discoveredToNotify = new List<TokenLocation>();
            int processedCount = 0;

            // Fetch raw objects before recording coverage. If IMapProvider throws,
            // no coverage entry is added and no false eviction can occur.
            var rawObjects = _mapProvider.GetObjects(region);
            var mapData = _mapProvider.GetMap();

            lock (_syncLock)
            {
                // Record scan coverage history only after successful fetch
                AddCoverageRecordInternal(new Region(region.Center, region.Radius), now);

                // Track seen IDs within this scan batch for deterministic first-occurrence-wins
                var seenIdsInBatch = new HashSet<string>(StringComparer.Ordinal);

                foreach (var raw in rawObjects)
                {
                    if (raw == null) continue;

                    // Skip duplicate ID in same batch (first valid occurrence wins)
                    if (seenIdsInBatch.Contains(raw.Id))
                    {
                        continue;
                    }

                    if (!_parser.TryParseToken(raw, out var parsedToken, out _) || parsedToken == null)
                    {
                        continue;
                    }

                    if (!seenIdsInBatch.Add(parsedToken.Id))
                    {
                        continue;
                    }

                    // Stamp detection / confirmation timestamps
                    parsedToken.DetectedAt = now;
                    parsedToken.LastConfirmedAt = now;

                    // Validate token
                    var validation = _validator.Validate(parsedToken, mapData, now, TtlSeconds);
                    if (!validation.IsValid)
                    {
                        continue;
                    }

                    // Check existing repository token state
                    var existing = _repository.Get(parsedToken.Id);
                    if (existing == null)
                    {
                        // Brand new token (or token reappearing after eviction: DetectedAt = now)
                        _repository.Add(parsedToken);
                        discoveredToNotify.Add(parsedToken.Clone());
                        processedCount++;
                    }
                    else
                    {
                        // Collected tokens must never be resurrected by a subsequent scan
                        if (existing.IsCollected)
                        {
                            continue;
                        }

                        // Check if mutated: Position (beyond epsilon), Rarity, or EstimatedValue
                        bool posChanged = Vec3.Distance(existing.Position, parsedToken.Position) > PositionChangeEpsilon;
                        bool rarityChanged = existing.Rarity != parsedToken.Rarity;
                        bool valChanged = Math.Abs(existing.EstimatedValue - parsedToken.EstimatedValue) > 0.0001f;

                        if (posChanged || rarityChanged || valChanged)
                        {
                            // Mutated token: retain original DetectedAt
                            parsedToken.DetectedAt = existing.DetectedAt;
                            _repository.Update(parsedToken);
                            discoveredToNotify.Add(parsedToken.Clone());
                            processedCount++;
                        }
                        else
                        {
                            // Unchanged token: refresh LastConfirmedAt only (no event fired)
                            existing.LastConfirmedAt = now;
                            _repository.Update(existing);
                            processedCount++;
                        }
                    }
                }

                // Prune bounded coverage history
                PruneCoverageHistoryInternal();
            }

            // Raise discovery event outside lock
            if (discoveredToNotify.Count > 0)
            {
                var sorted = discoveredToNotify.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
                SafeInvokeDiscovered(sorted);
            }

            return processedCount;
        }

        public int ScanAll()
        {
            var mapData = _mapProvider.GetMap();
            Region fullRegion;

            if (mapData?.Bounds != null)
            {
                var bounds = mapData.Bounds;
                var center = new Vec3((bounds.Min.X + bounds.Max.X) * 0.5f, 0, (bounds.Min.Z + bounds.Max.Z) * 0.5f);
                float radius = Vec3.Distance2D(center, new Vec3(bounds.Max.X, 0, bounds.Max.Z)) + 10f;
                fullRegion = new Region(center, radius);
            }
            else
            {
                fullRegion = new Region(Vec3.Zero, float.MaxValue);
            }

            return ScanRegion(fullRegion);
        }

        public void Tick()
        {
            DateTimeOffset now = _clock.UtcNow;
            if (_gameApi != null)
            {
                if (_lastTickScanTime == null || (now - _lastTickScanTime.Value) >= ScanInterval)
                {
                    var playerState = _gameApi.GetPlayerState();
                    var scanRegion = new Region(playerState.Position, ScanRadius);
                    ScanRegion(scanRegion);
                    _lastTickScanTime = now;
                }
            }

            PruneStaleTokens();
        }

        public int PruneStaleTokens()
        {
            DateTimeOffset now = _clock.UtcNow;
            var expiredIds = new List<string>();

            lock (_syncLock)
            {
                var availableTokens = _repository.GetAvailable();

                foreach (var token in availableTokens)
                {
                    double elapsedSeconds = (now - token.LastConfirmedAt).TotalSeconds;

                    // Rule: Expire exactly at TTL threshold: elapsed >= TtlSeconds
                    if (elapsedSeconds >= TtlSeconds)
                    {
                        // Check qualifying coverage: must have been scanned AFTER LastConfirmedAt
                        bool hasQualifyingCoverage = false;
                        for (int i = 0; i < _coverageHistory.Count; i++)
                        {
                            var record = _coverageHistory[i];
                            if (record.ScannedAt > token.LastConfirmedAt && record.Region.Contains(token.Position))
                            {
                                hasQualifyingCoverage = true;
                                break;
                            }
                        }

                        if (hasQualifyingCoverage)
                        {
                            _repository.Remove(token.Id);
                            expiredIds.Add(token.Id);
                        }
                    }
                }

                PruneCoverageHistoryInternal();
            }

            if (expiredIds.Count > 0)
            {
                var sorted = expiredIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
                SafeInvokeExpired(sorted);
            }

            return expiredIds.Count;
        }

        public void HandleMapChanged(MapChangedEventArgs args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            lock (_syncLock)
            {
                if (args.AffectedRegion == null)
                {
                    // Entire map invalidated
                    _repository.Clear();
                    _coverageHistory.Clear();
                }
                else
                {
                    // Sub-region invalidated
                    var affected = args.AffectedRegion;

                    // 1. Purge tokens inside invalidated region
                    var tokensInRegion = _repository.GetInRegion(affected);
                    foreach (var token in tokensInRegion)
                    {
                        _repository.Remove(token.Id);
                    }

                    // 2. Drop coverage entries intersecting the invalidated region
                    _coverageHistory.RemoveAll(record => record.Region.Intersects(affected));
                }
            }
        }

        private void OnTokenCollected(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            lock (_syncLock)
            {
                var token = _repository.Get(id);
                if (token != null)
                {
                    token.IsCollected = true;
                    _repository.Update(token);
                }
            }
        }

        private void OnTokenRemoved(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            lock (_syncLock)
            {
                _repository.Remove(id);
            }
        }

        private void OnMapChanged(MapChangedEventArgs args)
        {
            HandleMapChanged(args);
        }

        private void AddCoverageRecordInternal(Region region, DateTimeOffset scannedAt)
        {
            // Coalesce entries with identical center+radius (keep the newer)
            for (int i = _coverageHistory.Count - 1; i >= 0; i--)
            {
                var existing = _coverageHistory[i];
                if (existing.Region.Center == region.Center && Math.Abs(existing.Region.Radius - region.Radius) < 0.0001f)
                {
                    _coverageHistory.RemoveAt(i);
                }
            }

            _coverageHistory.Add(new CoverageRecord(new Region(region.Center, region.Radius), scannedAt));

            // When still at MaxCoverageHistoryCapacity, drop the oldest.
            // Consequence: Dropping older coverage records means unconfirmed tokens whose LastConfirmedAt
            // precedes the dropped scan will not be pruned by this historical coverage window until a new
            // qualifying scan covers them.
            if (_coverageHistory.Count > MaxCoverageHistoryCapacity)
            {
                _coverageHistory.RemoveRange(0, _coverageHistory.Count - MaxCoverageHistoryCapacity);
            }
        }

        private void PruneCoverageHistoryInternal()
        {
            var available = _repository.GetAvailable();
            if (available.Count > 0)
            {
                DateTimeOffset oldestConfirmed = available.Min(t => t.LastConfirmedAt);
                _coverageHistory.RemoveAll(c => c.ScannedAt < oldestConfirmed);
            }
            else
            {
                DateTimeOffset threshold = _clock.UtcNow - TimeSpan.FromSeconds(TtlSeconds);
                _coverageHistory.RemoveAll(c => c.ScannedAt < threshold);
            }

            if (_coverageHistory.Count > MaxCoverageHistoryCapacity)
            {
                _coverageHistory.RemoveRange(0, _coverageHistory.Count - MaxCoverageHistoryCapacity);
            }
        }

        private void SafeInvokeDiscovered(IReadOnlyList<TokenLocation> tokens)
        {
            var handler = TokensDiscovered;
            if (handler == null) return;

            foreach (var sub in handler.GetInvocationList())
            {
                try
                {
                    ((Action<IReadOnlyList<TokenLocation>>)sub)(tokens);
                }
                catch
                {
                    // Isolated subscriber exception: preserve discovery pipeline execution
                }
            }
        }

        private void SafeInvokeExpired(IReadOnlyList<string> tokenIds)
        {
            var handler = TokensExpired;
            if (handler == null) return;

            foreach (var sub in handler.GetInvocationList())
            {
                try
                {
                    ((Action<IReadOnlyList<string>>)sub)(tokenIds);
                }
                catch
                {
                    // Isolated subscriber exception: preserve discovery pipeline execution
                }
            }
        }

        public void Dispose()
        {
            if (_gameApi != null)
            {
                _gameApi.TokenCollected -= OnTokenCollected;
                _gameApi.TokenRemoved -= OnTokenRemoved;
                _gameApi.MapChanged -= OnMapChanged;
            }
        }

        private sealed class CoverageRecord
        {
            public Region Region { get; }
            public DateTimeOffset ScannedAt { get; }

            public CoverageRecord(Region region, DateTimeOffset scannedAt)
            {
                Region = region;
                ScannedAt = scannedAt;
            }
        }
    }
}
