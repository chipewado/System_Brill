using System;
using Assistant.Core.Data;

namespace Assistant.Core.Ranking
{
    public sealed class TargetEvaluationResult
    {
        public TokenLocation Token { get; }
        public double Score { get; }
        public double NormalizedValue { get; }
        public double NormalizedTime { get; }
        public double NormalizedRarity { get; }
        public double HysteresisBonus { get; }
        public float EstimatedTravelSeconds { get; }
        public float PathCostWorldUnits { get; }
        public bool IsPathVerified { get; }

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
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
            Score = score;
            NormalizedValue = normalizedValue;
            NormalizedTime = normalizedTime;
            NormalizedRarity = normalizedRarity;
            HysteresisBonus = hysteresisBonus;
            EstimatedTravelSeconds = estimatedTravelSeconds;
            PathCostWorldUnits = pathCostWorldUnits;
            IsPathVerified = isPathVerified;
        }
    }
}
