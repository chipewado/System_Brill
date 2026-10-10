using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Ranking
{
    public interface ITargetRanker
    {
        IReadOnlyList<TargetEvaluationResult> RankCandidates(
            IReadOnlyList<TokenLocation> candidates,
            PlayerState player,
            RankingConfig config,
            string? currentTargetId,
            IReadOnlyDictionary<string, float>? verifiedPathCosts = null,
            float lineCostPerMeter = 1.0f);
    }
}
