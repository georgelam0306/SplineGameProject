namespace Networking;

/// <summary>
/// Platform-agnostic matchmaking interface for lobby discovery.
/// Implementations: HttpMatchmakingProvider (custom server), SteamMatchmakingProvider, etc.
/// Connection is handled separately by NetworkCoordinator after matchmaking completes.
/// </summary>
public interface IMatchmakingProvider : IDisposable
{
    // --- State Queries ---

    /// <summary>Current matchmaking state.</summary>
    MatchmakingState State { get; }

    /// <summary>Error message if State == Error, otherwise empty.</summary>
    string ErrorMessage { get; }

    /// <summary>Available lobbies (updated after RequestLobbyListRefresh completes).</summary>
    IReadOnlyList<LobbyListItem> AvailableLobbies { get; }

    /// <summary>Whether lobby list refresh is currently in progress.</summary>
    bool IsRefreshingLobbyList { get; }

    /// <summary>Current lobby info when in InLobby state.</summary>
    LobbyInfo? CurrentLobby { get; }

    /// <summary>Whether local player was kicked from lobby.</summary>
    bool WasKicked { get; }

    /// <summary>Current list of players in lobby (updated via heartbeat).</summary>
    IReadOnlyList<LobbyPlayerInfo> LobbyPlayers { get; }

    /// <summary>Whether local player is the host of the current lobby.</summary>
    bool IsHost { get; }

    /// <summary>Whether the match has been started by the host.</summary>
    bool MatchStarted { get; }

    // --- Commands ---

    /// <summary>
    /// Poll for background task completion. Call every frame.
    /// Events are fired from this method when async operations complete.
    /// </summary>
    void Update();

    /// <summary>
    /// Request refresh of available lobbies.
    /// Fires OnLobbyListRefreshed when complete.
    /// </summary>
    void RequestLobbyListRefresh();

    /// <summary>
    /// Create a new lobby as host.
    /// Fires OnLobbyCreated on success, OnError on failure.
    /// </summary>
    void CreateLobby(string displayName, int port);

    /// <summary>
    /// Join an existing lobby.
    /// Fires OnLobbyJoined on success, OnError on failure.
    /// </summary>
    void JoinLobby(string lobbyId, string displayName, int port);

    /// <summary>
    /// Create a private lobby with optional password.
    /// Fires OnLobbyCreated on success, OnError on failure.
    /// </summary>
    void CreatePrivateLobby(string displayName, int port, string? password);

    /// <summary>
    /// Join a private lobby with password.
    /// Fires OnLobbyJoined on success, OnError on failure.
    /// </summary>
    void JoinLobby(string lobbyId, string displayName, int port, string? password);

    /// <summary>
    /// Host kicks a player from the lobby.
    /// Fires OnPlayerKicked on success, OnError on failure.
    /// </summary>
    void KickPlayer(Guid targetPlayerId);

    /// <summary>
    /// Leave the current lobby gracefully.
    /// </summary>
    void LeaveLobby();

    /// <summary>
    /// Send heartbeat to server. Should be called periodically (e.g., every 5 seconds).
    /// Updates LobbyPlayers and detects if kicked.
    /// </summary>
    void SendHeartbeat();

    /// <summary>
    /// Set local player's ready state.
    /// Fires OnReadyStateChanged on success, OnError on failure.
    /// </summary>
    void SetReady(bool isReady);

    /// <summary>
    /// Request match start (host only). All players must be ready.
    /// Fires OnMatchStarted on success (via heartbeat), OnError on failure.
    /// </summary>
    void RequestMatchStart();

    /// <summary>
    /// Cancel ongoing operation and return to Idle state.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Reset to initial state. Call when leaving matchmaking screen.
    /// </summary>
    void Reset();

    // --- Events ---

    /// <summary>
    /// Lobby created successfully. Contains info needed to start hosting.
    /// After this event, call NetworkCoordinator.HostGame().
    /// </summary>
    event Action<LobbyCreatedArgs>? OnLobbyCreated;

    /// <summary>
    /// Lobby joined successfully. Contains host connection info.
    /// After this event, call NetworkCoordinator.JoinGame().
    /// </summary>
    event Action<LobbyJoinedArgs>? OnLobbyJoined;

    /// <summary>
    /// Lobby list refreshed. Check AvailableLobbies property for results.
    /// </summary>
    event Action? OnLobbyListRefreshed;

    /// <summary>
    /// Operation failed. Check ErrorMessage property for details.
    /// </summary>
    event Action<string>? OnError;

    /// <summary>
    /// Player kicked successfully (host only).
    /// </summary>
    event Action<Guid>? OnPlayerKicked;

    /// <summary>
    /// Local player was kicked from lobby.
    /// </summary>
    event Action? OnKickedFromLobby;

    /// <summary>
    /// Player list updated (from heartbeat response).
    /// </summary>
    event Action<IReadOnlyList<LobbyPlayerInfo>>? OnPlayerListUpdated;

    /// <summary>
    /// Local player's ready state changed.
    /// </summary>
    event Action<bool>? OnReadyStateChanged;

    /// <summary>
    /// Match has been started by the host. Detected via heartbeat.
    /// </summary>
    event Action? OnMatchStarted;
}

/// <summary>
/// Matchmaking flow states.
/// </summary>
public enum MatchmakingState
{
    /// <summary>Ready to create or join a lobby.</summary>
    Idle,

    /// <summary>Creating a new lobby (HTTP request in flight).</summary>
    CreatingLobby,

    /// <summary>Lobby created, waiting to start connection (host only).</summary>
    InLobby,

    /// <summary>Joining an existing lobby (HTTP request in flight).</summary>
    JoiningLobby,

    /// <summary>Error occurred. Check ErrorMessage for details.</summary>
    Error
}

/// <summary>
/// Lobby entry in the lobby list.
/// </summary>
public readonly struct LobbyListItem
{
    public string LobbyId { get; init; }
    public string HostName { get; init; }
    public int PlayerCount { get; init; }
    public int MaxPlayers { get; init; }
}

/// <summary>
/// Full lobby information.
/// </summary>
public readonly struct LobbyInfo
{
    public string LobbyId { get; init; }
    public string HostName { get; init; }
    public string HostIp { get; init; }
    public int HostPort { get; init; }
    public int PlayerCount { get; init; }
    public int MaxPlayers { get; init; }
    public Guid LocalPlayerId { get; init; }
    public Guid HostPlayerId { get; init; }
}

/// <summary>
/// Event args for successful lobby creation.
/// </summary>
public readonly struct LobbyCreatedArgs
{
    public string LobbyId { get; init; }
    public Guid LocalPlayerId { get; init; }
    public int ListenPort { get; init; }
}

/// <summary>
/// Event args for successful lobby join.
/// </summary>
public readonly struct LobbyJoinedArgs
{
    public string LobbyId { get; init; }
    public Guid LocalPlayerId { get; init; }
    public Guid HostPlayerId { get; init; }
    public string HostAddress { get; init; }
    public int HostPort { get; init; }
}

/// <summary>
/// Player info in a lobby (from heartbeat).
/// </summary>
public readonly struct LobbyPlayerInfo
{
    public Guid PlayerId { get; init; }
    public string DisplayName { get; init; }
    public bool IsHost { get; init; }
    public bool IsReady { get; init; }
}
