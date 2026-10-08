using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Sim
{
    public class SimTokenSpawner
    {
        private int _counter = 0;

        public TokenLocation CreatePresetToken(string id, Vec3 position, TokenRarity rarity, float estimatedValue)
        {
            return new TokenLocation
            {
                Id = id,
                Position = position,
                Rarity = rarity,
                EstimatedValue = estimatedValue,
                IsCollected = false,
                DetectedAt = DateTime.UtcNow,
                LastConfirmedAt = DateTime.UtcNow
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

            // Find a walkable position on the map
            Vec3 pos = Vec3.Zero;
            for (int attempts = 0; attempts < 50; attempts++)
            {
                float x = (float)(rng.NextDouble() * map.WorldBounds.Max.X);
                float z = (float)(rng.NextDouble() * map.WorldBounds.Max.Z);
                var candidate = new Vec3(x, 0, z);
                if (map.IsWalkable(candidate))
                {
                    pos = candidate;
                    break;
                }
            }

            return new TokenLocation
            {
                Id = id,
                Position = pos,
                Rarity = rarity,
                EstimatedValue = value,
                IsCollected = false,
                DetectedAt = DateTime.UtcNow,
                LastConfirmedAt = DateTime.UtcNow
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
