using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Ranking
{
    public sealed class DefaultTargetRanker : ITargetRanker
    {
        public TargetEvaluationResult? Evaluate(
            TokenLocation candidate,
            PlayerState player,
            RankingConfig config,
            string? currentTargetId,
            float? verifiedPathCost = null,
            float lineCostPerMeter = 1.0f)
        {
            if (candidate == null || candidate.IsCollected)
                return null;
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!float.IsFinite(lineCostPerMeter) || lineCostPerMeter <= 0f)
                throw new ArgumentOutOfRangeException(nameof(lineCostPerMeter), "lineCostPerMeter must be positive and finite");

            // Planar 2.5D navigation: Horizontal plane is X-Z. Y is elevation and ignored.
            if (!float.IsFinite(player.Position.X) || !float.IsFinite(player.Position.Z))
                return null;
            if (!float.IsFinite(candidate.Position.X) || !float.IsFinite(candidate.Position.Z))
                return null;
            if (string.IsNullOrEmpty(candidate.Id))
                return null;

            double effectiveSpeed = (float.IsFinite(player.Speed) && player.Speed > 0f)
                ? (double)player.Speed
                : (double)config.DefaultSpeed;

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

            // 3. Travel time component S_time & Path cost
            double dist2D = Vec3.Distance2DDouble(player.Position, candidate.Position);
            double minLineCost = (double)lineCostPerMeter * dist2D;

            bool isPathVerified;
            double pathCostWorld;

            if (verifiedPathCost.HasValue)
            {
                float cost = verifiedPathCost.Value;

                // Unreachable paths (+Infinity) must never be selected as reachable targets
                if (float.IsPositiveInfinity(cost))
                {
                    return null;
                }

                // Invalid NaN, negative, or -Infinity costs must fail fast
                if (!float.IsFinite(cost) || cost < 0f)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(verifiedPathCost),
                        cost,
                        "Verified path cost must be a non-negative finite number or positive infinity.");
                }

                double vCost = (double)cost;

                double tol = 1e-5 * Math.Max(1.0, minLineCost) + 1e-6;

                // Lower-bound contract validation: verified cost cannot be strictly less than straight-line lower bound beyond relative tolerance
                if (vCost < minLineCost - tol)
                {
                    throw new ArgumentException(
                        $"Verified path cost ({vCost}) cannot be strictly less than straight-line lower bound ({minLineCost}) under lineCostPerMeter {lineCostPerMeter} beyond relative tolerance ({tol}).",
                        nameof(verifiedPathCost));
                }

                // Guard against floating-point epsilon underflow within tolerance [minLineCost - tol, minLineCost]
                pathCostWorld = Math.Max(vCost, minLineCost);
                isPathVerified = true;
            }
            else
            {
                pathCostWorld = minLineCost;
                isPathVerified = false;
            }

            double travelSecondsDouble = pathCostWorld / effectiveSpeed;
            float travelSeconds = (float)travelSecondsDouble;

            double tRatio = travelSecondsDouble / (double)config.MaxTravelSeconds;
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

            long scoreMicros = (long)Math.Floor(rawScore * 1_000_000.0 + 0.5);
            double score = scoreMicros / 1_000_000.0;

            return new TargetEvaluationResult(
                candidate,
                score,
                scoreMicros,
                sVal,
                sTime,
                sRar,
                sHyst,
                travelSeconds,
                (float)pathCostWorld,
                isPathVerified);
        }

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

            if (!float.IsFinite(player.Position.X) || !float.IsFinite(player.Position.Z))
            {
                return Array.Empty<TargetEvaluationResult>();
            }

            var results = new List<TargetEvaluationResult>(candidates.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || candidate.IsCollected)
                    continue;

                if (string.IsNullOrEmpty(candidate.Id) || !seenIds.Add(candidate.Id))
                {
                    // Skip null/empty ID or duplicates (first occurrence wins)
                    continue;
                }

                float? verifiedCost = null;
                if (verifiedPathCosts != null && verifiedPathCosts.TryGetValue(candidate.Id, out float cost))
                {
                    verifiedCost = cost;
                }

                var eval = Evaluate(candidate, player, config, currentTargetId, verifiedCost, lineCostPerMeter);
                if (eval != null)
                {
                    results.Add(eval);
                }
            }

            // Deterministic descending order by score (or ScoreMicros), then ordinal ID tie-breaker
            results.Sort((a, b) =>
            {
                int scoreCmp = b.ScoreMicros.CompareTo(a.ScoreMicros);
                if (scoreCmp != 0) return scoreCmp;
                return StringComparer.Ordinal.Compare(a.Token.Id, b.Token.Id);
            });

            return results;
        }
    }
}
