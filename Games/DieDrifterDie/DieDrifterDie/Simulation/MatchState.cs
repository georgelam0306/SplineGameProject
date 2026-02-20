namespace DieDrifterDie.Simulation;

/// <summary>
/// State of the match within the game simulation.
/// Stored in GameRulesStateRow for deterministic simulation.
/// </summary>
public enum MatchState
{
    /// <summary>Waiting in lobby for players to ready up.</summary>
    Lobby = 0,

    /// <summary>Countdown before game starts.</summary>
    Countdown = 1,

    /// <summary>Active gameplay.</summary>
    Playing = 2,

    /// <summary>Game has ended.</summary>
    GameOver = 3
}
