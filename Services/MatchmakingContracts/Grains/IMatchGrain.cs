using MatchmakingContracts.Models;

namespace MatchmakingContracts.Grains;

public interface IMatchGrain : IGrainWithStringKey
{
    // Match info
    Task<MatchInfo> GetMatchInfo();
    Task<bool> IsOpen();
    Task<bool> IsStale();
    Task Close();

    // Player management
    Task<(Guid playerId, bool success, string? error)> AddPlayer(string displayName, string publicIp, int port, bool isHost);
    Task<bool> RemovePlayer(Guid playerId);
    Task<(bool success, string? error)> KickPlayer(Guid requestingPlayerId, Guid targetPlayerId);
    Task<(bool success, bool kicked, bool matchStarted, List<PlayerInfo>? players)> Heartbeat(Guid playerId);
    Task<List<Guid>> GetDisconnectedPlayers();

    // Ready state and match start
    Task<(bool success, string? error)> SetReady(Guid playerId, bool isReady);
    Task<(bool success, string? error)> StartMatch(Guid requestingPlayerId);

    // Private lobby support
    Task SetVisibility(LobbyVisibility visibility);
    Task SetPassword(string? passwordHash);
    Task<bool> ValidatePassword(string? passwordHash);
    Task<LobbyVisibility> GetVisibility();
}
