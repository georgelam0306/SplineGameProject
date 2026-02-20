namespace Catrillion.Simulation;

/// <summary>
/// Outcome of the match when GameOver state is reached.
/// Stored in GameRulesStateRow for deterministic simulation.
/// </summary>
public enum MatchOutcome : byte
{
    /// <summary>Match still in progress or outcome not yet determined.</summary>
    None = 0,

    /// <summary>Players won - all enemies defeated after final wave.</summary>
    Victory = 1,

    /// <summary>Players lost - command center was destroyed.</summary>
    Defeat = 2
}
