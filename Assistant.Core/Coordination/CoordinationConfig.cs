using System;
using Assistant.Core.Ranking;

namespace Assistant.Core.Coordination
{
    /// <summary>
    /// Configuration for route coordination workflow and branch-and-bound search policy.
    /// Decouples orchestrator search budget and pruning policies from pure ranking scoring weights.
    /// </summary>
    public sealed class CoordinationConfig
    {
        public RankingConfig Ranking { get; }
        public float CommitRadius { get; }
        public float CommitOverrideMargin { get; }
        public int MaxPathSearches { get; }

        /// <summary>
        /// Slack in micro-score units (1e-6 = 1 micro) used by the coordinator's branch-and-bound pruning rule
        /// to guarantee admissibility against tiny floating-point variance.
        /// </summary>
        public long UpperBoundSlackMicros { get; }

        public CoordinationConfig(
            RankingConfig? ranking = null,
            float commitRadius = 15.0f,
            float commitOverrideMargin = 0.30f,
            int maxPathSearches = 8,
            long upperBoundSlackMicros = 2L)
        {
            Ranking = ranking ?? new RankingConfig();

            if (!float.IsFinite(commitRadius) || commitRadius < 0f)
                throw new ArgumentOutOfRangeException(nameof(commitRadius));
            if (float.IsNaN(commitOverrideMargin) || commitOverrideMargin < 0f)
                throw new ArgumentOutOfRangeException(nameof(commitOverrideMargin));
            if (maxPathSearches <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPathSearches));
            if (upperBoundSlackMicros < 0L)
                throw new ArgumentOutOfRangeException(nameof(upperBoundSlackMicros));

            CommitRadius = commitRadius;
            CommitOverrideMargin = commitOverrideMargin;
            MaxPathSearches = maxPathSearches;
            UpperBoundSlackMicros = upperBoundSlackMicros;
        }
    }
}
