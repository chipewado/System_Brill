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

        public GridPosition WorldToGrid(Vec3 pos)
        {
            int gx = (int)Math.Floor(pos.X / CellSize);
            int gz = (int)Math.Floor(pos.Z / CellSize);
            return new GridPosition(gx, gz);
        }

        public Vec3 GridToWorld(GridPosition gridPos)
        {
            return new Vec3(
                (gridPos.X + 0.5f) * CellSize,
                0,
                (gridPos.Z + 0.5f) * CellSize
            );
        }

        public Vec3 GridToWorld(int gx, int gz) => GridToWorld(new GridPosition(gx, gz));

        public bool IsInBounds(GridPosition gridPos)
        {
            return gridPos.X >= 0 && gridPos.X < GridWidth && gridPos.Z >= 0 && gridPos.Z < GridHeight;
        }

        public bool IsInBounds(int gx, int gz) => IsInBounds(new GridPosition(gx, gz));

        public SimCellType GetCell(GridPosition gridPos)
        {
            if (!IsInBounds(gridPos)) return SimCellType.Obstacle;
            return _cells[gridPos.X, gridPos.Z];
        }

        public SimCellType GetCell(int gx, int gz) => GetCell(new GridPosition(gx, gz));

        public void SetCell(GridPosition gridPos, SimCellType cellType)
        {
            if (IsInBounds(gridPos))
            {
                _cells[gridPos.X, gridPos.Z] = cellType;
            }
        }

        public void SetCell(int gx, int gz, SimCellType cellType) => SetCell(new GridPosition(gx, gz), cellType);

        public bool IsWalkable(GridPosition gridPos)
        {
            return GetCell(gridPos).IsWalkable();
        }

        public bool IsWalkable(Vec3 pos)
        {
            var gridPos = WorldToGrid(pos);
            return IsWalkable(gridPos);
        }

        public float GetTraversalCost(GridPosition gridPos)
        {
            return GetCell(gridPos).GetTraversalCost();
        }

        public float GetTraversalCost(Vec3 pos)
        {
            var gridPos = WorldToGrid(pos);
            return GetTraversalCost(gridPos);
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
