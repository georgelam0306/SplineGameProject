using SimTable;

namespace BaseTemplate.Simulation.Components;

/// <summary>
/// Countdown state for pre-game countdown (3-2-1-GO!).
/// Singleton table (Capacity=1) for deterministic snapshotting.
/// Auto-allocated with default FramesRemaining = 3 seconds at current tick rate.
/// </summary>
[SimDataTable]
public partial struct CountdownStateRow
{
    /// <summary>Frames remaining (TickRate * 3 = 3 seconds, 0 = complete/inactive).</summary>
    public int FramesRemaining = SimulationConfig.TickRate * 3;
}
