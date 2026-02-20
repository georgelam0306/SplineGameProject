using MatchmakingContracts.Grains;
using MatchmakingContracts.Models;
using Orleans;
using Serilog;

namespace Networking;

/// <summary>
/// Orleans-based matchmaking provider that calls grains directly via IClusterClient.
/// More efficient than HTTP - uses Orleans' binary serialization and persistent connections.
/// </summary>
public sealed class OrleansMatchmakingProvider : IMatchmakingProvider
{
    private readonly ILogger _log;
    private readonly IClusterClient _client;

    // State
    private MatchmakingState _state = MatchmakingState.Idle;
    private string _errorMessage = string.Empty;
    private List<LobbyListItem> _availableLobbies = new();
    private bool _isRefreshingLobbyList;
    private LobbyInfo? _currentLobby;
    private bool _wasKicked;
    private List<LobbyPlayerInfo> _lobbyPlayers = new();
    private bool _isHost;
    private bool _matchStarted;

    // Local state
    private Guid _localPlayerId;
    private string _currentMatchId = string.Empty;
    private int _localPort;

    // Pending async operations
    private Task? _pendingRefresh;
    private Task? _pendingCreate;
    private Task? _pendingJoin;
    private Task? _pendingKick;
    private Task? _pendingLeave;
    private Task? _pendingHeartbeat;
    private Task? _pendingSetReady;
    private Task? _pendingStartMatch;

    // Events
    public event Action<LobbyCreatedArgs>? OnLobbyCreated;
    public event Action<LobbyJoinedArgs>? OnLobbyJoined;
    public event Action? OnLobbyListRefreshed;
    public event Action<string>? OnError;
    public event Action<Guid>? OnPlayerKicked;
    public event Action? OnKickedFromLobby;
    public event Action<IReadOnlyList<LobbyPlayerInfo>>? OnPlayerListUpdated;
    public event Action<bool>? OnReadyStateChanged;
    public event Action? OnMatchStarted;

    // Properties
    public MatchmakingState State => _state;
    public string ErrorMessage => _errorMessage;
    public IReadOnlyList<LobbyListItem> AvailableLobbies => _availableLobbies;
    public bool IsRefreshingLobbyList => _isRefreshingLobbyList;
    public LobbyInfo? CurrentLobby => _currentLobby;
    public bool WasKicked => _wasKicked;
    public IReadOnlyList<LobbyPlayerInfo> LobbyPlayers => _lobbyPlayers;
    public bool IsHost => _isHost;
    public bool MatchStarted => _matchStarted;

    public OrleansMatchmakingProvider(IClusterClient client, ILogger logger)
    {
        _client = client;
        _log = logger.ForContext<OrleansMatchmakingProvider>();
    }

    public void Update()
    {
        CheckPendingRefresh();
        CheckPendingCreate();
        CheckPendingJoin();
        CheckPendingKick();
        CheckPendingLeave();
        CheckPendingHeartbeat();
        CheckPendingSetReady();
        CheckPendingStartMatch();
    }

    public void RequestLobbyListRefresh()
    {
        if (_pendingRefresh != null) return;

        _isRefreshingLobbyList = true;
        _pendingRefresh = RefreshLobbiesAsync();
    }

    private async Task RefreshLobbiesAsync()
    {
        try
        {
            var matchmakingGrain = _client.GetGrain<IMatchmakingGrain>("global");
            var matches = await matchmakingGrain.ListOpenMatches();

            _availableLobbies = matches.Select(m => new LobbyListItem
            {
                LobbyId = m.MatchId,
                HostName = m.HostName,
                PlayerCount = m.PlayerCount,
                MaxPlayers = m.MaxPlayers
            }).ToList();
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            _state = MatchmakingState.Error;
            OnError?.Invoke(_errorMessage);
        }
    }

    private void CheckPendingRefresh()
    {
        if (_pendingRefresh == null) return;
        if (!_pendingRefresh.IsCompleted) return;

        _isRefreshingLobbyList = false;
        _pendingRefresh = null;
        OnLobbyListRefreshed?.Invoke();
    }

    public void CreateLobby(string displayName, int port)
    {
        CreatePrivateLobby(displayName, port, null);
    }

    public void CreatePrivateLobby(string displayName, int port, string? password)
    {
        if (_pendingCreate != null) return;

        _state = MatchmakingState.CreatingLobby;
        _localPort = port;
        _pendingCreate = CreateLobbyAsync(displayName, port, password);
    }

    private async Task CreateLobbyAsync(string displayName, int port, string? password)
    {
        try
        {
            var matchmakingGrain = _client.GetGrain<IMatchmakingGrain>("global");
            string matchId = await matchmakingGrain.CreateMatch();

            var matchGrain = _client.GetGrain<IMatchGrain>(matchId);

            // Set visibility and password
            if (!string.IsNullOrEmpty(password))
            {
                await matchGrain.SetVisibility(LobbyVisibility.Private);
                await matchGrain.SetPassword(HashPassword(password));
            }

            // Add host player - no IP needed for direct Orleans calls
            var (playerId, success, error) = await matchGrain.AddPlayer(displayName, "", port, isHost: true);

            if (!success)
            {
                _errorMessage = error ?? "Failed to create lobby";
                _state = MatchmakingState.Error;
                return;
            }

            _currentMatchId = matchId;
            _localPlayerId = playerId;
            _isHost = true;

            var matchInfo = await matchGrain.GetMatchInfo();
            var host = matchInfo.Players.Find(p => p.IsHost);

            _currentLobby = new LobbyInfo
            {
                LobbyId = matchId,
                HostName = host?.DisplayName ?? displayName,
                HostIp = "", // Not needed for Orleans
                HostPort = port,
                PlayerCount = matchInfo.Players.Count,
                MaxPlayers = matchInfo.MaxPlayers,
                LocalPlayerId = playerId,
                HostPlayerId = matchInfo.HostPlayerId
            };

            _lobbyPlayers = matchInfo.Players.Select(p => new LobbyPlayerInfo
            {
                PlayerId = p.PlayerId,
                DisplayName = p.DisplayName,
                IsHost = p.IsHost,
                IsReady = p.IsReady
            }).ToList();

            _state = MatchmakingState.InLobby;
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            _state = MatchmakingState.Error;
        }
    }

    private void CheckPendingCreate()
    {
        if (_pendingCreate == null) return;
        if (!_pendingCreate.IsCompleted) return;

        _pendingCreate = null;

        if (_state == MatchmakingState.InLobby && _currentLobby.HasValue)
        {
            OnLobbyCreated?.Invoke(new LobbyCreatedArgs
            {
                LobbyId = _currentLobby.Value.LobbyId,
                LocalPlayerId = _localPlayerId,
                ListenPort = _localPort
            });
        }
        else if (_state == MatchmakingState.Error)
        {
            OnError?.Invoke(_errorMessage);
        }
    }

    public void JoinLobby(string lobbyId, string displayName, int port)
    {
        JoinLobby(lobbyId, displayName, port, null);
    }

    public void JoinLobby(string lobbyId, string displayName, int port, string? password)
    {
        if (_pendingJoin != null) return;

        _state = MatchmakingState.JoiningLobby;
        _localPort = port;
        _pendingJoin = JoinLobbyAsync(lobbyId, displayName, port, password);
    }

    private async Task JoinLobbyAsync(string lobbyId, string displayName, int port, string? password)
    {
        try
        {
            var matchGrain = _client.GetGrain<IMatchGrain>(lobbyId);

            // Validate password if needed
            if (!string.IsNullOrEmpty(password))
            {
                bool valid = await matchGrain.ValidatePassword(HashPassword(password));
                if (!valid)
                {
                    _errorMessage = "Invalid password";
                    _state = MatchmakingState.Error;
                    return;
                }
            }

            // Add player - no IP needed for direct Orleans calls
            var (playerId, success, error) = await matchGrain.AddPlayer(displayName, "", port, isHost: false);

            if (!success)
            {
                _errorMessage = error ?? "Failed to join lobby";
                _state = MatchmakingState.Error;
                return;
            }

            _currentMatchId = lobbyId;
            _localPlayerId = playerId;
            _isHost = false;

            var matchInfo = await matchGrain.GetMatchInfo();
            var host = matchInfo.Players.Find(p => p.IsHost);

            _currentLobby = new LobbyInfo
            {
                LobbyId = lobbyId,
                HostName = host?.DisplayName ?? "Unknown",
                HostIp = host?.PublicIp ?? "",
                HostPort = host?.Port ?? 0,
                PlayerCount = matchInfo.Players.Count,
                MaxPlayers = matchInfo.MaxPlayers,
                LocalPlayerId = playerId,
                HostPlayerId = matchInfo.HostPlayerId
            };

            _lobbyPlayers = matchInfo.Players.Select(p => new LobbyPlayerInfo
            {
                PlayerId = p.PlayerId,
                DisplayName = p.DisplayName,
                IsHost = p.IsHost,
                IsReady = p.IsReady
            }).ToList();

            _state = MatchmakingState.InLobby;
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            _state = MatchmakingState.Error;
        }
    }

    private void CheckPendingJoin()
    {
        if (_pendingJoin == null) return;
        if (!_pendingJoin.IsCompleted) return;

        _pendingJoin = null;

        if (_state == MatchmakingState.InLobby && _currentLobby.HasValue)
        {
            OnLobbyJoined?.Invoke(new LobbyJoinedArgs
            {
                LobbyId = _currentLobby.Value.LobbyId,
                LocalPlayerId = _localPlayerId,
                HostPlayerId = _currentLobby.Value.HostPlayerId,
                HostAddress = _currentLobby.Value.HostIp,
                HostPort = _currentLobby.Value.HostPort
            });
        }
        else if (_state == MatchmakingState.Error)
        {
            OnError?.Invoke(_errorMessage);
        }
    }

    public void KickPlayer(Guid targetPlayerId)
    {
        if (_pendingKick != null || !_isHost || string.IsNullOrEmpty(_currentMatchId)) return;

        _pendingKick = KickPlayerAsync(targetPlayerId);
    }

    private async Task KickPlayerAsync(Guid targetPlayerId)
    {
        try
        {
            var matchGrain = _client.GetGrain<IMatchGrain>(_currentMatchId);
            var (success, error) = await matchGrain.KickPlayer(_localPlayerId, targetPlayerId);

            if (!success)
            {
                _errorMessage = error ?? "Failed to kick player";
                OnError?.Invoke(_errorMessage);
            }
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            OnError?.Invoke(_errorMessage);
        }
    }

    private void CheckPendingKick()
    {
        if (_pendingKick == null) return;
        if (!_pendingKick.IsCompleted) return;

        _pendingKick = null;
    }

    public void LeaveLobby()
    {
        if (_pendingLeave != null || string.IsNullOrEmpty(_currentMatchId)) return;

        _pendingLeave = LeaveLobbyAsync();
    }

    private async Task LeaveLobbyAsync()
    {
        try
        {
            var matchGrain = _client.GetGrain<IMatchGrain>(_currentMatchId);
            await matchGrain.RemovePlayer(_localPlayerId);
        }
        catch
        {
            // Ignore errors on leave
        }
        finally
        {
            ResetLobbyState();
        }
    }

    private void CheckPendingLeave()
    {
        if (_pendingLeave == null) return;
        if (!_pendingLeave.IsCompleted) return;

        _pendingLeave = null;
    }

    public void SendHeartbeat()
    {
        if (_pendingHeartbeat != null || string.IsNullOrEmpty(_currentMatchId)) return;

        _pendingHeartbeat = SendHeartbeatAsync();
    }

    private async Task SendHeartbeatAsync()
    {
        try
        {
            var matchGrain = _client.GetGrain<IMatchGrain>(_currentMatchId);
            var (success, kicked, matchStarted, players) = await matchGrain.Heartbeat(_localPlayerId);

            if (!success && kicked)
            {
                _wasKicked = true;
                _state = MatchmakingState.Idle;
                _currentLobby = null;
                OnKickedFromLobby?.Invoke();
                return;
            }

            if (players != null)
            {
                _lobbyPlayers = players.Select(p => new LobbyPlayerInfo
                {
                    PlayerId = p.PlayerId,
                    DisplayName = p.DisplayName,
                    IsHost = p.IsHost,
                    IsReady = p.IsReady
                }).ToList();
                OnPlayerListUpdated?.Invoke(_lobbyPlayers);
            }

            if (matchStarted && !_matchStarted)
            {
                _matchStarted = true;
                OnMatchStarted?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            OnError?.Invoke(_errorMessage);
        }
    }

    private void CheckPendingHeartbeat()
    {
        if (_pendingHeartbeat == null) return;
        if (!_pendingHeartbeat.IsCompleted) return;

        _pendingHeartbeat = null;
    }

    public void SetReady(bool isReady)
    {
        if (_pendingSetReady != null || string.IsNullOrEmpty(_currentMatchId)) return;

        _pendingSetReady = SetReadyAsync(isReady);
    }

    private async Task SetReadyAsync(bool isReady)
    {
        try
        {
            _log.Debug("SetReady called: matchId={MatchId}, playerId={PlayerId}, isReady={IsReady}",
                _currentMatchId, _localPlayerId, isReady);
            var matchGrain = _client.GetGrain<IMatchGrain>(_currentMatchId);
            var (success, error) = await matchGrain.SetReady(_localPlayerId, isReady);

            if (success)
            {
                OnReadyStateChanged?.Invoke(isReady);
            }
            else
            {
                _errorMessage = error ?? "Failed to set ready state";
                OnError?.Invoke(_errorMessage);
            }
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            OnError?.Invoke(_errorMessage);
        }
    }

    private void CheckPendingSetReady()
    {
        if (_pendingSetReady == null) return;
        if (!_pendingSetReady.IsCompleted) return;

        _pendingSetReady = null;
    }

    public void RequestMatchStart()
    {
        if (_pendingStartMatch != null || !_isHost || string.IsNullOrEmpty(_currentMatchId)) return;

        _pendingStartMatch = RequestMatchStartAsync();
    }

    private async Task RequestMatchStartAsync()
    {
        try
        {
            var matchGrain = _client.GetGrain<IMatchGrain>(_currentMatchId);
            var (success, error) = await matchGrain.StartMatch(_localPlayerId);

            if (!success)
            {
                _errorMessage = error ?? "Failed to start match";
                OnError?.Invoke(_errorMessage);
            }
            // Match start is detected via heartbeat polling
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            OnError?.Invoke(_errorMessage);
        }
    }

    private void CheckPendingStartMatch()
    {
        if (_pendingStartMatch == null) return;
        if (!_pendingStartMatch.IsCompleted) return;

        _pendingStartMatch = null;
    }

    public void Cancel()
    {
        _state = MatchmakingState.Idle;
        _errorMessage = string.Empty;
    }

    public void Reset()
    {
        if (!string.IsNullOrEmpty(_currentMatchId))
        {
            LeaveLobby();
        }

        _state = MatchmakingState.Idle;
        _errorMessage = string.Empty;
        _availableLobbies.Clear();
        _isRefreshingLobbyList = false;
        _currentLobby = null;
        _wasKicked = false;
        _lobbyPlayers.Clear();
        _isHost = false;
        _matchStarted = false;
        _localPlayerId = Guid.Empty;
        _currentMatchId = string.Empty;
    }

    private void ResetLobbyState()
    {
        _currentLobby = null;
        _lobbyPlayers.Clear();
        _currentMatchId = string.Empty;
        _localPlayerId = Guid.Empty;
        _isHost = false;
        _matchStarted = false;
        _state = MatchmakingState.Idle;
    }

    private static string HashPassword(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public void Dispose()
    {
        // IClusterClient is managed externally
    }
}
