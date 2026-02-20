namespace Networking;

/// <summary>
/// No-op matchmaking provider for local multiplayer testing.
/// Used when running without Orleans/Steam matchmaking servers.
/// </summary>
public sealed class LocalMatchmakingProvider : IMatchmakingProvider
{
    public MatchmakingState State => MatchmakingState.Idle;
    public string ErrorMessage => string.Empty;
    public IReadOnlyList<LobbyListItem> AvailableLobbies => Array.Empty<LobbyListItem>();
    public bool IsRefreshingLobbyList => false;
    public LobbyInfo? CurrentLobby => null;
    public bool WasKicked => false;
    public IReadOnlyList<LobbyPlayerInfo> LobbyPlayers => Array.Empty<LobbyPlayerInfo>();
    public bool IsHost => true;
    public bool MatchStarted => false;

    public event Action<LobbyCreatedArgs>? OnLobbyCreated;
    public event Action<LobbyJoinedArgs>? OnLobbyJoined;
    public event Action? OnLobbyListRefreshed;
    public event Action<string>? OnError;
    public event Action<Guid>? OnPlayerKicked;
    public event Action? OnKickedFromLobby;
    public event Action<IReadOnlyList<LobbyPlayerInfo>>? OnPlayerListUpdated;
    public event Action<bool>? OnReadyStateChanged;
    public event Action? OnMatchStarted;

    public void Update() { }
    public void RequestLobbyListRefresh() { }
    public void CreateLobby(string displayName, int port) { }
    public void JoinLobby(string lobbyId, string displayName, int port) { }
    public void CreatePrivateLobby(string displayName, int port, string? password) { }
    public void JoinLobby(string lobbyId, string displayName, int port, string? password) { }
    public void KickPlayer(Guid targetPlayerId) { }
    public void LeaveLobby() { }
    public void SendHeartbeat() { }
    public void SetReady(bool isReady) { }
    public void RequestMatchStart() { }
    public void Cancel() { }
    public void Reset() { }
    public void Dispose() { }
}
