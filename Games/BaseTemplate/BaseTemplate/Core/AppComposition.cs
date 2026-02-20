using System.Diagnostics;
using Orleans;
using Pure.DI;
using Serilog;
using Steamworks;
using BaseTemplate.GameApp.AppState;
using BaseTemplate.GameApp.Core;
using BaseTemplate.GameApp.Stores;
using BaseTemplate.Infrastructure.Networking;
using Networking;
using static Pure.DI.Lifetime;

namespace BaseTemplate;

partial class AppComposition
{
    // Orleans client stored separately (nullable, not passed through DI)
    private static IClusterClient? _orleansClient;

    public static void SetOrleansClient(IClusterClient? client) => _orleansClient = client;

    [Conditional("DI")]
    static void Setup() => DI.Setup()
        .Arg<AppLaunch>("appLaunch")
        .Arg<NetworkConfig>("networkConfig")
        // Stores
        .Bind().As(Singleton).To<AppStore>()
        .Bind().As(Singleton).To<MatchmakingStore>()
        .Bind().As(Singleton).To<NetworkStore>()
        .Bind().As(Singleton).To<LobbyStore>()
        .Bind().As(Singleton).To<GameStore>()
        .Bind().As(Singleton).To<GameplayStore>()
        .Bind().As(Singleton).To<InputStore>()
        .Bind().As(Singleton).To<RootStore>()
        // Services
        .Bind<ILogger>().As(Singleton).To(_ => Log.Logger)
        .Bind().As(Singleton).To<AppEventBus>()
        .Bind().As(Singleton).To(ctx =>
        {
            ctx.Inject(out NetworkConfig config);
            return new NetworkService(config);
        })
        .Bind().As(Singleton).To<LobbyCoordinator>()
        .Bind<IMatchmakingProvider>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out AppLaunch launch);
            return CreateMatchmakingProvider(launch);
        })
        .Bind().As(Singleton).To<MatchmakingCoordinator>()
        .Bind().As(Singleton).To<MatchmakingPoller>()
        .Bind().As(Singleton).To<LoadingManager>()
        .Bind().As(Singleton).To<ScreenManager>()
        // Bug reporting
        .Bind<BugReportService>().As(Singleton).To(_ => Program.BugReportServiceInstance!)
        .Bind().As(Singleton).To<BugReportCoordinator>()
        // Game runners
        .Bind().As(Singleton).To<Main>()
        .Bind().As(Singleton).To<DebugMain>()
        .Bind<IGameRunner>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out AppLaunch launch);
            ctx.Inject(out Main main);
            ctx.Inject(out DebugMain debugMain);
            return launch.SkipMenu ? (IGameRunner)debugMain : main;
        })
        .Root<IGameRunner>("GameRunner");

    private static IMatchmakingProvider CreateMatchmakingProvider(AppLaunch launch)
    {
#if STEAM_BUILD
        // Production Steam build - always use Steam
        if (!SteamClient.IsValid)
        {
            SteamClient.Init(SteamMatchmakingConfig.AppId, true);
        }
        return new SteamMatchmakingProvider();
#else
        // Development: check launch flag for Steam matchmaking
        if (launch.UseSteamMatchmaking)
        {
            try
            {
                if (!SteamClient.IsValid)
                {
                    SteamClient.Init(SteamMatchmakingConfig.AppId, true);
                }
                return new SteamMatchmakingProvider();
            }
            catch (Exception ex)
            {
                Log.Warning("Steam init failed, falling back to Orleans: {Error}", ex.Message);
            }
        }

        // Fall back to Orleans
        if (_orleansClient == null)
        {
            throw new InvalidOperationException(
                "No matchmaking provider available. Use --steam-matchmaking or ensure Orleans server is running.");
        }
        return new OrleansMatchmakingProvider(_orleansClient, Log.Logger);
#endif
    }
}

