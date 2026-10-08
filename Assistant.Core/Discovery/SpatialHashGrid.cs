using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Data;

namespace Assistant.Core.Discovery
{
    /// <summary>
    /// 2D Spatial hash grid on the X-Z plane for fast spatial queries.
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

        private (int cx, int cz) GetCell(Vec3 pos)
        {
            int cx = (int)Math.Floor(pos.X / _cellSize);
            int cz = (int)Math.Floor(pos.Z / _cellSize);
            return (cx, cz);
        }

        public void Insert(string id, Vec3 pos)
        {
            if (string.IsNullOrEmpty(id)) return;

            // Remove previous cell mapping if exists
            Remove(id);

            var cell = GetCell(pos);
            if (!_cells.TryGetValue(cell, out var set))
            {
                set = new HashSet<string>();
                _cells[cell] = set;
            }
            set.Add(id);
            _idToCell[id] = cell;
        }

        public void Update(string id, Vec3 oldPos, Vec3 newPos)
        {
            var oldCell = GetCell(oldPos);
            var newCell = GetCell(newPos);
            if (oldCell == newCell && _idToCell.ContainsKey(id))
            {
                return;
            }
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

        public HashSet<string> QueryRadius(Vec3 center, float radius)
        {
            var result = new HashSet<string>();
            int minX = (int)Math.Floor((center.X - radius) / _cellSize);
            int maxX = (int)Math.Floor((center.X + radius) / _cellSize);
            int minZ = (int)Math.Floor((center.Z - radius) / _cellSize);
            int maxZ = (int)Math.Floor((center.Z + radius) / _cellSize);

            for (int cx = minX; cx <= maxX; cx++)
            {
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    if (_cells.TryGetValue((cx, cz), out var set))
                    {
                        foreach (var id in set)
                        {
                            result.Add(id);
                        }
                    }
                }
            }
            return result;
        }
    }
}
