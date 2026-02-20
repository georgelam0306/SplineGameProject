using DerpTech2D.MatchmakingServer.Grains;

namespace DerpTech2D.MatchmakingServer.Services;

public sealed class MatchCleanupService : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<MatchCleanupService> _logger;
    private const int CleanupIntervalSeconds = 15;

    public MatchCleanupService(IGrainFactory grainFactory, ILogger<MatchCleanupService> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Match cleanup service started, interval: {Interval}s", CleanupIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(CleanupIntervalSeconds), stoppingToken);

            try
            {
                var matchmakingGrain = _grainFactory.GetGrain<IMatchmakingGrain>("global");
                var matches = await matchmakingGrain.ListOpenMatches();
                _logger.LogDebug("Cleanup check: {Count} active matches", matches.Count);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error during match cleanup");
            }
        }
    }
}

