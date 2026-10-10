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
            if (weightValue + weightTime + weightRarity + weightHysteresis <= 0)
                throw new ArgumentException("At least one ranking weight must be strictly positive");

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

    public interface ITargetRanker
    {
        IReadOnlyList<TargetEvaluationResult> RankCandidates(
            IReadOnlyList<TokenLocation> candidates,
            PlayerState player,
            RankingConfig config,
            string? currentTargetId,
            IReadOnlyDictionary<string, float>? verifiedPathCosts = null);
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
            RankingConfig config,
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
   Because the player starts at world coordinate $p$ (inside $c_{\text{start}}$) and the token is located at world coordinate $t$ (inside $c_{\text{goal}}$), the complete world traversal cost must account for endpoint offsets:
   $$\text{pathCostWorld} = \text{GridCost} + \text{MinTraversalCost} \cdot \left( d_{2D}(p, \text{center}(c_{\text{start}})) + d_{2D}(\text{center}(c_{\text{goal}}), t) \right)$$
   *Snapping Rule*: If the player was snapped to a walkable cell $c_{\text{snapped}}$, the navigation search start cell is $c_{\text{start}} = c_{\text{snapped}}$, and $p$ used in the endpoint connector distance calculation is defined as $\text{center}(c_{\text{snapped}})$ itself (so $d_{2D}(p, \text{center}(c_{\text{start}})) = 0$).
   *Ranker Contract*: The ranker's `verifiedPathCosts` dictionary maps `token.Id` $\rightarrow \text{pathCostWorld}$.

4. **Rigorous Proof that Stage Cost Lower Bound Holds**:
   By the Euclidean triangle inequality:
   $$d_{2D}(p, t) \le d_{2D}(p, \text{center}(c_{\text{start}})) + \|\text{center}(c_{\text{start}}) - \text{center}(c_{\text{goal}})\|_2 + d_{2D}(\text{center}(c_{\text{goal}}), t)$$
   Multiplying through by $\text{MinTraversalCost} > 0$:
   $$\text{MinTraversalCost} \cdot d_{2D}(p, t) \le \text{MinTraversalCost} \cdot d_{2D}(p, \text{center}(c_{\text{start}})) + \text{MinTraversalCost} \cdot \|\text{center}(c_{\text{start}}) - \text{center}(c_{\text{goal}})\|_2 + \text{MinTraversalCost} \cdot d_{2D}(\text{center}(c_{\text{goal}}), t)$$
   Substituting the inequality for $\text{GridCost}$:
   $$\text{MinTraversalCost} \cdot d_{2D}(p, t) \le \text{pathCostWorld}$$
   **Theorem**: The quantity $C_{\text{lower}}(p, t) = \text{MinTraversalCost} \cdot d_{2D}(p, t)$ is a mathematically proven, valid lower bound on $\text{pathCostWorld}$ under all arbitrary continuous world positions.

### 3.2 Monotonic Scoring Upper Bound

1. **Travel Time Lower Bound**:
   $$v_{\text{eff}} = \begin{cases} \text{player.Speed}, & \text{if } \text{float.IsFinite(player.Speed)} \text{ and } \text{player.Speed} > 0 \\ \text{config.DefaultSpeed}, & \text{otherwise} \end{cases}$$
   $$T_{\text{sec, min}} = \frac{C_{\text{lower}}(p, t)}{v_{\text{eff}}} = \frac{\text{MinTraversalCost} \cdot d_{2D}(p, t)}{v_{\text{eff}}}$$
   $$T_{\text{sec, path}} = \frac{\text{pathCostWorld}}{v_{\text{eff}}}$$
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
   Rounding using `Math.Round(score, 6, MidpointRounding.AwayFromZero)` preserves monotonicity:
   $$\text{Score}_{\text{upperBound}} \ge \text{Score}_{\text{pathVerified}}$$

### 3.3 Coherent Score Definition, Rounding & Pruning Tie Rule

1. **Single Coherent Score Definition**:
   All sorting, ranking, best-candidate tracking, commit radius overrides, and pruning decisions evaluate the **rounded score** ($\text{Math.Round}(..., 6)$) in `double` precision.
2. **Slack-Adjusted Pruning Invariant (`UpperBoundSlack = 1e-6`)**:
   A candidate $C$ with straight-line upper bound $\text{Score}_{\text{upperBound}}(C)$ can be safely pruned against the currently verified best candidate $B$ (with score $\text{Score}_{\text{verified}}(B)$) **if and only if**:
   $$\text{Score}_{\text{upperBound}}(C) + \text{UpperBoundSlack} < \text{Score}_{\text{verified}}(B)$$
   $$\textbf{OR}$$
   $$|\left(\text{Score}_{\text{upperBound}}(C) + \text{UpperBoundSlack}\right) - \text{Score}_{\text{verified}}(B)| \le 10^{-9} \quad \text{AND} \quad \text{StringComparer.Ordinal.Compare}(C.\text{Id}, B.\text{Id}) > 0$$
   *Justification*: The slack $\epsilon = 10^{-6}$ prevents floating-point inaccuracy from ever pruning a candidate that could match or marginally beat the best verified candidate. If the slack-adjusted upper bound ties the best score, candidate $C$ is pruned if and only if its ID is lexicographically greater than $B$'s ID.

### 3.4 CommitRadius Semantics & Replacement Threshold

1. **Stability Policy**:
   `CommitRadius` is explicitly a **spatial anti-jitter and path-stability policy**, not a guarantee of global score optimality.
2. **Retention vs Replacement Policy**:
   When `currentTargetId` is set, available, reachable, and within $d_{2D} \le \text{CommitRadius}$:
   - **Default Retention**: The current target is retained to prevent oscillation near collection.
   - **Effective Switch Threshold**: Because the current target receives the hysteresis bonus ($w_h \cdot 1.0 = 0.10$ with defaults) while challenger $C$ does not, challenger $C$ must beat current target's base evaluation by an effective switch threshold inside `CommitRadius`:
     $$\text{EffectiveSwitchThreshold} = \text{CommitOverrideMargin} + \text{HysteresisBonus} \quad (0.30 + 0.10 = 0.40 \text{ with defaults})$$
     In terms of evaluated total scores:
     $$\text{Score}_{\text{verified}}(C) \ge \text{Score}_{\text{verified}}(\text{Current}) + \text{config.CommitOverrideMargin}$$
   - **Challenger Search Requirement**: Evaluating challengers is required unless `config.CommitOverrideMargin == float.PositiveInfinity`. If `CommitOverrideMargin` is $+\infty$, the current target is retained unconditionally after verifying reachability with a single search.
   - **Validation**: `CommitOverrideMargin` must be $\ge 0$ (rejecting `NaN`, negative values, and $-\infty$); $+\infty$ is permitted exclusively for `CommitOverrideMargin` to denote unconditional retention.
   - If the current target is unreachable, invalid, or collected, commitment is voided and normal branch-and-bound applies immediately.

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
    if (!float.IsFinite(player.Position.X) || !float.IsFinite(player.Position.Z))
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

    // Step 3: Fetch Snapshot and Filter
    var rawTokens = _tokenRepo.GetAvailable();
    var candidates = FilterValidCandidates(rawTokens);
    if (candidates.Count == 0)
        return TargetDecision.None(CoordinationTerminationReason.NoValidCandidates, 0, Array.Empty<string>(), EmptyDiag, snapped);

    // Step 4: Evaluate Commit Radius Stability
    if (!string.IsNullOrEmpty(currentTargetId))
    {
        var currentToken = candidates.FirstOrDefault(c => c.Id == currentTargetId);
        if (currentToken != null)
        {
            float dist = Vec3.Distance2D(player.Position, currentToken.Position);
            if (dist <= config.CommitRadius)
            {
                var directPath = _pathfinder.FindPath(player.Position, currentToken.Position);
                if (directPath.Status == PathStatus.Success)
                {
                    float pathCostWorld = ComputeWorldPathCost(player.Position, currentToken.Position, directPath.GridCost, snapped, playerGrid, _grid.WorldToGrid(currentToken.Position));
                    var currentEval = _ranker.RankCandidates(
                        new[] { currentToken }, player, config, currentTargetId,
                        new Dictionary<string, float> { [currentToken.Id] = pathCostWorld })[0];

                    // If CommitOverrideMargin is +Infinity, retain unconditionally after successful reachability
                    if (float.IsPositiveInfinity(config.CommitOverrideMargin))
                    {
                        return new TargetDecision(currentToken, directPath, currentEval,
                            CoordinationTerminationReason.OptimalFound, 1, Array.Empty<string>(), EmptyDiag, snapped);
                    }

                    // Check if an exceptional challenger overrides inside commit radius
                    var bestChallenger = FindOverrideChallenger(candidates, player, config, currentTargetId, currentEval.Score);
                    if (bestChallenger == null)
                    {
                        return new TargetDecision(currentToken, directPath, currentEval,
                            CoordinationTerminationReason.OptimalFound, 1, Array.Empty<string>(), EmptyDiag, snapped);
                    }
                }
            }
        }
    }

    // Step 5: Straight-Line Upper Bound Evaluation
    var upperBounds = _ranker.RankCandidates(candidates, player, config, currentTargetId, verifiedPathCosts: null);
    var sortedCandidates = upperBounds.OrderByDescending(u => u.Score)
                                      .ThenBy(u => u.Token.Id, StringComparer.Ordinal)
                                      .ToList();

    // Step 6: Branch-and-Bound Evaluation Loop
    TargetEvaluationResult? bestEval = null;
    PathResult? bestPath = null;
    TokenLocation? bestToken = null;
    int searchesRun = 0;
    bool searchBudgetExceededOccurred = false;
    var skipped = new List<string>();
    var diagnostics = new Dictionary<string, PathStatus>();

    for (int i = 0; i < sortedCandidates.Count; i++)
    {
        var candidateEval = sortedCandidates[i];

        // Rigorous Slack-Adjusted Pruning Rule (UpperBoundSlack = 1e-6)
        if (bestEval != null)
        {
            double adjustedUpper = candidateEval.Score + RankingConfig.UpperBoundSlack;
            if (adjustedUpper < bestEval.Score ||
               (Math.Abs(adjustedUpper - bestEval.Score) <= 1e-9 && StringComparer.Ordinal.Compare(candidateEval.Token.Id, bestToken!.Id) > 0))
            {
                var termination = searchBudgetExceededOccurred 
                    ? CoordinationTerminationReason.SearchBudgetReached 
                    : CoordinationTerminationReason.OptimalFound;

                return new TargetDecision(bestToken, bestPath, bestEval, termination, searchesRun, skipped, diagnostics, snapped);
            }
        }

        // Search Count Budget Check
        if (searchesRun >= config.MaxPathSearches)
        {
            return new TargetDecision(bestToken, bestPath, bestEval,
                bestEval != null ? CoordinationTerminationReason.SearchBudgetReached : CoordinationTerminationReason.NoValidCandidates,
                searchesRun, skipped, diagnostics, snapped);
        }

        // Execute Path Search
        searchesRun++;
        var pathResult = _pathfinder.FindPath(player.Position, candidateEval.Token.Position);
        diagnostics[candidateEval.Token.Id] = pathResult.Status;

        if (pathResult.Status == PathStatus.Success)
        {
            float verifiedWorldCost = ComputeWorldPathCost(player.Position, candidateEval.Token.Position, pathResult.GridCost, snapped, playerGrid, _grid.WorldToGrid(candidateEval.Token.Position));
            var verifiedEval = _ranker.RankCandidates(
                new[] { candidateEval.Token }, player, config, currentTargetId,
                new Dictionary<string, float> { [candidateEval.Token.Id] = verifiedWorldCost })[0];

            if (bestEval == null || verifiedEval.Score > bestEval.Score ||
               (verifiedEval.Score == bestEval.Score && StringComparer.Ordinal.Compare(candidateEval.Token.Id, bestToken!.Id) < 0))
            {
                bestEval = verifiedEval;
                bestPath = pathResult;
                bestToken = candidateEval.Token;
            }
        }
        else
        {
            if (pathResult.Status == PathStatus.SearchBudgetExceeded)
                searchBudgetExceededOccurred = true;

            skipped.Add(candidateEval.Token.Id);
        }
    }

    var finalReason = bestToken != null
        ? (searchBudgetExceededOccurred ? CoordinationTerminationReason.SearchBudgetReached : CoordinationTerminationReason.AllCandidatesEvaluated)
        : CoordinationTerminationReason.NoValidCandidates;

    return bestToken != null
        ? new TargetDecision(bestToken, bestPath, bestEval, finalReason, searchesRun, skipped, diagnostics, snapped)
        : TargetDecision.None(CoordinationTerminationReason.NoValidCandidates, searchesRun, skipped, diagnostics, snapped);
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

### 6.3 Coordinator Tests (`Assistant.Tests.Coordination`)
| Test ID | Category | Scenario & Assertion |
| :--- | :--- | :--- |
| **C-01** | Commit Radius Retain | Player within `CommitRadius` of current target: target retained even if a slightly better candidate exists. |
| **C-02** | Commit Radius Override | Player within `CommitRadius`, but an exceptional candidate beats current target by $\ge \text{CommitOverrideMargin}$: target switches to challenger. |
| **C-03** | Branch-and-Bound Pruning | Best path score beats remaining candidates' straight-line upper bounds: loop terminates early with `OptimalFound`. |
| **C-04** | Pruning Tie-Breaker | Candidate with upper bound equal to best verified score is evaluated if its ID is lexicographically smaller, but pruned if its ID is greater. |
| **C-05** | Equivalence Property | On seeded random maps with `MaxPathSearches >= candidateCount`: branch-and-bound decision is 100% identical to exhaustive full-evaluation decision. |
| **C-06** | Budget Exhaustion | Top 8 candidates all require heavy path searches: search stops at 8 with `SearchBudgetReached`. |
| **C-07** | Candidate Budget Fallback | Top candidate search exhausts expansion budget: coordinator logs `SearchBudgetExceeded` in diagnostics and falls back to 2nd candidate; termination is `SearchBudgetReached`. |
| **C-08** | Player Snapped Walkable | Player starts on unwalkable cell with walkable cell in Ring 1: snaps start position, executes coordination, and reports `PlayerSnappedToWalkable == true`. |
| **C-09** | Player On Blocked Cell | Player surrounded by radius 2 obstacles: returns `TargetDecision.None` with `PlayerOnBlockedCell` without evaluating candidates. |
| **C-10** | Multi-Run Determinism | Run 100 repeated coordination passes with identical input state; assert identical decisions, paths, and diagnostics. |
| **C-11** | Scoring Upper Bound Property | Seeded property test: for 500 random player/token positions (same cell, adjacent cells, near cell borders, snapped start) assert straight-line upper bound score $+ \text{UpperBoundSlack} \ge \text{Score}_{\text{verified}}$. |
| **C-12** | Termination Reasons Coverage | Dedicated isolated tests asserting exact condition + precedence for each termination reason (`OptimalFound`, `AllCandidatesEvaluated`, `SearchBudgetReached`, `NoValidCandidates`, `PlayerOnBlockedCell`). |
| **C-13** | Commit Radius Override Margin Boundary | Boundary test at exactly the override threshold: challenger with raw score advantage matching `CommitOverrideMargin + HysteresisBonus` exactly triggers/does not trigger according to strict `>=` rule. |

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
