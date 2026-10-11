using Assistant.Core.Data;
using Assistant.Core.Ranking;

namespace Assistant.Core.Coordination
{
    /// <summary>
    /// Contract for target selection and path coordination workflow.
    /// Note: Implementations of IRouteCoordinator are not thread-safe and are intended for single-threaded use.
    /// </summary>
    public interface IRouteCoordinator
    {
        /// <summary>
        /// Selects the best candidate target and provides verified navigation path using branch-and-bound search.
        /// </summary>
        TargetDecision Coordinate(
            PlayerState player,
            RankingConfig ranking,
            CoordinationConfig coordination,
            string? currentTargetId);

    }
}
