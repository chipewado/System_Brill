using System;
using System.Collections.Generic;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Sim
{
    public class SimTokenSpawner
    {
        private readonly IClock _clock;
        private int _counter = 0;

        public SimTokenSpawner(IClock? clock = null)
        {
            _clock = clock ?? SystemClock.Instance;
        }

        public TokenLocation CreatePresetToken(string id, Vec3 position, TokenRarity rarity, float estimatedValue)
        {
            var now = _clock.UtcNow;
            return new TokenLocation
            {
                Id = id,
                Position = position,
                Rarity = rarity,
                EstimatedValue = estimatedValue,
                IsCollected = false,
                DetectedAt = now,
                LastConfirmedAt = now
            };
        }

        public List<TokenLocation> CreateSpecPhase1Presets()
        {
            return new List<TokenLocation>
            {
                CreatePresetToken("token_alpha", new Vec3(100f, 0, 250f), TokenRarity.Common, 10f),
                CreatePresetToken("token_bravo", new Vec3(420f, 0, 180f), TokenRarity.Rare, 65f),
                CreatePresetToken("token_charlie", new Vec3(700f, 0, 500f), TokenRarity.Legendary, 220f)
            };
        }

        public TokenLocation SpawnRandomToken(SimGridMap map, Random rng, TokenRarity? forcedRarity = null)
        {
            int idNum = ++_counter;
            string id = $"token_{idNum:D4}";

            TokenRarity rarity = forcedRarity ?? RollRarity(rng);
            float value = RollValue(rarity, rng);

            // Find a walkable, non-obstacle position on the map
            Vec3 pos = Vec3.Zero;
            bool foundWalkable = false;
            float minX = map.WorldBounds.Min.X;
            float maxX = map.WorldBounds.Max.X;
            float minZ = map.WorldBounds.Min.Z;
            float maxZ = map.WorldBounds.Max.Z;

            for (int attempts = 0; attempts < 100; attempts++)
            {
                float x = (float)(minX + rng.NextDouble() * (maxX - minX));
                float z = (float)(minZ + rng.NextDouble() * (maxZ - minZ));
                var candidate = new Vec3(x, 0, z);
                if (map.IsWalkable(candidate))
                {
                    pos = candidate;
                    foundWalkable = true;
                    break;
                }
            }

            if (!foundWalkable)
            {
                // Deterministic fallback scan: pick the first walkable, non-obstacle cell
                for (int gx = 0; gx < map.GridWidth && !foundWalkable; gx++)
                {
                    for (int gz = 0; gz < map.GridHeight && !foundWalkable; gz++)
                    {
                        if (map.GetCell(gx, gz).IsWalkable())
                        {
                            pos = map.GridToWorld(gx, gz);
                            foundWalkable = true;
                        }
                    }
                }
            }

            var now = _clock.UtcNow;
            return new TokenLocation
            {
                Id = id,
                Position = pos,
                Rarity = rarity,
                EstimatedValue = value,
                IsCollected = false,
                DetectedAt = now,
                LastConfirmedAt = now
            };
        }

        public List<TokenLocation> SpawnBatch(int count, SimGridMap map, Random rng)
        {
            var list = new List<TokenLocation>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(SpawnRandomToken(map, rng));
            }
            return list;
        }

        private static TokenRarity RollRarity(Random rng)
        {
            double roll = rng.NextDouble();
            if (roll < 0.50) return TokenRarity.Common;       // 50%
            if (roll < 0.75) return TokenRarity.Uncommon;     // 25%
            if (roll < 0.90) return TokenRarity.Rare;         // 15%
            if (roll < 0.97) return TokenRarity.Epic;         // 7%
            return TokenRarity.Legendary;                     // 3%
        }

        private static float RollValue(TokenRarity rarity, Random rng) => rarity switch
        {
            TokenRarity.Common => (float)(rng.NextDouble() * 10 + 5),         // 5 - 15
            TokenRarity.Uncommon => (float)(rng.NextDouble() * 20 + 16),     // 16 - 36
            TokenRarity.Rare => (float)(rng.NextDouble() * 40 + 36),         // 36 - 76
            TokenRarity.Epic => (float)(rng.NextDouble() * 75 + 76),         // 76 - 151
            TokenRarity.Legendary => (float)(rng.NextDouble() * 150 + 151),  // 151 - 301
            _ => 10f
        };
    }
}
