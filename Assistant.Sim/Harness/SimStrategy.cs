namespace Assistant.Sim.Harness
{
    /// <summary>
    /// Targeting strategy used by the simulation harness.
    /// </summary>
    public enum SimStrategy
    {
        RankedCoordinator = 0,
        NearestFirst = 1,
        NearestFirstNaive = 1,
        NearestReachable = 2,
        BestRate = 3,
        HighestValueFirst = 4
    }
}
