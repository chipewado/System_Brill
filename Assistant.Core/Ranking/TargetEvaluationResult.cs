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
            double score,
            double normalizedValue,
            double normalizedTime,
            double normalizedRarity,
            double hysteresisBonus,
            float estimatedTravelSeconds,
            float pathCostWorldUnits,
            bool isPathVerified)
            : this(
                token,
                score,
                (long)Math.Round(score * 1_000_000.0, MidpointRounding.AwayFromZero),
                normalizedValue,
                normalizedTime,
                normalizedRarity,
                hysteresisBonus,
                estimatedTravelSeconds,
                pathCostWorldUnits,
                isPathVerified)
        {
        }
    }
}
