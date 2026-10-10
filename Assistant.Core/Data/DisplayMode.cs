using System;
using System.Collections.Generic;

namespace Assistant.Core.Data
{
    /// <summary>
    /// Configuration model for presentation layer filtering, HUD marker color, and tracers.
    /// </summary>
    public class DisplayMode
    {
        public string Id { get; }
        public string Name { get; set; } = string.Empty;
        public ColorRgba Color { get; set; } = ColorRgba.White;
        public string KeyBinding { get; set; } = string.Empty;
        public FilterMode Filter { get; set; } = FilterMode.Inclusive;
        public HashSet<TokenRarity> Rarities { get; set; } = new HashSet<TokenRarity>();
        public float MinValue { get; set; } = 0f;
        public float? MaxDistance { get; set; } = null; // null = unlimited (avoids Infinity in JSON)
        public bool ShowTracer { get; set; } = false;
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; } = 0;

        public DisplayMode(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("DisplayMode Id must be non-empty and injected.", nameof(id));
            }
            Id = id;
        }

        /// <summary>
        /// Evaluates deterministic matching criteria: Enabled AND RarityMatch AND ValueMatch AND DistanceMatch.
        /// </summary>
        public bool Matches(TokenLocation token, Vec3 playerPosition)
        {
            if (!Enabled) return false;
            if (token == null) return false;

            // Rarity filter
            if (Filter == FilterMode.Inclusive)
            {
                // Inclusive mode with an empty rarity set matches no tokens
                if (Rarities == null || Rarities.Count == 0) return false;
                if (!Rarities.Contains(token.Rarity)) return false;
            }
            else
            {
                // Exclusive mode with an empty rarity set matches all rarities
                if (Rarities != null && Rarities.Count > 0 && Rarities.Contains(token.Rarity)) return false;
            }

            // MinValue threshold
            if (float.IsNaN(MinValue) || float.IsInfinity(MinValue)) return false;
            if (token.EstimatedValue < MinValue) return false;

            // MaxDistance threshold (null = unlimited)
            if (MaxDistance.HasValue)
            {
                if (MaxDistance.Value < 0f || float.IsNaN(MaxDistance.Value) || float.IsInfinity(MaxDistance.Value))
                {
                    return false;
                }

                float dist = Vec3.Distance2D(playerPosition, token.Position);
                if (dist > MaxDistance.Value) return false;
            }

            return true;
        }

        /// <summary>
        /// Creates a deep copy of this DisplayMode, including cloning the Rarities set.
        /// </summary>
        public DisplayMode Clone()
        {
            return new DisplayMode(Id)
            {
                Name = Name,
                Color = Color,
                KeyBinding = KeyBinding,
                Filter = Filter,
                Rarities = Rarities != null ? new HashSet<TokenRarity>(Rarities) : new HashSet<TokenRarity>(),
                MinValue = MinValue,
                MaxDistance = MaxDistance,
                ShowTracer = ShowTracer,
                Enabled = Enabled,
                Priority = Priority
            };
        }
    }
}
