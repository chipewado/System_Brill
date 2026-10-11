namespace Assistant.Sim.Harness
{
    /// <summary>
    /// Replan scheduling policy for the simulation harness.
    /// </summary>
    public enum ReplanPolicy
    {
        CadenceAndEvents = 0,
        EventsOnly = 1
    }

    /// <summary>
    /// Follower waypoint navigation policy.
    /// </summary>
    public enum FollowerWaypointPolicy
    {
        KeepFirst = 0,
        SkipFirstIfWalkable = 1
    }
}
