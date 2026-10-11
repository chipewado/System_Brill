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

        public const double UpperBoundSlack = 1e-6;

        public RankingConfig(
            float weightValue = 0.40f,
            float weightTime = 0.35f,
            float weightRarity = 0.15f,
            float weightHysteresis = 0.10f,
            float valueScale = 250f,
            float maxTravelSeconds = 120f,
            float defaultSpeed = 5.0f)
        {
            if (!float.IsFinite(weightValue) || weightValue < 0f) throw new ArgumentOutOfRangeException(nameof(weightValue));
            if (!float.IsFinite(weightTime) || weightTime < 0f) throw new ArgumentOutOfRangeException(nameof(weightTime));
            if (!float.IsFinite(weightRarity) || weightRarity < 0f) throw new ArgumentOutOfRangeException(nameof(weightRarity));
            if (!float.IsFinite(weightHysteresis) || weightHysteresis < 0f) throw new ArgumentOutOfRangeException(nameof(weightHysteresis));

            double totalWeight = (double)weightValue + (double)weightTime + (double)weightRarity + (double)weightHysteresis;
            if (!double.IsFinite(totalWeight) || totalWeight <= 0.0)
                throw new ArgumentException("At least one ranking weight must be strictly positive and total weight must be finite.");
            if (totalWeight > 10.0)
                throw new ArgumentOutOfRangeException(nameof(weightValue), "The sum of ranking weights must not exceed 10.0.");

            if (!float.IsFinite(valueScale) || valueScale <= 0f) throw new ArgumentOutOfRangeException(nameof(valueScale));
            if (!float.IsFinite(maxTravelSeconds) || maxTravelSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(maxTravelSeconds));
            if (!float.IsFinite(defaultSpeed) || defaultSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(defaultSpeed));

            WeightValue = weightValue;
            WeightTime = weightTime;
            WeightRarity = weightRarity;
            WeightHysteresis = weightHysteresis;
            ValueScale = valueScale;
            MaxTravelSeconds = maxTravelSeconds;
            DefaultSpeed = defaultSpeed;
        }
    }
}
