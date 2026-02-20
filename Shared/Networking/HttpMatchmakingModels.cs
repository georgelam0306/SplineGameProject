using System.Text.Json.Serialization;

namespace Networking;

// Request/Response DTOs for HTTP matchmaking API
// Uses System.Text.Json source generation for AOT compatibility

// --- Lobby List ---
public sealed class ListLobbiesResponse
{
    public List<LobbyItemDto> Lobbies { get; set; } = new();
}

public sealed class LobbyItemDto
{
    public string LobbyId { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
}

// --- Create Lobby ---
public sealed class CreateLobbyRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string PublicIp { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool IsPrivate { get; set; }
    public string? PasswordHash { get; set; }
}

public sealed class CreateLobbyResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string LobbyId { get; set; } = string.Empty;
    public Guid PlayerId { get; set; }
    public LobbyInfoDto? LobbyInfo { get; set; }
}

// --- Join Lobby ---
public sealed class JoinLobbyRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string PublicIp { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? PasswordHash { get; set; }
}

public sealed class JoinLobbyResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Guid PlayerId { get; set; }
    public LobbyInfoDto? LobbyInfo { get; set; }
}

// --- Lobby Info ---
public sealed class LobbyInfoDto
{
    public string LobbyId { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string HostIp { get; set; } = string.Empty;
    public int HostPort { get; set; }
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public Guid HostPlayerId { get; set; }
    public List<PlayerInfoDto> Players { get; set; } = new();
}

public sealed class PlayerInfoDto
{
    public Guid PlayerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public bool IsReady { get; set; }
}

// --- Heartbeat ---
public sealed class HeartbeatRequest
{
    public Guid PlayerId { get; set; }
}

public sealed class HeartbeatResponse
{
    public bool Success { get; set; }
    public bool Kicked { get; set; }
    public bool MatchStarted { get; set; }
    public List<PlayerInfoDto> Players { get; set; } = new();
}

// --- Set Ready ---
public sealed class SetReadyRequest
{
    public Guid PlayerId { get; set; }
    public bool IsReady { get; set; }
}

public sealed class SetReadyResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

// --- Start Match ---
public sealed class StartMatchRequest
{
    public Guid PlayerId { get; set; }
}

public sealed class StartMatchResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

// --- Kick Player ---
public sealed class KickPlayerRequest
{
    public Guid RequestingPlayerId { get; set; }
    public Guid TargetPlayerId { get; set; }
}

public sealed class KickPlayerResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

// --- Leave Lobby ---
public sealed class LeaveLobbyRequest
{
    public Guid PlayerId { get; set; }
}

public sealed class LeaveLobbyResponse
{
    public bool Success { get; set; }
}

// --- JSON Source Generator for AOT ---
[JsonSerializable(typeof(ListLobbiesResponse))]
[JsonSerializable(typeof(LobbyItemDto))]
[JsonSerializable(typeof(CreateLobbyRequest))]
[JsonSerializable(typeof(CreateLobbyResponse))]
[JsonSerializable(typeof(JoinLobbyRequest))]
[JsonSerializable(typeof(JoinLobbyResponse))]
[JsonSerializable(typeof(LobbyInfoDto))]
[JsonSerializable(typeof(PlayerInfoDto))]
[JsonSerializable(typeof(HeartbeatRequest))]
[JsonSerializable(typeof(HeartbeatResponse))]
[JsonSerializable(typeof(SetReadyRequest))]
[JsonSerializable(typeof(SetReadyResponse))]
[JsonSerializable(typeof(StartMatchRequest))]
[JsonSerializable(typeof(StartMatchResponse))]
[JsonSerializable(typeof(KickPlayerRequest))]
[JsonSerializable(typeof(KickPlayerResponse))]
[JsonSerializable(typeof(LeaveLobbyRequest))]
[JsonSerializable(typeof(LeaveLobbyResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class MatchmakingJsonContext : JsonSerializerContext
{
}
