# Phase 3 Architecture Contract Design: Navigation, Ranking & Route Coordination

**Status:** AMENDED & SPECIFIED (Phase 3 Final Correctness Review)  
**Target Projects:** `Assistant.Core` (Abstractions, Ranking, Pathfinding), `Assistant.Sim` (NavGrid Implementation, Sim Harness)  
**Framework:** .NET Standard 2.1 (`Assistant.Core`), .NET 10 (`Assistant.Tests`, `Assistant.Runner`)  

---

## 1. Revised Dependency Diagram & Architectural Boundaries

```
+--------------------------------------------------------------------------------+
|                               Assistant.Sim                                    |
|                                                                                |
|  +--------------------+         implements           +----------------------+  |
|  |     SimGridMap     | ---------------------------> |       INavGrid       |  |
|  | (Procedural Map,   |                              |  (Bounds, CellSize,  |  |
|  |  Terrain Types)    |                              |   MinTraversalCost,  |  |
|  +--------------------+                              |   Cost Lookup)       |  |
|           |                                          +----------------------+  |
|           |                                                ^            ^      |
+-----------|------------------------------------------------|------------|------+
            | references                                     |            |
+-----------v------------------------------------------------|------------|------+
|                               Assistant.Core               |            |      |
|                                                            |            |      |
|  +--------------------+      queries snapshot      +-------+----+       |      |
|  |  ITokenRepository  | <------------------------- |   Route    |       |      |
|  | (Token Discovery)  |                            | Coordinator|       |      |
|  +--------------------+                            +------------+       |      |
|                                                      |        |         |      |
|                                            evaluates |        | queries |      |
|                                           candidates |        | grid    |      |
|                                                      v        v         |      |
|                                                +-------------+          |      |
|                                                |ITargetRanker|          |      |
|                                                +-------------+          |      |
|                                                       ^                 |      |
|                                            implements |                 |      |
|                                                +-------------+          |      |
|                                                |DefaultTarget|          |      |
|                                                |   Ranker    |          |      |
|                                                +-------------+          |      |
|                                                                         |      |
|                                                    executes path search |      |
|                                                                         v      |
|                                                    +------------------------+  |
|                                                    |     AStarPathfinder    |  |
|                                                    | (8-Way, No-Corner-Cut, |  |
|                                                    |  Bounded Expansions)   |  |
|                                                    +------------------------+  |
+--------------------------------------------------------------------------------+
```

### Boundary & Responsibility Invariants
1. **`RouteCoordinator`** is the sole workflow orchestrator. It receives `ITokenRepository`, `ITargetRanker`, `IPathfinder`, and `INavGrid` via constructor injection. It queries `ITokenRepository.GetAvailable()` to obtain an immutable candidate snapshot, handles start-cell validation and snapping, coordinates candidate evaluation, uses branch-and-bound pruning with verified upper bounds, evaluates commit radius stability, and issues a deterministic `TargetDecision`.
2. **`DefaultTargetRanker`** is a pure, stateless scoring engine implementing `ITargetRanker`. It operates exclusively on in-memory candidate snapshots (`IReadOnlyList<TokenLocation>`), `PlayerState`, and an immutable `RankingConfig`. It **never** queries repositories, grids, or pathfinders.
3. **`AStarPathfinder`** is a pure graph search engine receiving `INavGrid` via constructor injection. It has zero knowledge of tokens, player state, ranking scores, or display modes.
4. **`INavGrid`** lives in `Assistant.Core.Abstractions`. `SimGridMap` in `Assistant.Sim` implements `INavGrid`. Core has zero references to `Assistant.Sim`.
5. **No Presentation Leaks**: Target decisions depend strictly on domain tokens and physical grid metrics, completely decoupled from `DisplayModeManager` presentation filters.
6. **No Real-Game Automation**: All navigation and ranking components only recommend targets and waypoints. Any player movement or token collection logic exists solely inside `Assistant.Sim` test harnesses. No hooks, memory scanning, injection, or packet inspection.

---

## 2. Corrected Interfaces & Data Contracts

### 2.1 Navigation Grid Abstraction (`Assistant.Core.Abstractions`)

```csharp
namespace Assistant.Core.Abstractions
{
    public interface INavGrid
    {
        int GridWidth { get; }
        int GridHeight { get; }
        float CellSize { get; }
        float MinTraversalCost { get; }

        bool IsInBounds(GridPosition gridPos);
        bool IsWalkable(GridPosition gridPos);
        float GetTraversalCost(GridPosition gridPos);

        GridPosition WorldToGrid(Vec3 worldPos);
        Vec3 GridToWorld(GridPosition gridPos);
    }
}
```

#### Contract Invariants for `INavGrid`:
- `GridWidth > 0`, `GridHeight > 0`.
- `CellSize > 0` and `float.IsFinite(CellSize)`.
- `MinTraversalCost > 0` and `float.IsFinite(MinTraversalCost)`.
- For any cell `p`: `IsWalkable(p) == true` **if and only if** `IsInBounds(p) == true` and `float.IsFinite(GetTraversalCost(p))` and `GetTraversalCost(p) >= MinTraversalCost`.
- For unwalkable or out-of-bounds cells, `GetTraversalCost(p)` returns `float.PositiveInfinity`.
- Default Simulator Map dimensions: $100 \times 100$ cells, `CellSize = 10.0f` (representing a $1000\text{m} \times 1000\text{m}$ area), `MinTraversalCost = 1.0f`.

#### Coordinate Mapping & Grid Conventions:
1. **Axes & Orientation**: 2.5D planar navigation. World horizontal axes are $X$ and $Z$. World vertical axis $Y$ is elevation; it is ignored during grid mapping and preserved as $0.0f$ in generated waypoints.
2. **Origin & Bounds**: World coordinates $(0.0, 0.0)$ correspond to the minimum corner of cell $(0, 0)$. Valid cell indices are $0 \le X < \text{GridWidth}$ and $0 \le Z < \text{GridHeight}$.
3. **WorldToGrid**:
   $$X = \lfloor \text{worldPos.X} / \text{CellSize} \rfloor$$
   $$Z = \lfloor \text{worldPos.Z} / \text{CellSize} \rfloor$$
   Implemented using `(int)Math.Floor(...)`. If coordinates contain NaN or $\pm\infty$, `IsInBounds` evaluates to `false`.
4. **GridToWorld**:
   Returns the horizontal **center** of the cell:
   $$\text{worldX} = (\text{gridPos.X} + 0.5f) \times \text{CellSize}$$
   $$\text{worldZ} = (\text{gridPos.Z} + 0.5f) \times \text{CellSize}$$
   $$\text{worldY} = 0.0f$$

### 2.2 Pathfinding Contracts (`Assistant.Core.Pathfinding`)

```csharp
namespace Assistant.Core.Pathfinding
{
    public enum PathStatus
    {
        Success = 0,
        InvalidStart = 1,
        InvalidGoal = 2,
        Unreachable = 3,
        SearchBudgetExceeded = 4
    }

    public sealed class PathResult
    {
        public PathStatus Status { get; }
        public IReadOnlyList<GridPosition> Steps { get; }
        public IReadOnlyList<Vec3> Waypoints { get; }
        public float GridCost { get; }
        public int NodesExpanded { get; }

        private PathResult(
            PathStatus status,
            IReadOnlyList<GridPosition> steps,
            IReadOnlyList<Vec3> waypoints,
            float gridCost,
            int nodesExpanded)
        {
            Status = status;
            Steps = steps;
            Waypoints = waypoints;
            GridCost = gridCost;
            NodesExpanded = nodesExpanded;
        }

        public static PathResult Successful(
            GridPosition[] steps,
            Vec3[] waypoints,
            float gridCost,
            int nodesExpanded)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));

            // Defensive copies to guarantee immutability against caller mutation
            var defensiveSteps = (GridPosition[])steps.Clone();
            var defensiveWaypoints = (Vec3[])waypoints.Clone();

            return new PathResult(
                PathStatus.Success,
                Array.AsReadOnly(defensiveSteps),
                Array.AsReadOnly(defensiveWaypoints),
                gridCost,
                nodesExpanded);
        }

        public static PathResult Failure(PathStatus status, int nodesExpanded)
        {
            if (status == PathStatus.Success)
                throw new ArgumentException("Cannot create failure with Success status", nameof(status));

            return new PathResult(
                status,
                Array.Empty<GridPosition>(),
                Array.Empty<Vec3>(),
                float.PositiveInfinity,
                nodesExpanded);
        }
    }

    public interface IPathfinder
    {
        PathResult FindPath(GridPosition start, GridPosition goal, int maxExpansions = 10000);
        PathResult FindPath(Vec3 startWorld, Vec3 goalWorld, int maxExpansions = 10000);
    }
}
```

*Note*: `AStarPathfinder` accepts `INavGrid` via constructor injection:
```csharp
public sealed class AStarPathfinder : IPathfinder
{
    private readonly INavGrid _grid;
    public AStarPathfinder(INavGrid grid) { _grid = grid ?? throw new ArgumentNullException(nameof(grid)); }
    ...
}
```

### 2.3 Ranking Contracts (`Assistant.Core.Ranking`)

```csharp
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
        /// <summary>
        /// Margin in absolute score units required for a challenger to override an in-commit target.
        /// Calibrated for a default total weight sum of 1.0.
        /// </summary>
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
            if (!float.IsFinite(weightValue) || weightValue < 0) throw new ArgumentOutOfRangeException(nameof(weightValue));
            if (!float.IsFinite(weightTime) || weightTime < 0) throw new ArgumentOutOfRangeException(nameof(weightTime));
            if (!float.IsFinite(weightRarity) || weightRarity < 0) throw new ArgumentOutOfRangeException(nameof(weightRarity));
            if (!float.IsFinite(weightHysteresis) || weightHysteresis < 0) throw new ArgumentOutOfRangeException(nameof(weightHysteresis));

            double totalWeight = (double)weightValue + (double)weightTime + (double)weightRarity + (double)weightHysteresis;
            if (!double.IsFinite(totalWeight) || totalWeight <= 0.0)
                throw new ArgumentException("At least one ranking weight must be strictly positive and total weight must be finite.");
            if (totalWeight > 10.0)
                throw new ArgumentOutOfRangeException(nameof(weightValue), "The sum of ranking weights must not exceed 10.0.");

            if (!float.IsFinite(valueScale) || valueScale <= 0) throw new ArgumentOutOfRangeException(nameof(valueScale));
            if (!float.IsFinite(maxTravelSeconds) || maxTravelSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(maxTravelSeconds));
            if (!float.IsFinite(defaultSpeed) || defaultSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(defaultSpeed));
            if (!float.IsFinite(commitRadius) || commitRadius < 0) throw new ArgumentOutOfRangeException(nameof(commitRadius));
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

    public sealed class TargetEvaluationResult
    {
        public TokenLocation Token { get; }
        public double Score { get; }
        public long ScoreMicros { get; }
        public double NormalizedValue { get; }
        public double NormalizedTime { get; }
        public double NormalizedRarity { get; }
        public double NormalizedHysteresis { get; }
        public double HysteresisBonus => NormalizedHysteresis;
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
    }

    public interface ITargetRanker
    {
        TargetEvaluationResult? Evaluate(
            TokenLocation candidate,
            PlayerState player,
            RankingConfig config,
            string? currentTargetId,
            float? verifiedPathCost = null,
            float lineCostPerMeter = 1.0f);

        IReadOnlyList<TargetEvaluationResult> RankCandidates(
            IReadOnlyList<TokenLocation> candidates,
            PlayerState player,
            RankingConfig config,
            string? currentTargetId,
            IReadOnlyDictionary<string, float>? verifiedPathCosts = null,
            float lineCostPerMeter = 1.0f);
    }
}
```

### 2.4 Coordination Contracts (`Assistant.Core.Coordination`)

```csharp
namespace Assistant.Core.Coordination
{
    public enum CoordinationTerminationReason
    {
        OptimalFound,
        SearchBudgetReached,
        AllCandidatesEvaluated,
        NoValidCandidates,
        PlayerOnBlockedCell
    }

    public sealed class TargetDecision
    {
        public TokenLocation? SelectedTarget { get; }
        public PathResult? SelectedPath { get; }
        public TargetEvaluationResult? Evaluation { get; }
        public CoordinationTerminationReason TerminationReason { get; }
        public int PathSearchesExecuted { get; }
        public IReadOnlyList<string> SkippedCandidateIds { get; }
        public IReadOnlyDictionary<string, PathStatus> DiagnosticStatusByCandidateId { get; }
        public bool PlayerSnappedToWalkable { get; }

        public static TargetDecision None(
            CoordinationTerminationReason reason,
            int searchesExecuted,
            IReadOnlyList<string> skipped,
            IReadOnlyDictionary<string, PathStatus> diagnostics,
            bool playerSnapped = false) =>
            new TargetDecision(null, null, null, reason, searchesExecuted, skipped, diagnostics, playerSnapped);

        public TargetDecision(
            TokenLocation? selectedTarget,
            PathResult? selectedPath,
            TargetEvaluationResult? evaluation,
            CoordinationTerminationReason terminationReason,
            int pathSearchesExecuted,
            IReadOnlyList<string> skippedCandidateIds,
            IReadOnlyDictionary<string, PathStatus> diagnosticStatusByCandidateId,
            bool playerSnappedToWalkable = false)
        {
            SelectedTarget = selectedTarget;
            SelectedPath = selectedPath;
            Evaluation = evaluation;
            TerminationReason = terminationReason;
            PathSearchesExecuted = pathSearchesExecuted;
            SkippedCandidateIds = skippedCandidateIds ?? Array.Empty<string>();
            DiagnosticStatusByCandidateId = diagnosticStatusByCandidateId ?? new Dictionary<string, PathStatus>();
            PlayerSnappedToWalkable = playerSnappedToWalkable;
        }
    }

    public interface IRouteCoordinator
    {
        TargetDecision Coordinate(
            PlayerState player,
            RankingConfig ranking,
            CoordinationConfig coordination,
            string? currentTargetId);
    }
}
```

#### Coordination Termination Reason Table & Precedence Order:
| Precedence | TerminationReason | Exact Trigger Condition | Operational Meaning |
| :---: | :--- | :--- | :--- |
| **1** | `PlayerOnBlockedCell` | Player's world position maps to an unwalkable or out-of-bounds cell, and Chebyshev ring search (radii 1 and 2) yields no walkable cell. | Terminal failure: Player cannot navigate anywhere. Candidate evaluation is aborted. |
| **2** | `NoValidCandidates` | Player coordinates are non-finite, OR no candidates remain after filtering, OR all evaluated candidates were invalid or unreachable and no valid target exists. | No reachable, valid target candidate exists in the repository snapshot. |
| **3** | `OptimalFound` | A best verified target was found, AND all remaining unevaluated candidates satisfy the proven pruning bound (`upperBound + UpperBoundSlack < bestVerified` or tie-break rule), with zero inconclusive/budget-exceeded candidate evaluations that could beat it. | Mathematically proven optimal candidate under the implemented model. |
| **4** | `SearchBudgetReached` | At least one unpruned, unevaluated candidate remains because `MaxPathSearches` was reached, OR at least one evaluated candidate failed with `SearchBudgetExceeded` whose upper bound could have beaten the selected target. | Best-effort candidate returned; optimality not mathematically guaranteed due to budget cutoff. |
| **5** | `AllCandidatesEvaluated` | Every valid candidate in the snapshot was evaluated (all returned `Success`, `Unreachable`, `InvalidStart`, or `InvalidGoal`), zero unpruned candidates remain, no search budget was exceeded, and the best verified target was selected. | Exhaustive evaluation completed for all candidates. |

---

## 3. Exact Ranking, Path Cost, and Branch-and-Bound Mathematical Proof

### 3.1 World Path Cost vs Straight-Line Lower Bound

Let player position in continuous world space be $p = (x_p, z_p)$ and token position be $t = (x_t, z_t)$.  
Let the start cell be $c_{\text{start}} = \text{WorldToGrid}(p)$ and goal cell be $c_{\text{goal}} = \text{WorldToGrid}(t)$.

1. **Straight-Line Euclidean Distance ($d_{2D}$)**:
   $$d_{2D}(p, t) = \sqrt{(x_p - x_t)^2 + (z_p - z_t)^2}$$

2. **Discrete Grid Path Cost (`GridCost`)**:
   A* computes the sequence of transitions between cell centers. For any transition from cell $u$ to neighbor $v$:
   $$c(u, v) = \text{CellSize} \cdot \Delta_{\text{step}} \cdot \text{GetTraversalCost}(v)$$
   Where $\Delta_{\text{step}} = 1.0$ for cardinal moves and $\sqrt{2}$ for diagonal moves.
   Since $\text{GetTraversalCost}(v) \ge \text{MinTraversalCost} > 0$ for all walkable cells, and each step's Euclidean distance between centers is $\text{CellSize} \cdot \Delta_{\text{step}}$,
   $$\text{GridCost} \ge \text{MinTraversalCost} \cdot \|\text{center}(c_{\text{start}}) - \text{center}(c_{\text{goal}})\|_2$$

3. **Composite World Path Cost (`pathCostWorld`)**:
   Because the player starts at continuous world coordinate $p$ (inside or near $c_{\text{start}}$) and the token is located at continuous world coordinate $t$ (inside $c_{\text{goal}}$), the complete world traversal cost must account for endpoint connectors:
   $$\text{pathCostWorld} = \text{GridCost} + \text{MinTraversalCost} \cdot \left( d_{2D}(p, \text{center}(c_{\text{start}})) + d_{2D}(\text{center}(c_{\text{goal}}), t) \right)$$
   *Snapping Rule*: If the player position $p$ was snapped to a walkable cell $c_{\text{snapped}}$, the navigation search start cell is $c_{\text{start}} = c_{\text{snapped}}$. Crucially, $p$ in $d_{2D}(p, \text{center}(c_{\text{start}}))$ remains the **actual player continuous world position** $p$. By preserving $p$, the Euclidean triangle inequality between $p$, $\text{center}(c_{\text{start}})$, $\text{center}(c_{\text{goal}})$, and $t$ holds strictly and unconditionally even across snapped cells.
   *Ranker Contract*: The ranker's `verifiedPathCosts` dictionary maps `token.Id` $\rightarrow \text{pathCostWorld}$.

4. **Rigorous Proof that Stage Cost Lower Bound Holds**:
   By the Euclidean triangle inequality across the four consecutive points $p \rightarrow \text{center}(c_{\text{start}}) \rightarrow \text{center}(c_{\text{goal}}) \rightarrow t$:
   $$d_{2D}(p, t) \le d_{2D}(p, \text{center}(c_{\text{start}})) + \|\text{center}(c_{\text{start}}) - \text{center}(c_{\text{goal}})\|_2 + d_{2D}(\text{center}(c_{\text{goal}}), t)$$
   Multiplying through by $\text{MinTraversalCost} > 0$:
   $$\text{MinTraversalCost} \cdot d_{2D}(p, t) \le \text{MinTraversalCost} \cdot d_{2D}(p, \text{center}(c_{\text{start}})) + \text{MinTraversalCost} \cdot \|\text{center}(c_{\text{start}}) - \text{center}(c_{\text{goal}})\|_2 + \text{MinTraversalCost} \cdot d_{2D}(\text{center}(c_{\text{goal}}), t)$$
   Substituting the inequality $\text{GridCost} \ge \text{MinTraversalCost} \cdot \|\text{center}(c_{\text{start}}) - \text{center}(c_{\text{goal}})\|_2$:
   $$\text{MinTraversalCost} \cdot d_{2D}(p, t) \le \text{pathCostWorld}$$
   **Theorem**: For any arbitrary continuous world positions $p, t$ (whether snapped or non-snapped, central or non-central) and any grid with $\text{MinTraversalCost} > 0$:
   The quantity $C_{\text{lower}}(p, t) = \text{MinTraversalCost} \cdot d_{2D}(p, t)$ is a mathematically proven, valid lower bound on $\text{pathCostWorld}$.

### 3.2 Monotonic Scoring Upper Bound

1. **Travel Time Lower Bound & Line Cost Per Meter**:
   $$v_{\text{eff}} = \begin{cases} \text{player.Speed}, & \text{if } \text{float.IsFinite(player.Speed)} \text{ and } \text{player.Speed} > 0 \\ \text{config.DefaultSpeed}, & \text{otherwise} \end{cases}$$
   - `RankCandidates` accepts parameter `float lineCostPerMeter`. The `RouteCoordinator` **MUST** pass `lineCostPerMeter = grid.MinTraversalCost`.
   - For candidates **NOT** in the `verifiedPathCosts` dictionary:
     $$T_{\text{sec, min}} = \frac{C_{\text{lower}}(p, t)}{v_{\text{eff}}} = \frac{\text{grid.MinTraversalCost} \cdot d_{2D}(p, t)}{v_{\text{eff}}}$$
     `PathCostWorldUnits` $= \text{grid.MinTraversalCost} \cdot d_{2D}(p, t)$, and `IsPathVerified = false` (upper-bound stage).
   - For candidates **IN** the `verifiedPathCosts` dictionary:
     $$T_{\text{sec, path}} = \frac{\text{pathCostWorld}}{v_{\text{eff}}}$$
     `PathCostWorldUnits` $= \text{pathCostWorld}$, and `IsPathVerified = true`.
   Since $C_{\text{lower}} \le \text{pathCostWorld}$, we have $T_{\text{sec, min}} \le T_{\text{sec, path}}$.

2. **Component Normalization & Clamping**:
   - All score and upper-bound math is evaluated strictly in `double` precision.
   - $S_{\text{val}} = \text{clamp}\left(\frac{\text{token.EstimatedValue}}{\text{config.ValueScale}}, 0.0, 1.0\right)$ (Treat non-finite or negative values as $0.0$).
   - $S_{\text{rar}} = \text{clamp}\left(\frac{(\text{int})\text{token.Rarity}}{4.0}, 0.0, 1.0\right)$ (Invalid/out-of-range enums clamped to $[0.0, 1.0]$).
   - $S_{\text{hyst}} = 1.0$ if $\text{token.Id} == \text{currentTargetId}$, else $0.0$.
   - $S_{\text{time}}(T) = \text{clamp}\left(1.0 - \frac{T}{\text{config.MaxTravelSeconds}}, 0.0, 1.0\right)$.
   Since $f(T) = \text{clamp}(1 - T/T_{\max}, 0, 1)$ is monotonically non-increasing for all $T \ge 0$,
   $$S_{\text{time}}(T_{\text{sec, min}}) \ge S_{\text{time}}(T_{\text{sec, path}})$$

3. **Total Score Upper Bound**:
   Because all weights $w_v, w_t, w_r, w_h \ge 0$,
   $$\text{Score}_{\text{upperBound, raw}} \ge \text{Score}_{\text{pathVerified, raw}}$$
   Computing integer micro-scores via `ScoreMicros = (long)Math.Floor(raw * 1e6 + 0.5)` preserves monotonicity:
   $$\text{ScoreMicros}_{\text{upperBound}} \ge \text{ScoreMicros}_{\text{pathVerified}}$$

### 3.3 Coherent Score Definition, Rounding & Pruning Tie Rule

1. **Single Coherent Score Definition & ScoreMicros**:
   `ScoreMicros = (long)Math.Floor(raw * 1e6 + 0.5)` is computed directly from the RAW double score. `Score = ScoreMicros / 1e6`. All sorting, ranking, best-candidate tracking, tie-breaking, commit radius overrides, and pruning decisions evaluate `ScoreMicros` exclusively.
2. **Determinism Statement**:
   The determinism contract is explicitly guaranteed for **"same build, same runtime, same inputs"**. Integer micro-scores provide exact integer comparisons within one build and prevent floating-point representation ambiguity; they do not claim cross-runtime bit identity across different architectures without fixed-point arithmetic.
3. **Slack-Adjusted Pruning Invariant (`UpperBoundSlack = 1e-6` / `UpperBoundSlackMicros = 2L`)**:
   A candidate $C$ with straight-line upper bound $\text{Score}_{\text{upperBound}}(C)$ and adjusted upper bound $\text{AdjustedUpper} = \text{ScoreMicros}_{\text{upperBound}}(C) + \text{UpperBoundSlackMicros}$ is evaluated against the effective threshold $\text{Threshold}$:
   - **Outside Commit Radius** (or when incumbent was not retained):
     $\text{Threshold} = \text{ScoreMicros}_{\text{verified}}(B)$. Candidate $C$ is pruned if and only if:
     $$\text{AdjustedUpper} < \text{Threshold} \quad \textbf{OR} \quad (\text{AdjustedUpper} == \text{Threshold} \ \land \ \text{StringComparer.Ordinal.Compare}(C.\text{Id}, B.\text{Id}) > 0)$$
   - **Inside Commit Radius** (incumbent retained under commit policy):
     $\text{Threshold} = \text{ScoreMicros}_{\text{verified}}(\text{Current}) + \text{ScaledMarginMicros}$.
     Because any candidate whose verified score reaches or exceeds $\text{Threshold}$ dethrones the incumbent regardless of ID ordering, equality with $\text{Threshold}$ must not be pruned based on ID. Candidate $C$ is pruned if and only if:
     $$\text{AdjustedUpper} < \text{Threshold}$$
   *Justification*: The slack $\epsilon = 2\mu$ guarantees admissibility against tiny floating-point variance. With slack $> 0$, the tie branch only matters inside the sub-slack band.

### 3.4 CommitRadius Semantics & Replacement Threshold

1. **Stability Policy**:
   `CommitRadius` is explicitly a **spatial anti-jitter and path-stability policy**, not a guarantee of global score optimality. When the player enters the commit radius of their current target, target switching is suppressed unless an exceptional challenger emerges or the current target is compromised.

2. **Single Unambiguous Replacement Condition**:
   When `currentTargetId` is set, available in the repository, within continuous 2D distance $d_{2D}(p, \text{Current.Position}) \le \text{CommitRadius}$, and verified reachable (`PathStatus.Success`):
   $\text{CommitOverrideMargin}$ is defined as a fraction of the maximum theoretical score, scaled by the total ranking weight sum $W = \sum w_i$:
   $$\text{ScaledMarginMicros} = \lfloor \text{CommitOverrideMargin} \cdot W \cdot 10^6 + 0.5 \rfloor$$
   A path-verified challenger $C$ overrides / replaces the current target if and only if:
   $$\text{ScoreMicros}_{\text{verified}}(C) \ge \text{ScoreMicros}_{\text{verified}}(\text{Current}) + \text{ScaledMarginMicros}$$

3. **Hysteresis Accounting (Strict Single-Count Guarantee)**:
   - For the current target, $\text{Score}_{\text{verified}}(\text{Current})$ is evaluated with `currentTargetId` matching its ID, so it includes the hysteresis bonus:
     $$\text{Score}_{\text{verified}}(\text{Current}) = \text{BaseScore}(\text{Current}) + w_h \cdot 1.0$$
   - For challenger $C$, $\text{Score}_{\text{verified}}(C)$ has $s_{\text{hyst}} = 0$, so $\text{Score}_{\text{verified}}(C) = \text{BaseScore}(C)$.
   - In terms of intrinsic base scores (scores without hysteresis):
     $$\text{BaseScore}(C) \ge \text{BaseScore}(\text{Current}) + \text{HysteresisBonus} + \text{ScaledMargin}$$
   - Comparing final evaluated scores ($\text{Score}_{\text{verified}}(C) \ge \text{Score}_{\text{verified}}(\text{Current}) + \text{ScaledMargin}$) guarantees that the hysteresis bonus is **counted exactly once**. Implementations must never add hysteresis a second time.

4. **Edge Behaviors and Boundary Rules**:
   - **Zero Margin (`CommitOverrideMargin == 0.0f`)**:
     Condition reduces to $\text{Score}_{\text{verified}}(C) \ge \text{Score}_{\text{verified}}(\text{Current})$. Challenger replaces Current if it overcomes Current's hysteresis bonus.
   - **Equal Scores / Exact Boundary (`Score(C) == Score(Current) + ScaledMargin`)**:
     Evaluated with strict $\ge$. If challenger's verified score matches the threshold exactly, replacement triggers.
   - **Ordinal ID Ties**:
     Current target is the incumbent. Challenger only replaces Current if $\text{Score}_{\text{verified}}(C) \ge \text{Score}_{\text{verified}}(\text{Current}) + \text{ScaledMargin}$. If `Margin > 0` and $\text{Score}_{\text{verified}}(C) < \text{Score}_{\text{verified}}(\text{Current}) + \text{ScaledMargin}$, no tie-breaking occurs; Current is retained regardless of whether $C$'s ID is lexicographically smaller than Current's ID.
     If multiple challengers exceed the threshold, ties between challengers are broken by standard ranking rules: higher verified score, then lexicographically smaller ID (`StringComparer.Ordinal.Compare(C1.Id, C2.Id) < 0`).
   - **Positive-Infinity Margin (`CommitOverrideMargin == float.PositiveInfinity`)**:
     Evaluated unconditionally BEFORE any arithmetic or branch-and-bound searches: if the current target is verified reachable, it is immediately selected with `OptimalFound` and searchesRun = 1.
   - **Reachability Verification Requirement**:
     The commit-radius fast path **MUST** path-verify the current target first via `_pathfinder.FindPath(player.Position, currentToken.Position)`. If the path search returns `Unreachable`, `InvalidGoal`, or `SearchBudgetExceeded`, commitment is immediately voided. Current target is **not** retained, its status is recorded in diagnostics, and the coordinator falls back to standard candidate ranking and branch-and-bound.

5. **Concrete Numeric Examples**:
   - *Example A (Default config: $w_h = 0.10, \text{Margin} = 0.30$)*:
     Current target base score $= 0.450000$.
     $\text{Score}_{\text{verified}}(\text{Current}) = 0.450000 + 0.100000 = 0.550000$.
     Override threshold $= 0.550000 + 0.300000 = 0.850000$.
     - Challenger X with base score $0.849999$: $0.849999 < 0.850000 \implies$ Current is retained.
     - Challenger Y with base score $0.850000$: $0.850000 \ge 0.850000 \implies$ Challenger Y replaces Current.
     - Challenger Z with base score $0.890000$: $0.890000 \ge 0.850000 \implies$ Challenger Z replaces Current.
   - *Example B (Zero margin: $\text{Margin} = 0.00$)*:
     Current target evaluated score $= 0.550000$. Threshold $= 0.550000$.
     - Challenger X with score $0.549999$: $0.549999 < 0.550000 \implies$ Current is retained.
     - Challenger Y with score $0.550000$: $0.550000 \ge 0.550000 \implies$ Challenger Y replaces Current.
   - *Example C (Positive infinity margin)*:
     Current target evaluated score $= 0.550000$. Threshold $= +\infty$.
     Current verified reachable in 1 search $\implies$ Current retained immediately (`OptimalFound`, 1 search). No challengers searched.

---

## 4. Exact A* Execution Loop, Budget Off-by-One & Snap Semantics

### 4.1 Strict Execution Flow & Step Order

The A* search algorithm in `AStarPathfinder` executes strictly according to the following order:

```csharp
public PathResult FindPath(GridPosition start, GridPosition goal, int maxExpansions = 10000)
{
    // Step 1: Input Validation
    if (maxExpansions <= 0)
        throw new ArgumentOutOfRangeException(nameof(maxExpansions), "Expansion budget must be strictly positive");

    if (!_grid.IsInBounds(start) || !_grid.IsWalkable(start))
        return PathResult.Failure(PathStatus.InvalidStart, 0);

    if (!_grid.IsInBounds(goal) || !_grid.IsWalkable(goal))
        return PathResult.Failure(PathStatus.InvalidGoal, 0);

    // Step 2: Identity Check (Zero Cost)
    if (start.X == goal.X && start.Z == goal.Z)
    {
        var singleStep = new[] { start };
        var singleWaypoint = new[] { _grid.GridToWorld(start) };
        return PathResult.Successful(singleStep, singleWaypoint, 0f, nodesExpanded: 0);
    }

    // Step 3: Priority Queue Initialization
    // Priority tuple: (fCost, -gCost, sequenceCounter, nodeIndex)
    var openQueue = new MinPriorityQueue();
    int nodesExpanded = 0;
    long sequenceCounter = 0;

    int startIndex = GetIndex(start);
    int goalIndex = GetIndex(goal);

    gScore[startIndex] = 0f;
    float hStart = ComputeOctileHeuristic(start, goal);
    openQueue.Enqueue(startIndex, hStart, 0f, ++sequenceCounter);

    // Step 4: Search Loop
    while (openQueue.Count > 0)
    {
        var current = openQueue.Dequeue();

        // 4a: Stale Entry Check (already closed by an earlier, cheaper path)
        if (closed[current.Index])
            continue;

        // 4b: Goal Detection (BEFORE Budget Increment)
        // If the popped node is the goal, search succeeds immediately!
        // The goal node itself is NOT expanded to its neighbors.
        if (current.Index == goalIndex)
        {
            return ReconstructPath(current.Index, nodesExpanded);
        }

        // 4c: Budget Accounting & Exhaustion Check (BEFORE incrementing and expanding)
        if (nodesExpanded >= maxExpansions)
        {
            return PathResult.Failure(PathStatus.SearchBudgetExceeded, nodesExpanded);
        }
        nodesExpanded++;

        // 4d: Mark Closed
        closed[current.Index] = true;

        // 4e: Expand 8 Neighbors in Fixed Sequence (N, NE, E, SE, S, SW, W, NW)
        foreach (var dir in NeighborDirections)
        {
            var neighborPos = new GridPosition(current.X + dir.Dx, current.Z + dir.Dz);
            if (!_grid.IsInBounds(neighborPos) || !_grid.IsWalkable(neighborPos))
                continue;

            // Strict No-Corner-Cutting Check for diagonal moves
            if (dir.IsDiagonal)
            {
                var card1 = new GridPosition(current.X + dir.Dx, current.Z);
                var card2 = new GridPosition(current.X, current.Z + dir.Dz);
                if (!_grid.IsWalkable(card1) || !_grid.IsWalkable(card2))
                    continue;
            }

            int neighborIndex = GetIndex(neighborPos);
            if (closed[neighborIndex])
                continue;

            float stepCost = _grid.CellSize * dir.StepFactor * _grid.GetTraversalCost(neighborPos);
            float tentativeG = gScore[current.Index] + stepCost;

            if (tentativeG < gScore[neighborIndex])
            {
                gScore[neighborIndex] = tentativeG;
                cameFrom[neighborIndex] = current.Index;
                float h = ComputeOctileHeuristic(neighborPos, goal);
                float f = tentativeG + h;
                openQueue.Enqueue(neighborIndex, f, tentativeG, ++sequenceCounter);
            }
        }
    }

    // Step 5: Queue Exhaustion without reaching goal
    return PathResult.Failure(PathStatus.Unreachable, nodesExpanded);
}
```

#### Definition of Expanded Node & Budget Invariants:
An expanded node is a node that is popped from Open, verified not to be stale, verified not to be the goal, passes the budget check, and has its outgoing neighbors evaluated.
- **Strict Invariant**: `NodesExpanded <= maxExpansions` holds unconditionally at all times.
- If `start == goal`: 0 nodes expanded (`Success`).
- If `maxExpansions = 1` and goal is adjacent: start is popped, `nodesExpanded` becomes 1, its neighbors (including goal) are enqueued. Next iteration pops goal: Goal Detection (4b) fires before budget check, terminating with `Success` and `nodesExpanded = 1`.
- If `maxExpansions = 1` and goal is 2 cells away: start is popped, `nodesExpanded` becomes 1, neighbors enqueued. Next iteration pops an intermediate neighbor: Budget check `1 >= 1` fires before expansion, terminating with `SearchBudgetExceeded` and `nodesExpanded = 1`.

### 4.2 Start Cell Snapping & `PlayerOnBlockedCell`

If the player's continuous position $p$ maps via `WorldToGrid(p)` to an unwalkable cell or out-of-bounds cell:
1. Search Chebyshev rings of radius $r \in \{1, 2\}$ centered at $c_{\text{player}}$.
2. Ring 1 evaluates 8 neighbors; Ring 2 evaluates 16 neighbors.
3. Candidate walkable cells are ordered by:
   - Primary: Ascending Euclidean distance to $p$
   - Secondary: Ascending $X$
   - Tertiary: Ascending $Z$
4. If a walkable cell is found, snap the search start cell $c_{\text{start}}$ to it, and record `playerSnappedToWalkable = true` in `TargetDecision`.
5. If no walkable cell exists within radius 2, terminate coordination immediately with:
   `CoordinationTerminationReason.PlayerOnBlockedCell`. No candidates are evaluated.

---

## 5. Revised RouteCoordinator Algorithm

```csharp
public TargetDecision Coordinate(
    PlayerState player,
    RankingConfig config,
    string? currentTargetId)
{
    // Step 1: Validate player coordinates
    if (!float.IsFinite(player.Position.X) || !float.IsFinite(player.Position.Y) || !float.IsFinite(player.Position.Z))
        return TargetDecision.None(CoordinationTerminationReason.NoValidCandidates, 0, Array.Empty<string>(), EmptyDiag);

    // Step 2: Resolve Start Cell & Snapping
    var playerGrid = _grid.WorldToGrid(player.Position);
    bool snapped = false;
    if (!_grid.IsInBounds(playerGrid) || !_grid.IsWalkable(playerGrid))
    {
        if (!TrySnapToNearestWalkable(player.Position, playerGrid, out playerGrid))
        {
            return TargetDecision.None(CoordinationTerminationReason.PlayerOnBlockedCell, 0, Array.Empty<string>(), EmptyDiag);
        }
        snapped = true;
    }

    // Step 3: Fetch Snapshot, Filter Valid Candidates & Validate Goal Cells
    var rawTokens = _tokenRepo.GetAvailable();
    var skipped = new List<string>();
    var diagnostics = new Dictionary<string, PathStatus>();
    var candidates = new List<TokenLocation>();
    var seenIds = new HashSet<string>(StringComparer.Ordinal);

    for (int i = 0; i < rawTokens.Count; i++)
    {
        var t = rawTokens[i];
        if (t == null || t.IsCollected) continue;
        if (!float.IsFinite(t.Position.X) || !float.IsFinite(t.Position.Y) || !float.IsFinite(t.Position.Z)) continue;
        if (string.IsNullOrEmpty(t.Id) || !seenIds.Add(t.Id)) continue;

        var goalGrid = _grid.WorldToGrid(t.Position);
        if (!_grid.IsInBounds(goalGrid) || !_grid.IsWalkable(goalGrid))
        {
            diagnostics[t.Id] = PathStatus.InvalidGoal;
            skipped.Add(t.Id);
            continue;
        }

        candidates.Add(t);
    }

    if (candidates.Count == 0)
        return TargetDecision.None(CoordinationTerminationReason.NoValidCandidates, 0, skipped, diagnostics, snapped);

    int searchesRun = 0;
    int maxExpansions = _grid.GridWidth * _grid.GridHeight;
    TargetEvaluationResult? bestEval = null;
    PathResult? bestPath = null;
    TokenLocation? bestToken = null;
    bool isCurrentTargetRetainedInsideCommit = false;

    // Step 4: Evaluate Commit Radius Stability
    TokenLocation? currentToken = null;
    if (!string.IsNullOrEmpty(currentTargetId))
    {
        currentToken = candidates.FirstOrDefault(c => c.Id == currentTargetId);
        if (currentToken != null)
        {
            float dist = Vec3.Distance2D(player.Position, currentToken.Position);
            if (dist <= config.CommitRadius)
            {
                searchesRun++;
                var directPath = _pathfinder.FindPath(player.Position, currentToken.Position, maxExpansions);
                diagnostics[currentToken.Id] = directPath.Status;

                if (directPath.Status == PathStatus.Success)
                {
                    float pathCostWorld = ComputeWorldPathCost(player.Position, currentToken.Position, directPath.GridCost, snapped, playerGrid, _grid.WorldToGrid(currentToken.Position));
                    var currentEval = _ranker.RankCandidates(
                        new[] { currentToken }, player, config, currentTargetId,
                        new Dictionary<string, float> { [currentToken.Id] = pathCostWorld },
                        lineCostPerMeter: _grid.MinTraversalCost)[0];

                    bestToken = currentToken;
                    bestPath = directPath;
                    bestEval = currentEval;
                    isCurrentTargetRetainedInsideCommit = true;

                    // If CommitOverrideMargin is +Infinity, retain unconditionally after successful reachability
                    if (float.IsPositiveInfinity(config.CommitOverrideMargin))
                    {
                        return new TargetDecision(currentToken, directPath, currentEval,
                            CoordinationTerminationReason.OptimalFound, searchesRun, skipped, diagnostics, snapped);
                    }
                }
                else
                {
                    skipped.Add(currentToken.Id);
                }
            }
        }
    }

    // Step 5: Straight-Line Upper Bound Evaluation
    var upperBounds = _ranker.RankCandidates(
        candidates, player, config, currentTargetId,
        verifiedPathCosts: null,
        lineCostPerMeter: _grid.MinTraversalCost);

    var sortedCandidates = upperBounds.OrderByDescending(u => u.Score)
                                      .ThenBy(u => u.Token.Id, StringComparer.Ordinal)
                                      .ToList();

    // Step 6: Branch-and-Bound Evaluation Loop
    bool searchBudgetExceededOccurred = false;
    bool stoppedByPruning = false;

    for (int i = 0; i < sortedCandidates.Count; i++)
    {
        var candidateEval = sortedCandidates[i];

        // Skip if already evaluated during commit-radius check
        if (bestToken != null && candidateEval.Token.Id == bestToken.Id)
            continue;

        // Effective threshold to beat
        double effectiveThreshold = isCurrentTargetRetainedInsideCommit
            ? bestEval!.Score + config.CommitOverrideMargin
            : (bestEval != null ? bestEval.Score : double.NegativeInfinity);

        // Rigorous Slack-Adjusted Pruning Rule (UpperBoundSlack = 1e-6)
        if (bestEval != null)
        {
            double adjustedUpper = candidateEval.Score + RankingConfig.UpperBoundSlack;
            if (adjustedUpper < effectiveThreshold ||
               (Math.Abs(adjustedUpper - effectiveThreshold) <= 1e-9 && isCurrentTargetRetainedInsideCommit) ||
               (Math.Abs(adjustedUpper - effectiveThreshold) <= 1e-9 && !isCurrentTargetRetainedInsideCommit && StringComparer.Ordinal.Compare(candidateEval.Token.Id, bestToken!.Id) > 0))
            {
                stoppedByPruning = true;
                break;
            }
        }

        // Search Count Budget Check
        if (searchesRun >= config.MaxPathSearches)
        {
            break;
        }

        // Execute Path Search
        searchesRun++;
        var pathResult = _pathfinder.FindPath(player.Position, candidateEval.Token.Position, maxExpansions);
        diagnostics[candidateEval.Token.Id] = pathResult.Status;

        if (pathResult.Status == PathStatus.Success)
        {
            float verifiedWorldCost = ComputeWorldPathCost(player.Position, candidateEval.Token.Position, pathResult.GridCost, snapped, playerGrid, _grid.WorldToGrid(candidateEval.Token.Position));
            var verifiedEval = _ranker.RankCandidates(
                new[] { candidateEval.Token }, player, config, currentTargetId,
                new Dictionary<string, float> { [candidateEval.Token.Id] = verifiedWorldCost },
                lineCostPerMeter: _grid.MinTraversalCost)[0];

            if (bestEval == null)
            {
                bestEval = verifiedEval;
                bestPath = pathResult;
                bestToken = candidateEval.Token;
            }
            else if (isCurrentTargetRetainedInsideCommit)
            {
                // Challenger must beat threshold to override
                if (verifiedEval.Score >= effectiveThreshold)
                {
                    bestEval = verifiedEval;
                    bestPath = pathResult;
                    bestToken = candidateEval.Token;
                    isCurrentTargetRetainedInsideCommit = false; // Dethroned!
                }
            }
            else
            {
                if (verifiedEval.Score > bestEval.Score ||
                   (verifiedEval.Score == bestEval.Score && StringComparer.Ordinal.Compare(candidateEval.Token.Id, bestToken!.Id) < 0))
                {
                    bestEval = verifiedEval;
                    bestPath = pathResult;
                    bestToken = candidateEval.Token;
                }
            }
        }
        else
        {
            if (pathResult.Status == PathStatus.SearchBudgetExceeded)
            {
                double adjustedUpper = candidateEval.Score + RankingConfig.UpperBoundSlack;
                double currentBest = bestEval != null ? bestEval.Score : double.NegativeInfinity;
                if (adjustedUpper >= currentBest)
                {
                    searchBudgetExceededOccurred = true;
                }
            }

            skipped.Add(candidateEval.Token.Id);
        }
    }

    if (bestToken == null)
    {
        return TargetDecision.None(CoordinationTerminationReason.NoValidCandidates, searchesRun, skipped, diagnostics, snapped);
    }

    CoordinationTerminationReason finalReason;
    if (stoppedByPruning)
    {
        finalReason = searchBudgetExceededOccurred
            ? CoordinationTerminationReason.SearchBudgetReached
            : CoordinationTerminationReason.OptimalFound;
    }
    else if (searchesRun >= config.MaxPathSearches && skipped.Count + (bestToken != null ? 1 : 0) < candidates.Count)
    {
        finalReason = CoordinationTerminationReason.SearchBudgetReached;
    }
    else
    {
        finalReason = searchBudgetExceededOccurred
            ? CoordinationTerminationReason.SearchBudgetReached
            : CoordinationTerminationReason.AllCandidatesEvaluated;
    }

    return new TargetDecision(bestToken, bestPath, bestEval, finalReason, searchesRun, skipped, diagnostics, snapped);
}
```

---

## 6. Comprehensive Test Matrix

### 6.1 Pathfinder Tests (`Assistant.Tests.Pathfinding`)
| Test ID | Category | Scenario & Assertion |
| :--- | :--- | :--- |
| **A-01** | Basic | Straight-line path on clear grid matches Manhattan / Euclidean grid distance. |
| **A-02** | Obstacle | Single-wall obstacle navigation correctly routes around the barrier. |
| **A-03** | No-Corner-Cut | Strict corner-cutting guard: diagonal through touching diagonal walls is rejected. |
| **A-04** | Terrain Cost | Rough terrain (cost 2.5) vs Walkable (1.0): path detours through longer walkable path when cheaper. |
| **A-05** | Budget Boundary | Large maze with `maxExpansions = 50` returns `SearchBudgetExceeded` with exact node count. |
| **A-06** | Budget Boundary Exact | Assert `maxExpansions = 1`: adjacent goal succeeds with 1 expansion; 2-step goal fails with `SearchBudgetExceeded` at 1 expansion (invariant: `NodesExpanded <= maxExpansions`). |
| **A-07** | Endpoint Validation | Out-of-bounds start $\rightarrow$ `InvalidStart`; obstacle goal $\rightarrow$ `InvalidGoal`; start == goal $\rightarrow$ `Success` (0 cost, 0 expansions). |
| **A-08** | Invariant / Property | Property Test on seeded random grids: for 100 random pairs, A* `GridCost` equals reference Dijkstra `GridCost` (8-way, no-corner-cut, relative tolerance $10^{-4}$) and status matches. |
| **A-09** | Admissibility | Property Test: for 500 random cell pairs on arbitrary maps, verify $h(u) \le c(u, v) + h(v)$ (triangle inequality / consistency). |
| **A-10** | Determinism & Reuse | Run identical search 100 times consecutively on same `AStarPathfinder` instance; assert bit-identical waypoints and grid cost. |
| **A-11** | Immutability Defensive Copy | Mutate the original `GridPosition[]` and `Vec3[]` arrays passed to `PathResult.Successful`; assert `PathResult.Steps` and `Waypoints` are completely unmodified. |
| **A-12** | NavGrid Contract | Contract test for `SimGridMap` implementing `INavGrid`: assert valid dimensions, finite cell size, min traversal cost, out-of-bounds infinity, and bidirectional coordinate conversion consistency. |

### 6.2 Ranker Tests (`Assistant.Tests.Ranking`)
| Test ID | Category | Scenario & Assertion |
| :--- | :--- | :--- |
| **R-01** | Value Dominance | High-value token vs low-value token at equal distance: high value ranks higher. |
| **R-02** | Time Dominance | Distant token vs adjacent token at equal value: adjacent ranks higher. |
| **R-03** | Rarity Scaling | Verify linear progression $0.0, 0.25, 0.50, 0.75, 1.0$ across rarity tiers. |
| **R-04** | Hysteresis Marginal | Challenger raw score is $+5\%$ higher, but fails to overcome $w_h \cdot 1.0$ bonus: current target retained. |
| **R-05** | Hysteresis Decisive | Challenger raw score is $+50\%$ higher, decisively overcoming hysteresis bonus: target switches. |
| **R-06** | Hysteresis Removal | Current target collected/removed: hysteresis bonus drops to 0; next best chosen immediately. |
| **R-07** | Ordinal Tie-Breaker | Exact score tie between tokens with IDs `"token_alpha"` and `"token_beta"`: `"token_alpha"` selected. |
| **R-08** | Invalid Inputs | Candidate with NaN coordinates, NaN estimated value, negative value, or non-finite speed handled cleanly without throwing. |
| **R-09** | Rounding Stability | Scores with tiny floating-point variance at $10^{-8}$ round to identical score at 6 decimal places. |
| **R-10** | Three Presets Scenario | Test with Phase 1 standard presets (high-value far, cheap close, rare medium) verifying deterministic ranking order. |
| **R-11** | Monotonicity Property | Assert score is non-increasing in `pathCostWorld`, non-decreasing in `EstimatedValue`, and non-decreasing in `Rarity` across 50 random candidates. |
| **R-12** | Architecture & Source Scan | Reflection check AND source file text scan verifying no file under `Assistant.Core/Ranking` references `DisplayMode`, `IDisplayModeManager`, `ITokenRepository`, `INavGrid`, or `IPathfinder`. |
| **R-13** | Config Validation | Thorough parameter bounds checking for `RankingConfig` constructor. |
| **R-14** | Deduplication & Collected | Collected tokens skipped; duplicate candidate IDs keep first occurrence. |

### 6.3 Coordinator Tests (`Assistant.Tests.Coordination`)
| Test ID | Category | Scenario & Assertion |
| :--- | :--- | :--- |
| **C-01** | Commit Radius Retain | Player within `CommitRadius` of current target: target retained even if a slightly better challenger exists. |
| **C-02** | Commit Radius Override Boundary | Challenger beats threshold exactly at, just below, and just above `Score(Current) + CommitOverrideMargin`; assert strict `>=` trigger. |
| **C-03** | Branch-and-Bound Early Pruning | Best path score beats remaining candidates' straight-line upper bounds: loop terminates early with `OptimalFound` and exact `PathSearchesExecuted`. |
| **C-04** | Pruning Tie Rule Both Directions | Equal upper bound: lower ID evaluated, higher ID pruned. |
| **C-05** | MaxPathSearches Budget Cap | Search stops at `MaxPathSearches` limit; termination is `SearchBudgetReached` only when an unpruned, unevaluated candidate remains, else `OptimalFound`. |
| **C-06** | Unreachable Fallback & Diagnostics | Top candidate is unreachable: logged in diagnostics, falls back to 2nd candidate; termination reflects remaining evaluations. |
| **C-07** | Candidate Budget Fallback | Top candidate exhausts expansions (`SearchBudgetExceeded`): logged in diagnostics, falls back to 2nd candidate; termination is `SearchBudgetReached`. |
| **C-08** | All Candidates Fail | All candidates fail pathfinding (`Unreachable` or `InvalidGoal`): returns `TargetDecision.None` with `NoValidCandidates` and complete diagnostics dictionary. |
| **C-09** | Player Snapped Walkable | Player starts on unwalkable cell with walkable neighbor in Ring 1: snaps start position, executes coordination, and reports `PlayerSnappedToWalkable == true`. |
| **C-10** | Player On Blocked Cell | Player surrounded by radius 2 obstacles: returns `TargetDecision.None` with `PlayerOnBlockedCell` without evaluating candidates. |
| **C-11** | Termination Reasons Coverage | Dedicated test suite covering each `CoordinationTerminationReason` under its exact trigger condition and precedence order. |
| **C-12** | Reachable-but-Long-Detour | Token behind a long wall loses to a farther token with a clear path (the core behavioral goal of the entire system). |
| **C-13** | Equivalence Property | On seeded random maps with `MaxPathSearches >= candidateCount`: branch-and-bound decision is 100% identical to exhaustive evaluation. |
| **C-14** | Geometry & Lower Bound Property | For random player/token positions (same cell, adjacent cells, near cell borders, snapped start) with REAL A*: assert `MinTraversalCost * Distance2D(p, t) <= pathCostWorld + 1e-6` and `lineScore + 1e-6 >= verifiedScore`. |
| **C-15** | Multi-Run Determinism | Run 100 repeated coordination passes with identical input state; assert identical decisions, paths, and diagnostics. |
| **C-16** | Phase 1 Preset Navigation | Phase 1 presets on clear grid (expected winner Charlie), and with wall making Charlie unreachable $\rightarrow$ Bravo selected. |
| **C-17** | Invalid Goal Validation | Token on non-walkable or out-of-bounds goal cell is skipped with `DiagnosticStatusByCandidateId[id] = PathStatus.InvalidGoal`. |
| **C-18** | Empty Repository & Zero Candidates | Empty repository returns `TargetDecision.None` with `NoValidCandidates`. |

---

## 7. Risk Register & Mitigations

| Risk | Impact | Severity | Mitigation Strategy |
| :--- | :--- | :---: | :--- |
| **1. Cyclic Layering Dependencies** | Architecture violation | High | `INavGrid`, `ITokenRepository`, `ITargetRanker`, and `IPathfinder` are Core abstractions. Constructor injection injects `INavGrid` into `AStarPathfinder` and coordinator. Sim implements `INavGrid`. |
| **2. Upper Bound Underestimation** | Pruning optimal target | Critical | World path cost includes endpoint offset metrics: $\text{pathCostWorld} \ge \text{MinTraversalCost} \cdot d_{2D}$. Proved via triangle inequality. Slack $\epsilon = 10^{-6}$ added for float safety. |
| **3. Array Reference Mutation** | Silent corruption of path steps | High | `PathResult.Successful` makes defensive `.Clone()` of `steps` and `waypoints` before wrapping in `Array.AsReadOnly`. Validated via unit test A-11. |
| **4. Off-by-One in A* Budget** | Flaky budget exhaustion | Medium | Strict order: pop $\rightarrow$ stale check $\rightarrow$ goal check $\rightarrow$ budget check ($\ge \text{maxExpansions}$) $\rightarrow$ increment $\rightarrow$ expand. Tested at $\text{maxExpansions} = 1$. Invariant: `NodesExpanded <= maxExpansions`. |
| **5. Player On Non-Walkable Cell** | Crash or InvalidStart abort | High | Deterministic Chebyshev ring snap (radius 1 & 2); terminal `PlayerOnBlockedCell` if unresolvable. |

---

## 8. Implementation Sequence (Stage 3.1–3.4)

### Step 0: Housekeeping (Commit separately prior to Stage 3.1)
- Fix surviving mutant #5: Update `FilterTokens_ExceedingCap_...` in `DisplayModeManagerTests` to assign distinct token positions so reversing distance order fails.
- Fix surviving mutant #8: Update `JsonConfigStoreTests` atomic-write test to hold the destination file open with `FileAccess.ReadWrite, FileShare.None` during save, asserting the existing file remains intact and no temporary `.tmp` file remains.
- Verify 100% tests pass and working tree is clean.

### Stage 3.1: Grid Abstraction & A* Pathfinding Engine
- Remove Phase 1 stub `IPathFinder.cs` and `Path.cs`.
- Implement `INavGrid` in `Assistant.Core.Abstractions`.
- Update `SimGridMap` in `Assistant.Sim` to implement `INavGrid`, exposing `MinTraversalCost = 1.0f`.
- Implement `PathStatus`, `PathResult` (with defensive copies), and `AStarPathfinder` (with constructor injection of `INavGrid`).
- Add tests `A-01` through `A-12` including Dijkstra equivalence, immutability, and boundary tests.

### Stage 3.2: Deterministic Target Ranker
- Implement `RankingConfig`, `TargetEvaluationResult`, and `DefaultTargetRanker` in `Assistant.Core.Ranking`.
- Implement normalized scoring formula, hysteresis, sanitized inputs (NaN handling), 6-decimal rounding, and ordinal tie-breaking.
- Add tests `R-01` through `R-10`.

### Stage 3.3: Route & Target Coordinator
- Implement `TargetDecision`, `CoordinationTerminationReason`, and `RouteCoordinator` in `Assistant.Core.Coordination`.
- Implement branch-and-bound candidate evaluation, start snapping, commit radius retention/override, budget fallback, and diagnostics.
- Add tests `C-01` through `C-10`.

### Stage 3.4: Simulation Harness & End-to-End Verification
- Integrate `RouteCoordinator` into `Assistant.Runner` and simulation test harness.
- Confirm 0 build errors, 0 warnings, 100% passing tests, and zero real-game dependencies.

---

## 9. Stage 3.4 Simulator Harness & End-to-End Execution Specification

### 9.1 Simulator Architecture & Strict Boundaries
1. **Simulator-Only Boundary**:
   The entire simulation loop, mock player movement, physics approximation, and path following exist solely within `Assistant.Sim` (`SimulationHarness`, `SimClock`, `SimPlayer`). Core components (`RouteCoordinator`, `AStarPathfinder`, `DefaultTargetRanker`, `TokenDiscoveryService`) remain strictly pure, state-insulated, and real-game free.
2. **Deterministic Time (`SimClock : IClock`)**:
   Time advancement is owned exclusively by `SimulationHarness.Run()` via `_clock.Advance(dt)`. No ambient wall-clock (`DateTime.UtcNow`) is queried during simulation ticks.
3. **Multi-Targeting Compatibility**:
   `Assistant.Sim` targets `netstandard2.1` and `net10.0` simultaneously. All simulation APIs remain compliant with both targets.

### 9.2 Simulation Loop & Replan Cadence Invariants
1. **Fixed Step Size**:
   Simulation steps advance at fixed $\Delta t = 0.1\text{ s}$.
2. **Replan Scheduling Invariants**:
   - Step 0 initial replan ($t = 0.0\text{ s}$).
   - Periodic cadence replan every $1.0\text{ s}$ ($\Delta t_{\text{replan}} \ge 1.0\text{ s}$).
   - Event-driven replanning:
     * Current target collected (`TokenCollected`).
     * Current target removed or expired from repository (`TokensExpired` / TTL).
     * Navigation map changed (`MapChanged`).
     * Active path blocked by dynamic terrain alterations.
   - **Invariant**: The coordinator is **never** invoked every frame. Total replan invocations over duration $T$ are strictly bounded by $\lfloor T / 1.0 \rfloor + 1 + N_{\text{events}}$.

### 9.3 Path Following & Waypoint Semantics
1. **Waypoint Invariant**:
   When following `TargetDecision.SelectedPath.Waypoints`:
   - Keep the first waypoint (`Waypoints[0]`, corresponding to the snapped start cell center).
   - Intermediate waypoints follow grid cell centers.
   - Replace the last waypoint (`Waypoints[^1]`) with the exact continuous token position $p_{\text{token}}$.
2. **Token Collection**:
   Collection is triggered when 2D Euclidean distance $d_{2D}(p_{\text{player}}, p_{\text{token}}) \le \text{CollectRadius}$ (default $1.0\text{ m}$).
   On collection, `SimGameApi.CollectToken(id)` is called, raising `TokenCollected` and marking the token collected in `TokenRepository`.

### 9.4 Event Transcript & Metrics Contract
1. **Transcript Event Types**:
   - `TargetSelected`: Logged on initial target assignment or after collection/invalidation.
   - `TargetChanged`: Logged when an incumbent is dethroned by a challenger exceeding hysteresis + commit margin.
   - `TokenCollected`: Logged upon successful pickup.
   - `MapChanged`: Logged on dynamic map invalidation events.
   - `Replan`: Logged on every replan cycle.
2. **Stability (No-Chatter Guarantee)**:
   In static environments, `TargetChanged` is never fired between selection and collection of an incumbent target.

### 9.5 Baseline Strategy Comparison
The harness provides a `NearestFirst` straight-line distance baseline. Comparative benchmark runs over fixed seeds demonstrate that the `RankedCoordinator` systematically outperforms the baseline in total BRIL value by optimizing for value, rarity, and traversal costs over greedy proximity.

### 9.6 Harness Regression Test Suite (H-01 through H-11)
- `H-01`: Determinism — identical seed produces identical transcript events.
- `H-02`: Divergence — different seeds produce differing transcripts.
- `H-03`: Golden Baseline — seed 12345 matches committed golden transcript.
- `H-04`: No Chatter — static tokens only change target upon collection or invalidation.
- `H-05`: Dynamic Rerouting — obstacle across route triggers `MapChanged`, replanning, obstacle avoidance, and successful collection without entering blocked cells.
- `H-06`: Walkability Invariant — player cell is verified walkable at every tick.
- `H-07`: Deterministic Order — small hand-derived scenario achieves exact sequence and total value.
- `H-08`: Unreachable Isolation — walled-in token is never targeted, ending cleanly with `NoValidCandidates`.
- `H-09`: Single-Collection Invariant — `TokenCollected` raised exactly once per token through pipeline.
- `H-10`: Replan Frequency Bound — total replan calls do not exceed cadence plus event count.
- `H-11`: Coverage-Based TTL Lifecycle — uncovered tokens survive past TTL; silently despawned covered tokens are evicted.
