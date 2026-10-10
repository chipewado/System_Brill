using System;
using System.Collections.Generic;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Core.Pathfinding
{
    /// <summary>
    /// Computes 8-way navigation paths with strict no-corner-cutting on an <see cref="INavGrid"/>.
    /// Uses generation-stamped reusable internal arrays for high-throughput single-threaded allocations.
    /// NOTE: AStarPathfinder is not thread-safe; instantiate per thread or synchronize external calls.
    /// </summary>
    public sealed class AStarPathfinder : IPathfinder
    {
        private readonly INavGrid _grid;

        // Reusable internal buffers with generation stamping for O(1) resets
        private int[] _seenGeneration = Array.Empty<int>();
        private int[] _closedGeneration = Array.Empty<int>();
        private float[] _gScore = Array.Empty<float>();
        private int[] _cameFrom = Array.Empty<int>();
        private int _currentGeneration;
        private int _lastWidth;
        private int _lastHeight;

        private readonly MinHeap _openQueue = new MinHeap(256);

        private struct NeighborDir
        {
            public int Dx;
            public int Dz;
            public float StepFactor;
            public bool IsDiagonal;
        }

        // Fixed sequence: N, NE, E, SE, S, SW, W, NW
        private static readonly NeighborDir[] NeighborDirections = new NeighborDir[]
        {
            new NeighborDir { Dx = 0,  Dz = 1,  StepFactor = 1.0f,               IsDiagonal = false }, // N
            new NeighborDir { Dx = 1,  Dz = 1,  StepFactor = (float)Math.Sqrt(2), IsDiagonal = true },  // NE
            new NeighborDir { Dx = 1,  Dz = 0,  StepFactor = 1.0f,               IsDiagonal = false }, // E
            new NeighborDir { Dx = 1,  Dz = -1, StepFactor = (float)Math.Sqrt(2), IsDiagonal = true },  // SE
            new NeighborDir { Dx = 0,  Dz = -1, StepFactor = 1.0f,               IsDiagonal = false }, // S
            new NeighborDir { Dx = -1, Dz = -1, StepFactor = (float)Math.Sqrt(2), IsDiagonal = true },  // SW
            new NeighborDir { Dx = -1, Dz = 0,  StepFactor = 1.0f,               IsDiagonal = false }, // W
            new NeighborDir { Dx = -1, Dz = 1,  StepFactor = (float)Math.Sqrt(2), IsDiagonal = true }   // NW
        };

        public AStarPathfinder(INavGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        public PathResult FindPath(Vec3 startWorld, Vec3 goalWorld, int maxExpansions = 10000)
        {
            var startGrid = _grid.WorldToGrid(startWorld);
            var goalGrid = _grid.WorldToGrid(goalWorld);
            return FindPath(startGrid, goalGrid, maxExpansions);
        }

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

            // Step 3: Priority Queue & Buffer Initialization
            int totalNodes = _grid.GridWidth * _grid.GridHeight;
            EnsureBuffers(totalNodes);
            AdvanceGeneration();
            _openQueue.Clear();

            int nodesExpanded = 0;
            long sequenceCounter = 0;

            int startIndex = GetIndex(start);
            int goalIndex = GetIndex(goal);

            SetGScore(startIndex, 0f, -1);
            float hStart = ComputeOctileHeuristic(start, goal);
            _openQueue.Enqueue(new HeapNode
            {
                NodeIndex = startIndex,
                F = hStart,
                G = 0f,
                Sequence = ++sequenceCounter
            });

            // Step 4: Search Loop
            while (_openQueue.Count > 0)
            {
                var current = _openQueue.Dequeue();

                // 4a: Stale Entry Check (already closed via a cheaper path)
                if (IsClosed(current.NodeIndex))
                    continue;

                // 4b: Goal Detection (BEFORE Budget Increment)
                if (current.NodeIndex == goalIndex)
                {
                    return ReconstructPath(startIndex, goalIndex, nodesExpanded);
                }

                // 4c: Budget Accounting & Exhaustion Check (BEFORE incrementing and expanding)
                if (nodesExpanded >= maxExpansions)
                {
                    return PathResult.Failure(PathStatus.SearchBudgetExceeded, nodesExpanded);
                }
                nodesExpanded++;

                // 4d: Mark Closed
                SetClosed(current.NodeIndex);

                // 4e: Expand 8 Neighbors in Fixed Sequence
                var currentPos = GetPosition(current.NodeIndex);
                float currentG = current.G;

                for (int i = 0; i < NeighborDirections.Length; i++)
                {
                    var dir = NeighborDirections[i];
                    var neighborPos = new GridPosition(currentPos.X + dir.Dx, currentPos.Z + dir.Dz);

                    if (!_grid.IsInBounds(neighborPos) || !_grid.IsWalkable(neighborPos))
                        continue;

                    // Strict No-Corner-Cutting Check for diagonal moves
                    if (dir.IsDiagonal)
                    {
                        var card1 = new GridPosition(currentPos.X + dir.Dx, currentPos.Z);
                        var card2 = new GridPosition(currentPos.X, currentPos.Z + dir.Dz);
                        if (!_grid.IsWalkable(card1) || !_grid.IsWalkable(card2))
                            continue;
                    }

                    int neighborIndex = GetIndex(neighborPos);
                    if (IsClosed(neighborIndex))
                        continue;

                    float stepCost = _grid.CellSize * dir.StepFactor * _grid.GetTraversalCost(neighborPos);
                    float tentativeG = currentG + stepCost;

                    if (tentativeG < GetGScore(neighborIndex))
                    {
                        SetGScore(neighborIndex, tentativeG, current.NodeIndex);
                        float h = ComputeOctileHeuristic(neighborPos, goal);
                        float f = tentativeG + h;
                        _openQueue.Enqueue(new HeapNode
                        {
                            NodeIndex = neighborIndex,
                            F = f,
                            G = tentativeG,
                            Sequence = ++sequenceCounter
                        });
                    }
                }
            }

            // Step 5: Queue Exhaustion without reaching goal
            return PathResult.Failure(PathStatus.Unreachable, nodesExpanded);
        }

        private float ComputeOctileHeuristic(GridPosition from, GridPosition to)
        {
            int dx = Math.Abs(from.X - to.X);
            int dz = Math.Abs(from.Z - to.Z);
            int minD = Math.Min(dx, dz);
            int maxD = Math.Max(dx, dz);
            float octile = ((float)Math.Sqrt(2) - 1.0f) * minD + maxD;
            return _grid.CellSize * _grid.MinTraversalCost * octile;
        }

        private PathResult ReconstructPath(int startIndex, int goalIndex, int nodesExpanded)
        {
            float totalGridCost = _gScore[goalIndex];
            var pathList = new List<GridPosition>();
            int curr = goalIndex;
            while (curr != -1)
            {
                pathList.Add(GetPosition(curr));
                if (curr == startIndex) break;
                curr = _cameFrom[curr];
            }
            pathList.Reverse();

            var steps = pathList.ToArray();
            var waypoints = new Vec3[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                waypoints[i] = _grid.GridToWorld(steps[i]);
            }

            return PathResult.Successful(steps, waypoints, totalGridCost, nodesExpanded);
        }

        private int GetIndex(GridPosition pos) => pos.Z * _grid.GridWidth + pos.X;
        private GridPosition GetPosition(int index) => new GridPosition(index % _grid.GridWidth, index / _grid.GridWidth);

        private bool IsClosed(int nodeIndex) => _closedGeneration[nodeIndex] == _currentGeneration;
        private void SetClosed(int nodeIndex) => _closedGeneration[nodeIndex] = _currentGeneration;

        private float GetGScore(int nodeIndex) =>
            _seenGeneration[nodeIndex] == _currentGeneration ? _gScore[nodeIndex] : float.PositiveInfinity;

        private void SetGScore(int nodeIndex, float g, int parent)
        {
            _seenGeneration[nodeIndex] = _currentGeneration;
            _gScore[nodeIndex] = g;
            _cameFrom[nodeIndex] = parent;
        }

        private void EnsureBuffers(int capacity)
        {
            if (_seenGeneration.Length != capacity || _lastWidth != _grid.GridWidth || _lastHeight != _grid.GridHeight)
            {
                _seenGeneration = new int[capacity];
                _closedGeneration = new int[capacity];
                _gScore = new float[capacity];
                _cameFrom = new int[capacity];
                _currentGeneration = 0;
                _lastWidth = _grid.GridWidth;
                _lastHeight = _grid.GridHeight;
            }
        }

        private void AdvanceGeneration()
        {
            if (_currentGeneration == int.MaxValue)
            {
                Array.Clear(_seenGeneration, 0, _seenGeneration.Length);
                Array.Clear(_closedGeneration, 0, _closedGeneration.Length);
                _currentGeneration = 0;
            }
            _currentGeneration++;
        }

        internal void SetGenerationForTesting(int generation) => _currentGeneration = generation;
        internal int CurrentGenerationForTesting => _currentGeneration;
        internal int BufferCapacityForTesting => _seenGeneration.Length;
        internal int[] SeenGenerationForTesting => _seenGeneration;
        internal int[] ClosedGenerationForTesting => _closedGeneration;

        private struct HeapNode
        {
            public int NodeIndex;
            public float F;
            public float G;
            public long Sequence;
        }

        private sealed class MinHeap
        {
            private HeapNode[] _data;
            public int Count { get; private set; }

            public MinHeap(int initialCapacity)
            {
                _data = new HeapNode[initialCapacity];
            }

            public void Clear()
            {
                Count = 0;
            }

            public void Enqueue(HeapNode item)
            {
                if (Count == _data.Length)
                {
                    Array.Resize(ref _data, _data.Length * 2);
                }

                _data[Count] = item;
                BubbleUp(Count);
                Count++;
            }

            public HeapNode Dequeue()
            {
                if (Count == 0) throw new InvalidOperationException("Heap is empty");

                HeapNode min = _data[0];
                Count--;
                if (Count > 0)
                {
                    _data[0] = _data[Count];
                    BubbleDown(0);
                }
                return min;
            }

            private void BubbleUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (IsHigherPriority(in _data[index], in _data[parent]))
                    {
                        Swap(index, parent);
                        index = parent;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private void BubbleDown(int index)
            {
                while (true)
                {
                    int left = 2 * index + 1;
                    int right = 2 * index + 2;
                    int highest = index;

                    if (left < Count && IsHigherPriority(in _data[left], in _data[highest]))
                    {
                        highest = left;
                    }
                    if (right < Count && IsHigherPriority(in _data[right], in _data[highest]))
                    {
                        highest = right;
                    }

                    if (highest != index)
                    {
                        Swap(index, highest);
                        index = highest;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private static bool IsHigherPriority(in HeapNode a, in HeapNode b)
            {
                if (a.F < b.F) return true;
                if (a.F > b.F) return false;

                // Priority tie-breaker: larger g preferred (-g ascending)
                if (a.G > b.G) return true;
                if (a.G < b.G) return false;

                // FIFO sequence counter
                return a.Sequence < b.Sequence;
            }

            private void Swap(int i, int j)
            {
                var temp = _data[i];
                _data[i] = _data[j];
                _data[j] = temp;
            }
        }
    }
}
