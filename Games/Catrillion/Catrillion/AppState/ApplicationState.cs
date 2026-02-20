namespace Catrillion.AppState;

/// <summary>
/// Application screen states for the main menu flow.
/// </summary>
public enum ApplicationState
{
    /// <summary>Main menu with Local Play, Host Game, Join Game options.</summary>
    MainMenu,

    /// <summary>Matchmaking screen for browsing and creating online lobbies.</summary>
    Matchmaking,

    /// <summary>Lobby screen showing connected players and ready states.</summary>
    Lobby,

    /// <summary>Loading screen while Game is being created and players confirm loaded.</summary>
    Loading,

    /// <summary>Countdown before game starts (3 seconds).</summary>
    Countdown,

    /// <summary>Active game simulation running.</summary>
    InGame,

    /// <summary>Game over screen showing victory/defeat and stats.</summary>
    GameOver
}
