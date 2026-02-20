using MatchmakingContracts.Models;
using Microsoft.Extensions.Logging;

namespace DerpTech2D.MatchmakingServer.Grains;

public sealed class MatchGrain : Grain, IMatchGrain
{
    private readonly ILogger<MatchGrain> _logger;
    private readonly List<PlayerInfo> _players = new();

    public MatchGrain(ILogger<MatchGrain> logger)
    {
        _logger = logger;
    }
    private MatchState _state = MatchState.Open;
    private string _hostIp = string.Empty;
    private int _hostPort;
    private DateTime _lastActivity = DateTime.UtcNow;
    private const int MaxPlayers = 8;
    private const int MatchTimeoutSeconds = 30;

    // New fields for player management and private lobbies
    private Guid _hostPlayerId = Guid.Empty;
    private LobbyVisibility _visibility = LobbyVisibility.Public;
    private string? _passwordHash;
    private const int PlayerTimeoutSeconds = 15;

    // Match start state
    private bool _matchStarted;

    public Task<MatchInfo> GetMatchInfo()
    {
        var info = new MatchInfo
        {
            MatchId = this.GetPrimaryKeyString(),
            State = _state,
            Players = _players.ToList(),
            HostIp = _hostIp,
            HostPort = _hostPort,
            MaxPlayers = MaxPlayers,
            Visibility = _visibility,
            HasPassword = !string.IsNullOrEmpty(_passwordHash),
            HostPlayerId = _hostPlayerId
        };
        return Task.FromResult(info);
    }

    public Task<bool> IsStale()
    {
        bool isStale = (DateTime.UtcNow - _lastActivity).TotalSeconds > MatchTimeoutSeconds;
        return Task.FromResult(isStale);
    }

    public Task Close()
    {
        _state = MatchState.Closed;
        return Task.CompletedTask;
    }

    public Task<(Guid playerId, bool success, string? error)> AddPlayer(string displayName, string publicIp, int port, bool isHost)
    {
        _lastActivity = DateTime.UtcNow;

        if (_state != MatchState.Open)
        {
            return Task.FromResult<(Guid, bool, string?)>((Guid.Empty, false, "Match is not open for joining"));
        }

        if (_players.Count >= MaxPlayers)
        {
            return Task.FromResult<(Guid, bool, string?)>((Guid.Empty, false, "Match is full"));
        }

        // Check for duplicate IP+Port (prevents same client joining twice)
        // Only check if IP is provided - Orleans clients pass empty string since there's no HTTP context
        if (!string.IsNullOrEmpty(publicIp) && _players.Any(p => p.PublicIp == publicIp && p.Port == port))
        {
            return Task.FromResult<(Guid, bool, string?)>((Guid.Empty, false, "A player with this address is already in the match"));
        }

        var playerId = Guid.NewGuid();
        var player = new PlayerInfo
        {
            PlayerId = playerId,
            DisplayName = displayName,
            PublicIp = publicIp,
            Port = port,
            IsHost = isHost,
            LastHeartbeat = DateTime.UtcNow
        };

        if (isHost)
        {
            _hostPlayerId = playerId;
            _hostIp = publicIp;
            _hostPort = port;
        }

        _players.Add(player);
        return Task.FromResult((playerId, true, (string?)null));
    }

    public Task<bool> RemovePlayer(Guid playerId)
    {
        int removedCount = _players.RemoveAll(p => p.PlayerId == playerId);

        if (_players.Count == 0)
        {
            _state = MatchState.Closed;
        }

        return Task.FromResult(removedCount > 0);
    }

    public Task<bool> IsOpen()
    {
        return Task.FromResult(_state == MatchState.Open && _players.Count < MaxPlayers);
    }

    // Player management methods

    public Task<(bool success, string? error)> KickPlayer(Guid requestingPlayerId, Guid targetPlayerId)
    {
        // Validate requester is host
        if (requestingPlayerId != _hostPlayerId)
        {
            return Task.FromResult((false, (string?)"Only the host can kick players"));
        }

        // Cannot kick self
        if (targetPlayerId == _hostPlayerId)
        {
            return Task.FromResult((false, (string?)"Host cannot kick themselves"));
        }

        var player = _players.Find(p => p.PlayerId == targetPlayerId);
        if (player == null)
        {
            return Task.FromResult((false, (string?)"Player not found"));
        }

        _players.Remove(player);
        _lastActivity = DateTime.UtcNow;

        return Task.FromResult((true, (string?)null));
    }

    public Task<(bool success, bool kicked, bool matchStarted, List<PlayerInfo>? players)> Heartbeat(Guid playerId)
    {
        var player = _players.Find(p => p.PlayerId == playerId);
        if (player == null)
        {
            // Player was kicked or removed
            return Task.FromResult((false, true, false, (List<PlayerInfo>?)null));
        }

        player.LastHeartbeat = DateTime.UtcNow;
        _lastActivity = DateTime.UtcNow;

        // Return current player list for efficient polling
        return Task.FromResult<(bool, bool, bool, List<PlayerInfo>?)>((true, false, _matchStarted, _players.ToList()));
    }

    public Task<List<Guid>> GetDisconnectedPlayers()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-PlayerTimeoutSeconds);
        var disconnected = _players
            .Where(p => p.LastHeartbeat < cutoff)
            .Select(p => p.PlayerId)
            .ToList();

        return Task.FromResult(disconnected);
    }

    // Private lobby methods

    public Task SetVisibility(LobbyVisibility visibility)
    {
        _visibility = visibility;
        return Task.CompletedTask;
    }

    public Task SetPassword(string? passwordHash)
    {
        _passwordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<bool> ValidatePassword(string? passwordHash)
    {
        if (string.IsNullOrEmpty(_passwordHash))
        {
            return Task.FromResult(true); // No password required
        }
        return Task.FromResult(_passwordHash == passwordHash);
    }

    public Task<LobbyVisibility> GetVisibility()
    {
        return Task.FromResult(_visibility);
    }

    // Ready state and match start methods

    public Task<(bool success, string? error)> SetReady(Guid playerId, bool isReady)
    {
        _logger.LogDebug("SetReady called: matchId={MatchId}, playerId={PlayerId}, playerCount={PlayerCount}",
            this.GetPrimaryKeyString(), playerId, _players.Count);

        var player = _players.Find(p => p.PlayerId == playerId);
        if (player == null)
        {
            _logger.LogWarning("Player {PlayerId} not found in match {MatchId}", playerId, this.GetPrimaryKeyString());
            return Task.FromResult((false, (string?)"Player not found"));
        }

        player.IsReady = isReady;
        _lastActivity = DateTime.UtcNow;
        return Task.FromResult((true, (string?)null));
    }

    public Task<(bool success, string? error)> StartMatch(Guid requestingPlayerId)
    {
        // Validate requester is host
        if (requestingPlayerId != _hostPlayerId)
        {
            return Task.FromResult((false, (string?)"Only the host can start the match"));
        }

        // Check all players are ready
        if (!_players.All(p => p.IsReady))
        {
            return Task.FromResult((false, (string?)"Not all players are ready"));
        }

        // Start the match
        _matchStarted = true;
        _state = MatchState.Closed; // No more joins allowed
        _lastActivity = DateTime.UtcNow;

        return Task.FromResult((true, (string?)null));
    }
}

