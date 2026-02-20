using MatchmakingContracts.Models;

namespace DerpTech2D.MatchmakingServer.Grains;

public sealed class MatchmakingGrain : Grain, IMatchmakingGrain
{
    private readonly HashSet<string> _activeMatches = new();
    private readonly IGrainFactory _grainFactory;
    private readonly Random _random = new();

    // Characters for invite codes (no O/0/I/1 to avoid confusion)
    private const string InviteCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public MatchmakingGrain(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public Task<string> CreateMatch()
    {
        // Generate 6-char invite code
        string matchId;
        do
        {
            var chars = new char[6];
            for (int i = 0; i < 6; i++)
            {
                chars[i] = InviteCodeChars[_random.Next(InviteCodeChars.Length)];
            }
            matchId = new string(chars);
        } while (_activeMatches.Contains(matchId)); // Ensure uniqueness

        _activeMatches.Add(matchId);
        return Task.FromResult(matchId);
    }

    public async Task<List<MatchListItem>> ListOpenMatches()
    {
        var openMatches = new List<MatchListItem>();
        var closedMatches = new List<string>();

        foreach (string matchId in _activeMatches)
        {
            var matchGrain = _grainFactory.GetGrain<IMatchGrain>(matchId);

            bool isStale = await matchGrain.IsStale();
            if (isStale)
            {
                await matchGrain.Close();
                closedMatches.Add(matchId);
                continue;
            }

            // Auto-remove disconnected players (heartbeat timeout)
            var disconnected = await matchGrain.GetDisconnectedPlayers();
            foreach (var playerId in disconnected)
            {
                await matchGrain.RemovePlayer(playerId);
            }

            bool isOpen = await matchGrain.IsOpen();
            var visibility = await matchGrain.GetVisibility();

            // Only return PUBLIC lobbies in the list
            if (isOpen && visibility == LobbyVisibility.Public)
            {
                var matchInfo = await matchGrain.GetMatchInfo();
                var host = matchInfo.Players.Find(p => p.IsHost);

                openMatches.Add(new MatchListItem
                {
                    MatchId = matchId,
                    HostName = host?.DisplayName ?? "Unknown",
                    PlayerCount = matchInfo.Players.Count,
                    MaxPlayers = matchInfo.MaxPlayers
                });
            }
            else if (!isOpen)
            {
                var matchInfo = await matchGrain.GetMatchInfo();
                if (matchInfo.State == MatchState.Closed)
                {
                    closedMatches.Add(matchId);
                }
            }
        }

        foreach (string closedMatchId in closedMatches)
        {
            _activeMatches.Remove(closedMatchId);
        }

        return openMatches;
    }

    public Task RegisterMatch(string matchId)
    {
        _activeMatches.Add(matchId);
        return Task.CompletedTask;
    }

    public Task UnregisterMatch(string matchId)
    {
        _activeMatches.Remove(matchId);
        return Task.CompletedTask;
    }
}

