namespace Networking;

/// <summary>
/// Configuration for matchmaking services.
/// </summary>
public static class MatchmakingConfig
{
    /// <summary>HTTP matchmaking server URL.</summary>
    public const string ServerUrl = "http://45.76.79.231:5050";

    /// <summary>NAT punch server host.</summary>
    public const string NatPunchHost = "45.76.79.231";

    /// <summary>NAT punch server port.</summary>
    public const int NatPunchPort = 5051;

    /// <summary>Default game port for P2P connections.</summary>
    public const int DefaultPort = 7777;

    /// <summary>Maximum players per lobby.</summary>
    public const int MaxPlayers = 8;
}
