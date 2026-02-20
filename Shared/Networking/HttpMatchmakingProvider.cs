using System.Net.Http.Json;
using System.Text.Json;
using Serilog;

namespace Networking;

/// <summary>
/// HTTP-based matchmaking provider that calls REST endpoints.
/// AOT-compatible alternative to OrleansMatchmakingProvider.
/// </summary>
public sealed class HttpMatchmakingProvider : IMatchmakingProvider
{
    private readonly ILogger _log;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

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
    private string _localPublicIp = string.Empty;

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

    public HttpMatchmakingProvider(string baseUrl, ILogger logger)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _log = logger.ForContext<HttpMatchmakingProvider>();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = MatchmakingJsonContext.Default
        };
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
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/lobbies");
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ListLobbiesResponse>(MatchmakingJsonContext.Default.ListLobbiesResponse);

            if (result != null)
            {
                _availableLobbies = result.Lobbies.Select(l => new LobbyListItem
                {
                    LobbyId = l.LobbyId,
                    HostName = l.HostName,
                    PlayerCount = l.PlayerCount,
                    MaxPlayers = l.MaxPlayers
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to refresh lobby list");
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
            // Get public IP for P2P connections
            await FetchPublicIpAsync();

            var request = new CreateLobbyRequest
            {
                DisplayName = displayName,
                PublicIp = _localPublicIp,
                Port = port,
                IsPrivate = !string.IsNullOrEmpty(password),
                PasswordHash = !string.IsNullOrEmpty(password) ? HashPassword(password) : null
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/lobbies",
                request,
                MatchmakingJsonContext.Default.CreateLobbyRequest);

            var result = await response.Content.ReadFromJsonAsync<CreateLobbyResponse>(
                MatchmakingJsonContext.Default.CreateLobbyResponse);

            if (result == null || !result.Success)
            {
                _errorMessage = result?.Error ?? "Failed to create lobby";
                _state = MatchmakingState.Error;
                return;
            }

            _currentMatchId = result.LobbyId;
            _localPlayerId = result.PlayerId;
            _isHost = true;

            if (result.LobbyInfo != null)
            {
                _currentLobby = new LobbyInfo
                {
                    LobbyId = result.LobbyId,
                    HostName = result.LobbyInfo.HostName,
                    HostIp = result.LobbyInfo.HostIp,
                    HostPort = result.LobbyInfo.HostPort,
                    PlayerCount = result.LobbyInfo.PlayerCount,
                    MaxPlayers = result.LobbyInfo.MaxPlayers,
                    LocalPlayerId = result.PlayerId,
                    HostPlayerId = result.LobbyInfo.HostPlayerId
                };

                _lobbyPlayers = result.LobbyInfo.Players.Select(p => new LobbyPlayerInfo
                {
                    PlayerId = p.PlayerId,
                    DisplayName = p.DisplayName,
                    IsHost = p.IsHost,
                    IsReady = p.IsReady
                }).ToList();
            }

            _state = MatchmakingState.InLobby;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to create lobby");
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
            // Get public IP for P2P connections
            await FetchPublicIpAsync();

            var request = new JoinLobbyRequest
            {
                DisplayName = displayName,
                PublicIp = _localPublicIp,
                Port = port,
                PasswordHash = !string.IsNullOrEmpty(password) ? HashPassword(password) : null
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/lobbies/{lobbyId}/join",
                request,
                MatchmakingJsonContext.Default.JoinLobbyRequest);

            var result = await response.Content.ReadFromJsonAsync<JoinLobbyResponse>(
                MatchmakingJsonContext.Default.JoinLobbyResponse);

            if (result == null || !result.Success)
            {
                _errorMessage = result?.Error ?? "Failed to join lobby";
                _state = MatchmakingState.Error;
                return;
            }

            _currentMatchId = lobbyId;
            _localPlayerId = result.PlayerId;
            _isHost = false;

            if (result.LobbyInfo != null)
            {
                _currentLobby = new LobbyInfo
                {
                    LobbyId = lobbyId,
                    HostName = result.LobbyInfo.HostName,
                    HostIp = result.LobbyInfo.HostIp,
                    HostPort = result.LobbyInfo.HostPort,
                    PlayerCount = result.LobbyInfo.PlayerCount,
                    MaxPlayers = result.LobbyInfo.MaxPlayers,
                    LocalPlayerId = result.PlayerId,
                    HostPlayerId = result.LobbyInfo.HostPlayerId
                };

                _lobbyPlayers = result.LobbyInfo.Players.Select(p => new LobbyPlayerInfo
                {
                    PlayerId = p.PlayerId,
                    DisplayName = p.DisplayName,
                    IsHost = p.IsHost,
                    IsReady = p.IsReady
                }).ToList();
            }

            _state = MatchmakingState.InLobby;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to join lobby");
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
            var request = new KickPlayerRequest
            {
                RequestingPlayerId = _localPlayerId,
                TargetPlayerId = targetPlayerId
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/lobbies/{_currentMatchId}/kick",
                request,
                MatchmakingJsonContext.Default.KickPlayerRequest);

            var result = await response.Content.ReadFromJsonAsync<KickPlayerResponse>(
                MatchmakingJsonContext.Default.KickPlayerResponse);

            if (result == null || !result.Success)
            {
                _errorMessage = result?.Error ?? "Failed to kick player";
                OnError?.Invoke(_errorMessage);
            }
            else
            {
                OnPlayerKicked?.Invoke(targetPlayerId);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to kick player");
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
            var request = new LeaveLobbyRequest { PlayerId = _localPlayerId };

            await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/lobbies/{_currentMatchId}/leave",
                request,
                MatchmakingJsonContext.Default.LeaveLobbyRequest);
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
            var request = new HeartbeatRequest { PlayerId = _localPlayerId };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/lobbies/{_currentMatchId}/heartbeat",
                request,
                MatchmakingJsonContext.Default.HeartbeatRequest);

            var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>(
                MatchmakingJsonContext.Default.HeartbeatResponse);

            if (result == null) return;

            if (!result.Success && result.Kicked)
            {
                _wasKicked = true;
                _state = MatchmakingState.Idle;
                _currentLobby = null;
                OnKickedFromLobby?.Invoke();
                return;
            }

            if (result.Players.Count > 0)
            {
                _lobbyPlayers = result.Players.Select(p => new LobbyPlayerInfo
                {
                    PlayerId = p.PlayerId,
                    DisplayName = p.DisplayName,
                    IsHost = p.IsHost,
                    IsReady = p.IsReady
                }).ToList();
                OnPlayerListUpdated?.Invoke(_lobbyPlayers);
            }

            if (result.MatchStarted && !_matchStarted)
            {
                _matchStarted = true;
                OnMatchStarted?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Heartbeat failed");
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
            var request = new SetReadyRequest
            {
                PlayerId = _localPlayerId,
                IsReady = isReady
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/lobbies/{_currentMatchId}/ready",
                request,
                MatchmakingJsonContext.Default.SetReadyRequest);

            var result = await response.Content.ReadFromJsonAsync<SetReadyResponse>(
                MatchmakingJsonContext.Default.SetReadyResponse);

            if (result != null && result.Success)
            {
                OnReadyStateChanged?.Invoke(isReady);
            }
            else
            {
                _errorMessage = result?.Error ?? "Failed to set ready state";
                OnError?.Invoke(_errorMessage);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to set ready state");
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
            var request = new StartMatchRequest { PlayerId = _localPlayerId };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/lobbies/{_currentMatchId}/start",
                request,
                MatchmakingJsonContext.Default.StartMatchRequest);

            var result = await response.Content.ReadFromJsonAsync<StartMatchResponse>(
                MatchmakingJsonContext.Default.StartMatchResponse);

            if (result == null || !result.Success)
            {
                _errorMessage = result?.Error ?? "Failed to start match";
                OnError?.Invoke(_errorMessage);
            }
            // Match start is detected via heartbeat polling
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to start match");
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

    private async Task FetchPublicIpAsync()
    {
        if (!string.IsNullOrEmpty(_localPublicIp)) return;

        try
        {
            // Use a simple IP echo service
            _localPublicIp = await _httpClient.GetStringAsync("https://api.ipify.org");
            _localPublicIp = _localPublicIp.Trim();
        }
        catch
        {
            // Fallback - server may be able to detect IP from request
            _localPublicIp = "";
        }
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
        _httpClient.Dispose();
    }
}
