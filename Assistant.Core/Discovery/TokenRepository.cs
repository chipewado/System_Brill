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
                if (_tokens.ContainsKey(t.Id))
                {
                    throw new ArgumentException($"Token with ID '{t.Id}' already exists.", nameof(t));
                }

                var clone = t.Clone();
                _tokens[t.Id] = clone;
                if (!clone.IsCollected)
                {
                    _grid.Insert(clone.Id, clone.Position);
                }
            }
        }

        public void Update(TokenLocation t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (string.IsNullOrWhiteSpace(t.Id)) throw new ArgumentException("Token ID cannot be empty.", nameof(t));

            lock (_lock)
            {
                if (!_tokens.ContainsKey(t.Id))
                {
                    throw new KeyNotFoundException($"Token with ID '{t.Id}' does not exist.");
                }

                var clone = t.Clone();
                _tokens[t.Id] = clone;
                if (!clone.IsCollected)
                {
                    _grid.Update(clone.Id, clone.Position);
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
                int availableCount = 0;
                foreach (var t in _tokens.Values)
                {
                    if (!t.IsCollected) availableCount++;
                }

                if (availableCount == 0) return Array.Empty<TokenLocation>();
                int targetCount = Math.Min(count, availableCount);

                float searchRadius = _grid.CellSize;
                var candidateIds = new HashSet<string>();

                while (true)
                {
                    var ids = _grid.QueryCandidates(from, searchRadius);
                    candidateIds.UnionWith(ids);

                    var candidates = new List<(TokenLocation token, float distSq)>();
                    foreach (var id in candidateIds)
                    {
                        if (_tokens.TryGetValue(id, out var t) && !t.IsCollected)
                        {
                            float distSq = Vec3.DistanceSquared(from, t.Position);
                            candidates.Add((t, distSq));
                        }
                    }

                    if (candidates.Count >= targetCount)
                    {
                        candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));
                        float kthDist = (float)Math.Sqrt(candidates[targetCount - 1].distSq);
                        if (searchRadius >= kthDist || candidates.Count == availableCount)
                        {
                            var result = new List<TokenLocation>(targetCount);
                            for (int i = 0; i < targetCount; i++)
                            {
                                result.Add(candidates[i].token.Clone());
                            }
                            return result;
                        }

                        searchRadius = Math.Max(searchRadius * 2f, kthDist);
                    }
                    else
                    {
                        if (candidates.Count == availableCount)
                        {
                            candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));
                            var result = new List<TokenLocation>(candidates.Count);
                            for (int i = 0; i < candidates.Count; i++)
                            {
                                result.Add(candidates[i].token.Clone());
                            }
                            return result;
                        }

                        searchRadius *= 2f;
                    }
                }
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
