namespace MatchmakingContracts.Models;

[GenerateSerializer]
public enum LobbyVisibility
{
    Public,
    Private
}

[GenerateSerializer]
public sealed class PlayerInfo
{
    [Id(0)]
    public Guid PlayerId { get; set; }

    [Id(1)]
    public string DisplayName { get; set; } = string.Empty;

    [Id(2)]
    public string PublicIp { get; set; } = string.Empty;

    [Id(3)]
    public int Port { get; set; }

    [Id(4)]
    public bool IsHost { get; set; }

    [Id(5)]
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    [Id(6)]
    public bool IsReady { get; set; }
}

[GenerateSerializer]
public enum MatchState
{
    Open,
    Closed
}

[GenerateSerializer]
public sealed class MatchInfo
{
    [Id(0)]
    public string MatchId { get; set; } = string.Empty;

    [Id(1)]
    public MatchState State { get; set; }

    [Id(2)]
    public List<PlayerInfo> Players { get; set; } = new();

    [Id(3)]
    public string HostIp { get; set; } = string.Empty;

    [Id(4)]
    public int HostPort { get; set; }

    [Id(5)]
    public int MaxPlayers { get; set; } = 8;

    [Id(6)]
    public LobbyVisibility Visibility { get; set; } = LobbyVisibility.Public;

    [Id(7)]
    public bool HasPassword { get; set; }

    [Id(8)]
    public Guid HostPlayerId { get; set; }
}

[GenerateSerializer]
public sealed class MatchListItem
{
    [Id(0)]
    public string MatchId { get; set; } = string.Empty;

    [Id(1)]
    public string HostName { get; set; } = string.Empty;

    [Id(2)]
    public int PlayerCount { get; set; }

    [Id(3)]
    public int MaxPlayers { get; set; }
}
