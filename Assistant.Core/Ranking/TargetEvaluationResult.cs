using System;
using Assistant.Core.Data;

namespace Assistant.Core.Ranking
{
    public sealed class TargetEvaluationResult
    {
        public TokenLocation Token { get; }
        public double Score { get; }
        public long ScoreMicros { get; }
        public double NormalizedValue { get; }
        public double NormalizedTime { get; }
        public double NormalizedRarity { get; }
        public double NormalizedHysteresis { get; }
        public float EstimatedTravelSeconds { get; }
        public float PathCostWorldUnits { get; }
        public bool IsPathVerified { get; }

        public TargetEvaluationResult(
            TokenLocation token,
            double score,
            long scoreMicros,
            double normalizedValue,
            double normalizedTime,
            double normalizedRarity,
            double normalizedHysteresis,
            float estimatedTravelSeconds,
            float pathCostWorldUnits,
            bool isPathVerified)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
            Score = score;
            ScoreMicros = scoreMicros;
            NormalizedValue = normalizedValue;
            NormalizedTime = normalizedTime;
            NormalizedRarity = normalizedRarity;
            NormalizedHysteresis = normalizedHysteresis;
            EstimatedTravelSeconds = estimatedTravelSeconds;
            PathCostWorldUnits = pathCostWorldUnits;
            IsPathVerified = isPathVerified;
        }

        public TargetEvaluationResult(
            TokenLocation token,
            double rawScore,
            double normalizedValue,
            double normalizedTime,
            double normalizedRarity,
            double normalizedHysteresis,
            float estimatedTravelSeconds,
            float pathCostWorldUnits,
            bool isPathVerified)
            : this(
                token,
                ((long)Math.Floor(rawScore * 1_000_000.0 + 0.5)) / 1_000_000.0,
                (long)Math.Floor(rawScore * 1_000_000.0 + 0.5),
                normalizedValue,
                normalizedTime,
                normalizedRarity,
                normalizedHysteresis,
                estimatedTravelSeconds,
                pathCostWorldUnits,
                isPathVerified)
        {
        }
    }
}
