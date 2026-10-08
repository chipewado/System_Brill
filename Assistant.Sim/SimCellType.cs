namespace Assistant.Sim
{
    public enum SimCellType
    {
        Walkable = 0,
        RoughTerrain = 1,
        DangerZone = 2,
        Obstacle = 3
    }

    public static class SimCellExtensions
    {
        public static float GetTraversalCost(this SimCellType type) => type switch
        {
            SimCellType.Walkable => 1.0f,
            SimCellType.RoughTerrain => 2.5f,
            SimCellType.DangerZone => 5.0f,
            SimCellType.Obstacle => float.PositiveInfinity,
            _ => 1.0f
        };

        public static bool IsWalkable(this SimCellType type) => type != SimCellType.Obstacle;
    }
}
