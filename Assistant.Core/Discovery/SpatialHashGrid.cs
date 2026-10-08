using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Discovery
{
    /// <summary>
    /// 2D Spatial hash grid on the X-Z plane for candidate spatial queries.
    /// Note: This class is not thread-safe. Synchronization must be handled by caller (e.g. via repository lock).
    /// </summary>
    public class SpatialHashGrid
    {
        private readonly float _cellSize;
        private readonly Dictionary<(int cx, int cz), HashSet<string>> _cells = new Dictionary<(int, int), HashSet<string>>();
        private readonly Dictionary<string, (int cx, int cz)> _idToCell = new Dictionary<string, (int, int)>();

        public SpatialHashGrid(float cellSize = 50f)
        {
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
            _cellSize = cellSize;
        }

        public float CellSize => _cellSize;
        public int Count => _idToCell.Count;

        private int ToCellIndex(float v)
        {
            double c = Math.Floor(v / (double)_cellSize);
            if (c > 1_000_000_000) return 1_000_000_000;
            if (c < -1_000_000_000) return -1_000_000_000;
            return (int)c;
        }

        private (int cx, int cz) GetCell(Vec3 pos) => (ToCellIndex(pos.X), ToCellIndex(pos.Z));

        private static bool IsUsable(Vec3 p) => float.IsFinite(p.X) && float.IsFinite(p.Z);

        public bool Insert(string id, Vec3 pos)
        {
            if (string.IsNullOrEmpty(id) || !IsUsable(pos)) return false;
            Remove(id);
            var cell = GetCell(pos);
            if (!_cells.TryGetValue(cell, out var set))
            {
                set = new HashSet<string>();
                _cells[cell] = set;
            }
            set.Add(id);
            _idToCell[id] = cell;
            return true;
        }

        public void Update(string id, Vec3 newPos)
        {
            if (!IsUsable(newPos)) return;
            if (_idToCell.TryGetValue(id, out var current) && current == GetCell(newPos)) return;
            Insert(id, newPos);
        }

        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (_idToCell.TryGetValue(id, out var cell))
            {
                _idToCell.Remove(id);
                if (_cells.TryGetValue(cell, out var set))
                {
                    set.Remove(id);
                    if (set.Count == 0)
                    {
                        _cells.Remove(cell);
                    }
                }
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _cells.Clear();
            _idToCell.Clear();
        }

        public HashSet<string> QueryCandidates(Vec3 center, float radius)
        {
            var result = new HashSet<string>();
            if (!IsUsable(center) || float.IsNaN(radius) || radius < 0) return result;
            if (float.IsPositiveInfinity(radius))
            {
                result.UnionWith(_idToCell.Keys);
                return result;
            }

            int minX = ToCellIndex(center.X - radius), maxX = ToCellIndex(center.X + radius);
            int minZ = ToCellIndex(center.Z - radius), maxZ = ToCellIndex(center.Z + radius);
            long span = ((long)maxX - minX + 1) * ((long)maxZ - minZ + 1);

            if (span > _cells.Count) // Fewer real cells than candidate span -> iterate real cells
            {
                foreach (var kv in _cells)
                {
                    var (cx, cz) = kv.Key;
                    if (cx >= minX && cx <= maxX && cz >= minZ && cz <= maxZ)
                    {
                        result.UnionWith(kv.Value);
                    }
                }
                return result;
            }

            for (int cx = minX; cx <= maxX; cx++)
            {
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    if (_cells.TryGetValue((cx, cz), out var set))
                    {
                        result.UnionWith(set);
                    }
                }
            }
            return result;
        }

        [Obsolete("Use QueryCandidates instead.")]
        public HashSet<string> QueryRadius(Vec3 center, float radius) => QueryCandidates(center, radius);
    }
}
