using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Core.Discovery
{
    public class TokenRepository : ITokenRepository
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, TokenLocation> _tokens = new Dictionary<string, TokenLocation>();
        private readonly SpatialHashGrid _grid;

        public TokenRepository(float spatialCellSize = 50f)
        {
            _grid = new SpatialHashGrid(spatialCellSize);
        }

        public int TotalCount
        {
            get
            {
                lock (_lock)
                {
                    return _tokens.Count;
                }
            }
        }

        public void Add(TokenLocation t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (string.IsNullOrWhiteSpace(t.Id)) throw new ArgumentException("Token ID cannot be empty.", nameof(t));

            lock (_lock)
            {
                var clone = t.Clone();
                _tokens[t.Id] = clone;
                if (!clone.IsCollected)
                {
                    _grid.Insert(clone.Id, clone.Position);
                }
                else
                {
                    _grid.Remove(clone.Id);
                }
            }
        }

        public void Update(TokenLocation t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (string.IsNullOrWhiteSpace(t.Id)) throw new ArgumentException("Token ID cannot be empty.", nameof(t));

            lock (_lock)
            {
                var clone = t.Clone();
                _tokens[t.Id] = clone;
                if (!clone.IsCollected)
                {
                    _grid.Insert(clone.Id, clone.Position);
                }
                else
                {
                    _grid.Remove(clone.Id);
                }
            }
        }

        public bool Remove(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            lock (_lock)
            {
                _grid.Remove(id);
                return _tokens.Remove(id);
            }
        }

        public TokenLocation? Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            lock (_lock)
            {
                if (_tokens.TryGetValue(id, out var token))
                {
                    return token.Clone();
                }
                return null;
            }
        }

        public IReadOnlyList<TokenLocation> GetAvailable()
        {
            lock (_lock)
            {
                var list = new List<TokenLocation>(_tokens.Count);
                foreach (var t in _tokens.Values)
                {
                    if (!t.IsCollected)
                    {
                        list.Add(t.Clone());
                    }
                }
                return list;
            }
        }

        public IReadOnlyList<TokenLocation> GetByRarity(TokenRarity r)
        {
            lock (_lock)
            {
                var list = new List<TokenLocation>();
                foreach (var t in _tokens.Values)
                {
                    if (!t.IsCollected && t.Rarity == r)
                    {
                        list.Add(t.Clone());
                    }
                }
                return list;
            }
        }

        public IReadOnlyList<TokenLocation> GetNearest(Vec3 from, int count)
        {
            if (count <= 0) return Array.Empty<TokenLocation>();

            lock (_lock)
            {
                // When token count is small, linear scan is fast and exact
                if (_tokens.Count <= 200)
                {
                    return _tokens.Values
                        .Where(t => !t.IsCollected)
                        .OrderBy(t => Vec3.DistanceSquared(from, t.Position))
                        .Take(count)
                        .Select(t => t.Clone())
                        .ToList();
                }

                // For larger datasets, expand radius queries via SpatialHashGrid
                float searchRadius = _grid.CellSize;
                const float maxRadius = 5000f;
                HashSet<string> candidateIds = new HashSet<string>();

                while (searchRadius <= maxRadius)
                {
                    var ids = _grid.QueryRadius(from, searchRadius);
                    foreach (var id in ids)
                    {
                        candidateIds.Add(id);
                    }

                    if (candidateIds.Count >= count * 2 || searchRadius >= maxRadius)
                    {
                        break;
                    }
                    searchRadius *= 2f;
                }

                // If grid query found enough candidates, rank them
                if (candidateIds.Count > 0)
                {
                    var result = new List<TokenLocation>();
                    foreach (var id in candidateIds)
                    {
                        if (_tokens.TryGetValue(id, out var t) && !t.IsCollected)
                        {
                            result.Add(t);
                        }
                    }

                    return result
                        .OrderBy(t => Vec3.DistanceSquared(from, t.Position))
                        .Take(count)
                        .Select(t => t.Clone())
                        .ToList();
                }

                // Fallback to all available tokens
                return _tokens.Values
                    .Where(t => !t.IsCollected)
                    .OrderBy(t => Vec3.DistanceSquared(from, t.Position))
                    .Take(count)
                    .Select(t => t.Clone())
                    .ToList();
            }
        }

        public void ClearCollected()
        {
            lock (_lock)
            {
                var collectedIds = _tokens.Values
                    .Where(t => t.IsCollected)
                    .Select(t => t.Id)
                    .ToList();

                foreach (var id in collectedIds)
                {
                    _tokens.Remove(id);
                    _grid.Remove(id);
                }
            }
        }
    }
}
