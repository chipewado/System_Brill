using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Sim
{
    public class SimGameApi : IGameApi, IMapProvider
    {
        private readonly SimGridMap _map;
        private readonly SimPlayer _player;
        private readonly SimTokenSpawner _spawner;
        private readonly IClock _clock;
        private readonly Random _rng;
        private readonly Dictionary<string, TokenLocation> _activeTokens = new Dictionary<string, TokenLocation>();
        private readonly object _lock = new object();

        public event Action<TokenLocation>? TokenSpawned;
        public event Action<string>? TokenCollected;
        public event Action<string>? TokenRemoved;
        public event Action? MapChanged;

        public SimGridMap Map => _map;
        public SimPlayer Player => _player;
        public SimTokenSpawner Spawner => _spawner;
        public IClock Clock => _clock;

        public SimGameApi(SimGridMap? map = null, SimPlayer? player = null, int seed = 42, IClock? clock = null)
        {
            _clock = clock ?? SystemClock.Instance;
            _rng = new Random(seed);
            _map = map ?? new SimGridMap(100, 100, 10f);
            _player = player ?? new SimPlayer(new Vec3(50f, 0, 50f), 5f);
            _spawner = new SimTokenSpawner(_clock);
        }

        #region IGameApi Implementation

        public PlayerState GetPlayerState()
        {
            lock (_lock)
            {
                return _player.State.Clone();
            }
        }

        #endregion

        #region IMapProvider Implementation

        public MapData GetMap()
        {
            return _map.ToMapData();
        }

        public IEnumerable<RawMapObject> GetObjects(Region region)
        {
            var list = new List<RawMapObject>();
            lock (_lock)
            {
                foreach (var token in _activeTokens.Values)
                {
                    if (!token.IsCollected && region.Contains(token.Position))
                    {
                        var obj = new RawMapObject
                        {
                            Id = token.Id,
                            ObjectType = "CollectibleToken",
                            Position = token.Position,
                            Properties = new Dictionary<string, object>
                            {
                                { "Rarity", token.Rarity.ToString() },
                                { "EstimatedValue", token.EstimatedValue },
                                { "IsCollected", token.IsCollected }
                            }
                        };
                        list.Add(obj);
                    }
                }
            }
            return list;
        }

        #endregion

        #region Simulation Operations

        public void SpawnToken(TokenLocation token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));

            lock (_lock)
            {
                _activeTokens[token.Id] = token.Clone();
            }

            TokenSpawned?.Invoke(token);
        }

        public TokenLocation SpawnRandomToken(TokenRarity? forcedRarity = null)
        {
            TokenLocation token;
            lock (_lock)
            {
                token = _spawner.SpawnRandomToken(_map, _rng, forcedRarity);
                _activeTokens[token.Id] = token.Clone();
            }

            TokenSpawned?.Invoke(token);
            return token;
        }

        public bool CollectToken(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            bool found = false;
            lock (_lock)
            {
                if (_activeTokens.TryGetValue(id, out var token) && !token.IsCollected)
                {
                    token.IsCollected = true;
                    found = true;
                }
            }

            if (found)
            {
                TokenCollected?.Invoke(id);
                return true;
            }
            return false;
        }

        public bool RemoveToken(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            bool removed = false;
            lock (_lock)
            {
                removed = _activeTokens.Remove(id);
            }

            if (removed)
            {
                TokenRemoved?.Invoke(id);
                return true;
            }
            return false;
        }

        public void TriggerMapChanged()
        {
            MapChanged?.Invoke();
        }

        public IReadOnlyList<TokenLocation> GetActiveTokens()
        {
            lock (_lock)
            {
                return _activeTokens.Values.Select(t => t.Clone()).ToList();
            }
        }

        public void TeleportPlayer(Vec3 pos)
        {
            lock (_lock)
            {
                _player.Teleport(pos);
            }
        }

        #endregion
    }
}
