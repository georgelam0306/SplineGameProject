namespace Networking;

/// <summary>
/// Configuration for Steam matchmaking.
/// </summary>
public static class SteamMatchmakingConfig
{
    /// <summary>
    /// Game identifier used to filter Steam lobbies.
    /// Only lobbies with matching game_id metadata will be shown.
    /// </summary>
    public const string GameId = "Catrillion";

    /// <summary>
    /// Steam App ID for initialization.
    /// 480 = Spacewar (Valve's test app) for development.
    /// Replace with your actual App ID for production.
    /// </summary>
    public const uint AppId = 480;
}
