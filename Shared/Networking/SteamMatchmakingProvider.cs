using System.Security.Cryptography;
using System.Text;
using Steamworks;
using Steamworks.Data;

namespace Networking;

/// <summary>
/// Steam-based matchmaking provider using Facepunch.Steamworks.
/// Uses Steam lobbies for discovery and metadata, existing NAT punch for P2P connection.
/// </summary>
public sealed class SteamMatchmakingProvider : IMatchmakingProvider
{
    // --- State ---
    private MatchmakingState _state = MatchmakingState.Idle;
    private string _errorMessage = string.Empty;
    private List<LobbyListItem> _availableLobbies = new();
    private bool _isRefreshingLobbyList;
    private LobbyInfo? _currentLobby;
    private bool _wasKicked;
    private List<LobbyPlayerInfo> _lobbyPlayers = new();
    private bool _isHost;
    private bool _matchStarted;

    // --- Steam state ---
    private Lobby? _steamLobby;
    private SteamId _localSteamId;
    private string _localDisplayName = string.Empty;
    private int _localPort;

    // --- Pending operations ---
    private Task<Lobby[]>? _pendingRefresh;
    private Task<Lobby?>? _pendingCreate;
    private Task<Lobby?>? _pendingJoin;
    private string? _pendingJoinLobbyId;
    private string? _pendingPassword;
    private bool _pendingIsPrivate;

    // --- Events ---
    public event Action<LobbyCreatedArgs>? OnLobbyCreated;
    public event Action<LobbyJoinedArgs>? OnLobbyJoined;
    public event Action? OnLobbyListRefreshed;
    public event Action<string>? OnError;
    public event Action<Guid>? OnPlayerKicked;
    public event Action? OnKickedFromLobby;
    public event Action<IReadOnlyList<LobbyPlayerInfo>>? OnPlayerListUpdated;
    public event Action<bool>? OnReadyStateChanged;
    public event Action? OnMatchStarted;

    // --- Properties ---
    public MatchmakingState State => _state;
    public string ErrorMessage => _errorMessage;
    public IReadOnlyList<LobbyListItem> AvailableLobbies => _availableLobbies;
    public bool IsRefreshingLobbyList => _isRefreshingLobbyList;
    public LobbyInfo? CurrentLobby => _currentLobby;
    public bool WasKicked => _wasKicked;
    public IReadOnlyList<LobbyPlayerInfo> LobbyPlayers => _lobbyPlayers;
    public bool IsHost => _isHost;
    public bool MatchStarted => _matchStarted;

    public SteamMatchmakingProvider()
    {
        if (!SteamClient.IsValid)
        {
            throw new InvalidOperationException("Steam client not initialized. Call SteamClient.Init() first.");
        }
        _localSteamId = SteamClient.SteamId;
    }

    public void Update()
    {
        // Dispatch Steam callbacks
        SteamClient.RunCallbacks();

        // Poll lobby state if in lobby
        if (_steamLobby.HasValue && _state == MatchmakingState.InLobby)
        {
            PollLobbyState();
        }

        // Check pending async operations
        CheckPendingRefresh();
        CheckPendingCreate();
        CheckPendingJoin();
    }

    #region Lobby List

    public void RequestLobbyListRefresh()
    {
        if (_pendingRefresh != null) return;

        _isRefreshingLobbyList = true;
        _pendingRefresh = SteamMatchmaking.LobbyList
            .FilterDistanceWorldwide()
            .WithKeyValue("game_id", SteamMatchmakingConfig.GameId)
            .RequestAsync();
    }

    private void CheckPendingRefresh()
    {
        if (_pendingRefresh == null) return;
        if (!_pendingRefresh.IsCompleted) return;

        try
        {
            var lobbies = _pendingRefresh.Result ?? Array.Empty<Lobby>();
            _availableLobbies.Clear();

            foreach (var lobby in lobbies)
            {
                // Skip started matches
                if (lobby.GetData("match_started") == "1") continue;

                // Skip private lobbies (they have password_hash set)
                var passwordHash = lobby.GetData("password_hash");
                if (!string.IsNullOrEmpty(passwordHash)) continue;

                _availableLobbies.Add(new LobbyListItem
                {
                    LobbyId = lobby.Id.Value.ToString(),
                    HostName = lobby.GetData("host_name") ?? "Unknown",
                    PlayerCount = lobby.MemberCount,
                    MaxPlayers = lobby.MaxMembers
                });
            }
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            OnError?.Invoke(_errorMessage);
        }

        _isRefreshingLobbyList = false;
        _pendingRefresh = null;
        OnLobbyListRefreshed?.Invoke();
    }

    #endregion

    #region Create Lobby

    public void CreateLobby(string displayName, int port)
    {
        CreatePrivateLobby(displayName, port, null);
    }

    public void CreatePrivateLobby(string displayName, int port, string? password)
    {
        if (_pendingCreate != null) return;

        _state = MatchmakingState.CreatingLobby;
        _localDisplayName = displayName;
        _localPort = port;
        _pendingPassword = password;
        _pendingIsPrivate = !string.IsNullOrEmpty(password);

        _pendingCreate = SteamMatchmaking.CreateLobbyAsync(MatchmakingConfig.MaxPlayers);
    }

    private void CheckPendingCreate()
    {
        if (_pendingCreate == null) return;
        if (!_pendingCreate.IsCompleted) return;

        try
        {
            var lobby = _pendingCreate.Result;
            if (!lobby.HasValue)
            {
                _errorMessage = "Failed to create Steam lobby";
                _state = MatchmakingState.Error;
                OnError?.Invoke(_errorMessage);
                _pendingCreate = null;
                _pendingPassword = null;
                return;
            }

            _steamLobby = lobby.Value;
            var lobbyVal = lobby.Value;

            // Set lobby metadata
            lobbyVal.SetData("game_id", SteamMatchmakingConfig.GameId);
            lobbyVal.SetData("host_name", _localDisplayName);
            lobbyVal.SetData("host_steam_id", _localSteamId.Value.ToString());
            lobbyVal.SetData("host_port", _localPort.ToString());
            lobbyVal.SetData("match_started", "0");
            lobbyVal.SetData("kick_list", "");

            if (_pendingIsPrivate && !string.IsNullOrEmpty(_pendingPassword))
            {
                lobbyVal.SetData("password_hash", HashPassword(_pendingPassword));
                lobbyVal.SetPrivate();
            }
            else
            {
                lobbyVal.SetPublic();
            }

            // Set local member data
            lobbyVal.SetMemberData("display_name", _localDisplayName);
            lobbyVal.SetMemberData("ready", "0");
            lobbyVal.SetMemberData("port", _localPort.ToString());

            _isHost = true;
            _state = MatchmakingState.InLobby;
            _matchStarted = false;

            UpdateCurrentLobbyInfo(lobbyVal);
            UpdateLobbyPlayers(lobbyVal);

            OnLobbyCreated?.Invoke(new LobbyCreatedArgs
            {
                LobbyId = lobbyVal.Id.Value.ToString(),
                LocalPlayerId = _localSteamId.ToGuid(),
                ListenPort = _localPort
            });
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            _state = MatchmakingState.Error;
            OnError?.Invoke(_errorMessage);
        }

        _pendingCreate = null;
        _pendingPassword = null;
    }

    #endregion

    #region Join Lobby

    public void JoinLobby(string lobbyId, string displayName, int port)
    {
        JoinLobby(lobbyId, displayName, port, null);
    }

    public void JoinLobby(string lobbyId, string displayName, int port, string? password)
    {
        if (_pendingJoin != null) return;

        _state = MatchmakingState.JoiningLobby;
        _localDisplayName = displayName;
        _localPort = port;
        _pendingPassword = password;
        _pendingJoinLobbyId = lobbyId;

        var steamLobbyId = new SteamId { Value = ulong.Parse(lobbyId) };
        _pendingJoin = SteamMatchmaking.JoinLobbyAsync(steamLobbyId);
    }

    private void CheckPendingJoin()
    {
        if (_pendingJoin == null) return;
        if (!_pendingJoin.IsCompleted) return;

        try
        {
            var lobby = _pendingJoin.Result;
            if (!lobby.HasValue)
            {
                _errorMessage = "Failed to join Steam lobby";
                _state = MatchmakingState.Error;
                OnError?.Invoke(_errorMessage);
                _pendingJoin = null;
                _pendingPassword = null;
                _pendingJoinLobbyId = null;
                return;
            }

            var lobbyVal = lobby.Value;

            // Validate password if required
            var storedHash = lobbyVal.GetData("password_hash");
            if (!string.IsNullOrEmpty(storedHash))
            {
                if (string.IsNullOrEmpty(_pendingPassword) ||
                    storedHash != HashPassword(_pendingPassword))
                {
                    lobbyVal.Leave();
                    _errorMessage = "Invalid password";
                    _state = MatchmakingState.Error;
                    OnError?.Invoke(_errorMessage);
                    _pendingJoin = null;
                    _pendingPassword = null;
                    _pendingJoinLobbyId = null;
                    return;
                }
            }

            _steamLobby = lobbyVal;

            // Set local member data
            lobbyVal.SetMemberData("display_name", _localDisplayName);
            lobbyVal.SetMemberData("ready", "0");
            lobbyVal.SetMemberData("port", _localPort.ToString());

            _isHost = lobbyVal.Owner.Id == _localSteamId;
            _state = MatchmakingState.InLobby;
            _matchStarted = false;

            UpdateCurrentLobbyInfo(lobbyVal);
            UpdateLobbyPlayers(lobbyVal);

            // Get host connection info
            var hostSteamIdStr = lobbyVal.GetData("host_steam_id") ?? lobbyVal.Owner.Id.Value.ToString();
            var hostSteamId = new SteamId { Value = ulong.Parse(hostSteamIdStr) };
            var hostPort = int.TryParse(lobbyVal.GetData("host_port"), out var p) ? p : MatchmakingConfig.DefaultPort;

            OnLobbyJoined?.Invoke(new LobbyJoinedArgs
            {
                LobbyId = lobbyVal.Id.Value.ToString(),
                LocalPlayerId = _localSteamId.ToGuid(),
                HostPlayerId = hostSteamId.ToGuid(),
                HostAddress = hostSteamIdStr, // SteamId as address for NAT punch token
                HostPort = hostPort
            });
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            _state = MatchmakingState.Error;
            OnError?.Invoke(_errorMessage);
        }

        _pendingJoin = null;
        _pendingPassword = null;
        _pendingJoinLobbyId = null;
    }

    #endregion

    #region Lobby State Polling

    private void PollLobbyState()
    {
        if (!_steamLobby.HasValue) return;
        var lobby = _steamLobby.Value;

        // Check kick list
        var kickList = lobby.GetData("kick_list") ?? "";
        if (!string.IsNullOrEmpty(kickList))
        {
            var kickedIds = kickList.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var kickedIdStr in kickedIds)
            {
                if (ulong.TryParse(kickedIdStr, out var kickedId) && kickedId == _localSteamId.Value)
                {
                    // We were kicked
                    _wasKicked = true;
                    lobby.Leave();
                    _steamLobby = null;
                    _currentLobby = null;
                    _state = MatchmakingState.Idle;
                    OnKickedFromLobby?.Invoke();
                    return;
                }
            }
        }

        // Check if we're still a member
        bool stillMember = false;
        foreach (var member in lobby.Members)
        {
            if (member.Id == _localSteamId)
            {
                stillMember = true;
                break;
            }
        }

        if (!stillMember)
        {
            _wasKicked = true;
            _steamLobby = null;
            _currentLobby = null;
            _state = MatchmakingState.Idle;
            OnKickedFromLobby?.Invoke();
            return;
        }

        // Check match started flag
        var matchStartedStr = lobby.GetData("match_started");
        var matchStartedNow = matchStartedStr == "1";
        if (matchStartedNow && !_matchStarted)
        {
            _matchStarted = true;
            OnMatchStarted?.Invoke();
        }

        // Update player list
        UpdateLobbyPlayers(lobby);
    }

    private void UpdateCurrentLobbyInfo(Lobby lobby)
    {
        var hostSteamIdStr = lobby.GetData("host_steam_id");
        SteamId hostSteamId;

        if (!string.IsNullOrEmpty(hostSteamIdStr) && ulong.TryParse(hostSteamIdStr, out var hostId))
        {
            hostSteamId = new SteamId { Value = hostId };
        }
        else
        {
            hostSteamId = lobby.Owner.Id;
        }

        _currentLobby = new LobbyInfo
        {
            LobbyId = lobby.Id.Value.ToString(),
            HostName = lobby.GetData("host_name") ?? "Unknown",
            HostIp = hostSteamIdStr ?? lobby.Owner.Id.Value.ToString(),
            HostPort = int.TryParse(lobby.GetData("host_port"), out var p) ? p : MatchmakingConfig.DefaultPort,
            PlayerCount = lobby.MemberCount,
            MaxPlayers = lobby.MaxMembers,
            LocalPlayerId = _localSteamId.ToGuid(),
            HostPlayerId = hostSteamId.ToGuid()
        };
    }

    private void UpdateLobbyPlayers(Lobby lobby)
    {
        var previousPlayers = _lobbyPlayers.ToList();
        _lobbyPlayers.Clear();

        foreach (var member in lobby.Members)
        {
            var displayName = lobby.GetMemberData(member, "display_name");
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = member.Name ?? "Unknown";
            }

            _lobbyPlayers.Add(new LobbyPlayerInfo
            {
                PlayerId = member.Id.ToGuid(),
                DisplayName = displayName,
                IsHost = member.Id == lobby.Owner.Id,
                IsReady = lobby.GetMemberData(member, "ready") == "1"
            });
        }

        // Fire event if changed
        if (!PlayersEqual(previousPlayers, _lobbyPlayers))
        {
            OnPlayerListUpdated?.Invoke(_lobbyPlayers);
        }
    }

    private static bool PlayersEqual(IReadOnlyList<LobbyPlayerInfo> a, IReadOnlyList<LobbyPlayerInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].PlayerId != b[i].PlayerId ||
                a[i].IsReady != b[i].IsReady ||
                a[i].DisplayName != b[i].DisplayName)
                return false;
        }
        return true;
    }

    #endregion

    #region In-Lobby Operations

    public void SetReady(bool isReady)
    {
        if (!_steamLobby.HasValue) return;

        _steamLobby.Value.SetMemberData("ready", isReady ? "1" : "0");
        OnReadyStateChanged?.Invoke(isReady);
    }

    public void RequestMatchStart()
    {
        if (!_isHost || !_steamLobby.HasValue) return;

        // Check all players ready
        foreach (var player in _lobbyPlayers)
        {
            if (!player.IsReady)
            {
                _errorMessage = "Not all players are ready";
                OnError?.Invoke(_errorMessage);
                return;
            }
        }

        // Set match started flag in lobby metadata
        _steamLobby.Value.SetData("match_started", "1");
        // Event will fire on next poll when we detect the change
    }

    public void KickPlayer(Guid targetPlayerId)
    {
        if (!_isHost || !_steamLobby.HasValue) return;

        // Steam doesn't have direct kick API for lobbies
        // Add to kick list in metadata - clients check this in PollLobbyState
        var targetSteamId = targetPlayerId.ToSteamId();

        var kickList = _steamLobby.Value.GetData("kick_list") ?? "";
        if (string.IsNullOrEmpty(kickList))
        {
            kickList = targetSteamId.Value.ToString();
        }
        else
        {
            kickList = $"{kickList},{targetSteamId.Value}";
        }
        _steamLobby.Value.SetData("kick_list", kickList);

        OnPlayerKicked?.Invoke(targetPlayerId);
    }

    public void LeaveLobby()
    {
        if (_steamLobby.HasValue)
        {
            _steamLobby.Value.Leave();
        }
        ResetLobbyState();
    }

    public void SendHeartbeat()
    {
        // Force poll lobby state on next Update
        if (_steamLobby.HasValue && _state == MatchmakingState.InLobby)
        {
            PollLobbyState();
        }
    }

    #endregion

    #region State Management

    public void Cancel()
    {
        _state = MatchmakingState.Idle;
        _errorMessage = string.Empty;
    }

    public void Reset()
    {
        LeaveLobby();
        _availableLobbies.Clear();
        _isRefreshingLobbyList = false;
        _pendingRefresh = null;
        _pendingCreate = null;
        _pendingJoin = null;
        _pendingPassword = null;
        _pendingJoinLobbyId = null;
    }

    private void ResetLobbyState()
    {
        _steamLobby = null;
        _currentLobby = null;
        _lobbyPlayers.Clear();
        _isHost = false;
        _matchStarted = false;
        _wasKicked = false;
        _state = MatchmakingState.Idle;
    }

    #endregion

    #region Helpers

    private static string HashPassword(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    #endregion

    public void Dispose()
    {
        if (_steamLobby.HasValue)
        {
            _steamLobby.Value.Leave();
        }
    }
}
