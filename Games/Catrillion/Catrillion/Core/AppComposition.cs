using System.Diagnostics;
using Pure.DI;
using Serilog;
using Steamworks;
using Catrillion.AppState;
using Catrillion.Core;
using Catrillion.OnlineServices;
using Catrillion.Stores;
using Networking;
using static Pure.DI.Lifetime;

namespace Catrillion;

partial class AppComposition
{

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
        .Bind().As(Singleton).To(ctx => {
            ctx.Inject(out NetworkConfig config);
            return new NetworkService(config);
        })
        .Bind().As(Singleton).To<LobbyCoordinator>()
        .Bind<IMatchmakingProvider>().As(Singleton).To(ctx => {
            ctx.Inject(out AppLaunch launch);
            return CreateMatchmakingProvider(launch);
        })
        .Bind().As(Singleton).To<MatchmakingCoordinator>()
        .Bind().As(Singleton).To<MatchmakingPoller>()
        .Bind().As(Singleton).To<LoadingManager>()
        .Bind().As(Singleton).To<ScreenManager>()
        // Bug reporting
        .Bind<BugReportService>().As(Singleton).To(_ => Application.BugReportServiceInstance!)
        .Bind().As(Singleton).To<BugReportCoordinator>()
        // Game runners
        .Bind().As(Singleton).To<Main>()
        .Bind().As(Singleton).To<DebugMain>()
        .Bind<IGameRunner>().As(Singleton).To(ctx => {
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
#elif NATIVE_AOT
        // NativeAOT build - use HTTP matchmaking (Orleans is not AOT compatible)
        var serverAddress = Environment.GetEnvironmentVariable("MATCHMAKING_SERVER");
        if (string.IsNullOrEmpty(serverAddress))
        {
            serverAddress = "45.76.79.231"; // Production server
        }
        var httpPort = Environment.GetEnvironmentVariable("MATCHMAKING_HTTP_PORT") ?? "5050";
        var baseUrl = $"http://{serverAddress}:{httpPort}";
        Log.Information("Using HTTP matchmaking provider: {BaseUrl}", baseUrl);
        return new HttpMatchmakingProvider(baseUrl, Log.Logger);
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
                Log.Warning("Steam init failed, falling back to HTTP: {Error}", ex.Message);
            }
        }

        // Use HTTP matchmaking (simpler than Orleans, works for all builds)
        var serverAddress = Environment.GetEnvironmentVariable("MATCHMAKING_SERVER") ?? "45.76.79.231";
        var httpPort = Environment.GetEnvironmentVariable("MATCHMAKING_HTTP_PORT") ?? "5050";
        var baseUrl = $"http://{serverAddress}:{httpPort}";
        Log.Information("Using HTTP matchmaking provider: {BaseUrl}", baseUrl);
        return new HttpMatchmakingProvider(baseUrl, Log.Logger);
#endif
    }
}

