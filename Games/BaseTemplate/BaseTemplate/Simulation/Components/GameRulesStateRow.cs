using SimTable;

namespace BaseTemplate.Simulation.Components;

/// <summary>
/// Game rules and match state tracking.
/// Singleton table (Capacity=1) for deterministic snapshotting.
/// Auto-allocated with default MatchState = Countdown.
/// </summary>
[SimDataTable]
public partial struct GameRulesStateRow
{
    public MatchState MatchState = MatchState.Countdown;
    public MatchOutcome MatchOutcome;
    public int SpawnedUnitCount;
    public int WinningPlayerId;
    public int FrameMatchStarted;
    public int SessionSeed;
}
