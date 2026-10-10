using System;

namespace Assistant.Core.Ranking
{
    public sealed class RankingConfig
    {
        public float WeightValue { get; }
        public float WeightTime { get; }
        public float WeightRarity { get; }
        public float WeightHysteresis { get; }

        public float ValueScale { get; }
        public float MaxTravelSeconds { get; }
        public float DefaultSpeed { get; }
        public float CommitRadius { get; }
        public float CommitOverrideMargin { get; }
        public int MaxPathSearches { get; }

        public const double UpperBoundSlack = 1e-6;

        public RankingConfig(
            float weightValue = 0.40f,
            float weightTime = 0.35f,
            float weightRarity = 0.15f,
            float weightHysteresis = 0.10f,
            float valueScale = 250f,
            float maxTravelSeconds = 120f,
            float defaultSpeed = 5.0f,
            float commitRadius = 15.0f,
            float commitOverrideMargin = 0.30f,
            int maxPathSearches = 8)
        {
            if (!float.IsFinite(weightValue) || weightValue < 0f) throw new ArgumentOutOfRangeException(nameof(weightValue));
            if (!float.IsFinite(weightTime) || weightTime < 0f) throw new ArgumentOutOfRangeException(nameof(weightTime));
            if (!float.IsFinite(weightRarity) || weightRarity < 0f) throw new ArgumentOutOfRangeException(nameof(weightRarity));
            if (!float.IsFinite(weightHysteresis) || weightHysteresis < 0f) throw new ArgumentOutOfRangeException(nameof(weightHysteresis));
            if (weightValue + weightTime + weightRarity + weightHysteresis <= 0f)
                throw new ArgumentException("At least one ranking weight must be strictly positive");

            if (!float.IsFinite(valueScale) || valueScale <= 0f) throw new ArgumentOutOfRangeException(nameof(valueScale));
            if (!float.IsFinite(maxTravelSeconds) || maxTravelSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(maxTravelSeconds));
            if (!float.IsFinite(defaultSpeed) || defaultSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(defaultSpeed));
            if (!float.IsFinite(commitRadius) || commitRadius < 0f) throw new ArgumentOutOfRangeException(nameof(commitRadius));
            if (float.IsNaN(commitOverrideMargin) || commitOverrideMargin < 0f)
                throw new ArgumentOutOfRangeException(nameof(commitOverrideMargin), "CommitOverrideMargin must be non-negative and not NaN (+Infinity is permitted for unconditional retention).");
            if (maxPathSearches <= 0) throw new ArgumentOutOfRangeException(nameof(maxPathSearches));

            WeightValue = weightValue;
            WeightTime = weightTime;
            WeightRarity = weightRarity;
            WeightHysteresis = weightHysteresis;
            ValueScale = valueScale;
            MaxTravelSeconds = maxTravelSeconds;
            DefaultSpeed = defaultSpeed;
            CommitRadius = commitRadius;
            CommitOverrideMargin = commitOverrideMargin;
            MaxPathSearches = maxPathSearches;
        }
    }
}
