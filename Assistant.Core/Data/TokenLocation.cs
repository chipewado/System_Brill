using System;

namespace Assistant.Core.Data
{
    public class TokenLocation
    {
        public string Id { get; set; } = string.Empty;
        public Vec3 Position { get; set; }
        public TokenRarity Rarity { get; set; }
        public float EstimatedValue { get; set; }
        public bool IsCollected { get; set; }
        public DateTimeOffset DetectedAt { get; set; }
        public DateTimeOffset LastConfirmedAt { get; set; }

        public TokenLocation Clone()
        {
            return new TokenLocation
            {
                Id = Id,
                Position = Position,
                Rarity = Rarity,
                EstimatedValue = EstimatedValue,
                IsCollected = IsCollected,
                DetectedAt = DetectedAt,
                LastConfirmedAt = LastConfirmedAt
            };
        }

        public override string ToString() => $"Token[{Id}, {Rarity}, Pos={Position}, Val={EstimatedValue:F1}, Collected={IsCollected}]";
    }
}
