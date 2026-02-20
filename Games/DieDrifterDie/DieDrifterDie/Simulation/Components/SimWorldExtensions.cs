namespace DieDrifterDie.Simulation.Components;

/// <summary>
/// Extension methods for SimWorld to check match state.
/// </summary>
public static class SimWorldExtensions
{
    /// <summary>
    /// Check if the game is in Playing state.
    /// Returns false during Lobby, Countdown, or GameOver.
    /// </summary>
    public static bool IsPlaying(this SimWorld world)
    {
        if (!world.GameRulesStateRows.TryGetRow(0, out var rulesRow))
            return false;
        return rulesRow.MatchState == MatchState.Playing;
    }

    /// <summary>
    /// Check if the game is in Countdown state.
    /// </summary>
    public static bool IsCountingDown(this SimWorld world)
    {
        if (!world.GameRulesStateRows.TryGetRow(0, out var rulesRow))
            return false;
        return rulesRow.MatchState == MatchState.Countdown;
    }

    /// <summary>
    /// Check if the game is in GameOver state.
    /// </summary>
    public static bool IsGameOver(this SimWorld world)
    {
        if (!world.GameRulesStateRows.TryGetRow(0, out var rulesRow))
            return false;
        return rulesRow.MatchState == MatchState.GameOver;
    }
}
