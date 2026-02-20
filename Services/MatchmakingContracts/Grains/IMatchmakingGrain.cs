using MatchmakingContracts.Models;

namespace MatchmakingContracts.Grains;

public interface IMatchmakingGrain : IGrainWithStringKey
{
    Task<string> CreateMatch();
    Task<List<MatchListItem>> ListOpenMatches();
    Task RegisterMatch(string matchId);
    Task UnregisterMatch(string matchId);
}
