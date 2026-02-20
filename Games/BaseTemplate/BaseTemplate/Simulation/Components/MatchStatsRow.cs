using SimTable;

namespace BaseTemplate.Simulation.Components;

/// <summary>
/// Global match statistics for end-game display.
/// Singleton table (Capacity=1) for deterministic snapshotting.
/// </summary>
[SimDataTable]
public partial struct MatchStatsRow
{
    /// <summary>Total enemy units killed during the match.</summary>
    public int UnitsKilled;

    /// <summary>Total buildings constructed during the match.</summary>
    public int BuildingsConstructed;

    /// <summary>Total resources gathered during the match (all types combined).</summary>
    public int ResourcesGathered;
}
