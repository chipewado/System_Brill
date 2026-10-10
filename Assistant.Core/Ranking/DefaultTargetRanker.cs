using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Ranking
{
    public sealed class DefaultTargetRanker : ITargetRanker
    {
        public IReadOnlyList<TargetEvaluationResult> RankCandidates(
            IReadOnlyList<TokenLocation> candidates,
            PlayerState player,
            RankingConfig config,
            string? currentTargetId,
            IReadOnlyDictionary<string, float>? verifiedPathCosts = null,
            float lineCostPerMeter = 1.0f)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!float.IsFinite(lineCostPerMeter) || lineCostPerMeter <= 0f)
                throw new ArgumentOutOfRangeException(nameof(lineCostPerMeter), "lineCostPerMeter must be positive and finite");

            if (!float.IsFinite(player.Position.X) || !float.IsFinite(player.Position.Y) || !float.IsFinite(player.Position.Z))
            {
                return Array.Empty<TargetEvaluationResult>();
            }

            double effectiveSpeed = (float.IsFinite(player.Speed) && player.Speed > 0f)
                ? (double)player.Speed
                : (double)config.DefaultSpeed;

            var results = new List<TargetEvaluationResult>(candidates.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || candidate.IsCollected)
                    continue;

                if (!float.IsFinite(candidate.Position.X) ||
                    !float.IsFinite(candidate.Position.Y) ||
                    !float.IsFinite(candidate.Position.Z))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(candidate.Id) || !seenIds.Add(candidate.Id))
                {
                    // Skip null/empty ID or duplicates (first occurrence wins)
                    continue;
                }

                // 1. Value component S_val
                double val = candidate.EstimatedValue;
                if (!double.IsFinite(val) || val < 0.0)
                {
                    val = 0.0;
                }
                double vRatio = val / (double)config.ValueScale;
                double sVal = double.IsNaN(vRatio) ? 0.0 : Math.Clamp(vRatio, 0.0, 1.0);

                // 2. Rarity component S_rar
                int rarityInt = (int)candidate.Rarity;
                double rRatio = rarityInt / 4.0;
                double sRar = double.IsNaN(rRatio) ? 0.0 : Math.Clamp(rRatio, 0.0, 1.0);

                // 3. Travel time component S_time
                float dist2D = Vec3.Distance2D(player.Position, candidate.Position);
                bool isPathVerified = false;
                float pathCostWorld;

                if (verifiedPathCosts != null && verifiedPathCosts.TryGetValue(candidate.Id, out float cost))
                {
                    pathCostWorld = cost;
                    isPathVerified = true;
                }
                else
                {
                    pathCostWorld = lineCostPerMeter * dist2D;
                    isPathVerified = false;
                }

                float travelSeconds;
                if (float.IsPositiveInfinity(pathCostWorld))
                {
                    travelSeconds = float.PositiveInfinity;
                }
                else if (!float.IsFinite(pathCostWorld) || pathCostWorld < 0f)
                {
                    pathCostWorld = lineCostPerMeter * dist2D;
                    travelSeconds = (float)((double)pathCostWorld / effectiveSpeed);
                    isPathVerified = false;
                }
                else
                {
                    travelSeconds = (float)((double)pathCostWorld / effectiveSpeed);
                }

                double tRatio = (double)travelSeconds / (double)config.MaxTravelSeconds;
                double sTime;
                if (double.IsNaN(tRatio) || tRatio >= 1.0)
                {
                    sTime = 0.0;
                }
                else if (tRatio <= 0.0)
                {
                    sTime = 1.0;
                }
                else
                {
                    sTime = 1.0 - tRatio;
                }

                // 4. Hysteresis component S_hyst
                double sHyst = (!string.IsNullOrEmpty(currentTargetId) && StringComparer.Ordinal.Equals(candidate.Id, currentTargetId))
                    ? 1.0
                    : 0.0;

                // 5. Total composite score
                double rawScore = (double)config.WeightValue * sVal
                                + (double)config.WeightTime * sTime
                                + (double)config.WeightRarity * sRar
                                + (double)config.WeightHysteresis * sHyst;

                double score = Math.Round(rawScore, 6, MidpointRounding.AwayFromZero);

                results.Add(new TargetEvaluationResult(
                    candidate,
                    score,
                    sVal,
                    sTime,
                    sRar,
                    sHyst,
                    travelSeconds,
                    pathCostWorld,
                    isPathVerified
                ));
            }

            // Deterministic descending order by score, then ordinal ID tie-breaker
            results.Sort((a, b) =>
            {
                int scoreCmp = b.Score.CompareTo(a.Score);
                if (scoreCmp != 0) return scoreCmp;
                return StringComparer.Ordinal.Compare(a.Token.Id, b.Token.Id);
            });

            return results;
        }
    }
}
