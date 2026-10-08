using System;
using Assistant.Core.Data;

namespace Assistant.Sim
{
    public class SimGridMap
    {
        public int GridWidth { get; }
        public int GridHeight { get; }
        public float CellSize { get; }

        private readonly SimCellType[,] _cells;

        public SimGridMap(int width = 100, int height = 100, float cellSize = 10f)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));

            GridWidth = width;
            GridHeight = height;
            CellSize = cellSize;
            _cells = new SimCellType[width, height];
        }

        public MapBounds WorldBounds => new MapBounds(
            new Vec3(0, 0, 0),
            new Vec3(GridWidth * CellSize, 0, GridHeight * CellSize)
        );

        public MapData ToMapData(string mapId = "SimulatedMine_Alpha")
        {
            return new MapData(mapId, WorldBounds);
        }

        public (int gx, int gz) WorldToGrid(Vec3 pos)
        {
            int gx = (int)Math.Floor(pos.X / CellSize);
            int gz = (int)Math.Floor(pos.Z / CellSize);
            return (gx, gz);
        }

        public Vec3 GridToWorld(int gx, int gz)
        {
            return new Vec3(
                (gx + 0.5f) * CellSize,
                0,
                (gz + 0.5f) * CellSize
            );
        }

        public bool IsInBounds(int gx, int gz)
        {
            return gx >= 0 && gx < GridWidth && gz >= 0 && gz < GridHeight;
        }

        public SimCellType GetCell(int gx, int gz)
        {
            if (!IsInBounds(gx, gz)) return SimCellType.Obstacle;
            return _cells[gx, gz];
        }

        public void SetCell(int gx, int gz, SimCellType cellType)
        {
            if (IsInBounds(gx, gz))
            {
                _cells[gx, gz] = cellType;
            }
        }

        public bool IsWalkable(Vec3 pos)
        {
            var (gx, gz) = WorldToGrid(pos);
            return GetCell(gx, gz).IsWalkable();
        }

        public float GetTraversalCost(Vec3 pos)
        {
            var (gx, gz) = WorldToGrid(pos);
            return GetCell(gx, gz).GetTraversalCost();
        }

        /// <summary>
        /// Populates procedural terrain features with obstacles, rough terrain, and danger zones.
        /// </summary>
        public void GenerateProceduralFeatures(int seed = 42)
        {
            var rng = new Random(seed);

            // Clear to walkable
            for (int x = 0; x < GridWidth; x++)
            {
                for (int z = 0; z < GridHeight; z++)
                {
                    _cells[x, z] = SimCellType.Walkable;
                }
            }

            // Generate some rock clusters / obstacles (~5% of map)
            int obstacleCount = (int)(GridWidth * GridHeight * 0.05f);
            for (int i = 0; i < obstacleCount; i++)
            {
                int ox = rng.Next(1, GridWidth - 1);
                int oz = rng.Next(1, GridHeight - 1);
                _cells[ox, oz] = SimCellType.Obstacle;
                if (rng.NextDouble() < 0.4 && IsInBounds(ox + 1, oz)) _cells[ox + 1, oz] = SimCellType.Obstacle;
                if (rng.NextDouble() < 0.4 && IsInBounds(ox, oz + 1)) _cells[ox, oz + 1] = SimCellType.Obstacle;
            }

            // Generate rough terrain patches (~10%)
            int roughCount = (int)(GridWidth * GridHeight * 0.10f);
            for (int i = 0; i < roughCount; i++)
            {
                int rx = rng.Next(0, GridWidth);
                int rz = rng.Next(0, GridHeight);
                if (_cells[rx, rz] == SimCellType.Walkable)
                {
                    _cells[rx, rz] = SimCellType.RoughTerrain;
                }
            }

            // Generate danger zones (~5%)
            int dangerCount = (int)(GridWidth * GridHeight * 0.05f);
            for (int i = 0; i < dangerCount; i++)
            {
                int dx = rng.Next(0, GridWidth);
                int dz = rng.Next(0, GridHeight);
                if (_cells[dx, dz] == SimCellType.Walkable)
                {
                    _cells[dx, dz] = SimCellType.DangerZone;
                }
            }
        }
    }
}
