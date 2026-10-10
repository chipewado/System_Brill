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
}
