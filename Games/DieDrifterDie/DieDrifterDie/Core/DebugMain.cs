using Serilog;
using DieDrifterDie.GameApp.AppState;
using DieDrifterDie.Infrastructure.Networking;
using Networking;

namespace DieDrifterDie.GameApp.Core;

/// <summary>
/// Debug/development game runner that sets up lobby state for skip-menu mode,
/// then delegates to Main for the actual game loop.
/// </summary>
public class DebugMain : IGameRunner
{
    private readonly ILogger _log;
    private readonly AppLaunch _appLaunch;
    private readonly Main _main;
    private readonly RootStore _rootStore;
    private readonly LobbyCoordinator _lobbyCoordinator;

    public DebugMain(
        ILogger logger,
        AppLaunch appLaunch,
        Main main,
        RootStore rootStore,
        LobbyCoordinator lobbyCoordinator)
    {
        _log = logger.ForContext<DebugMain>();
        _appLaunch = appLaunch;
        _main = main;
        _rootStore = rootStore;
        _lobbyCoordinator = lobbyCoordinator;
    }

    public void Run()
    {
        SetupLobbyForSkipMenu();
        _main.Run();
    }

    private void SetupLobbyForSkipMenu()
    {
        string? netHostEnv = Environment.GetEnvironmentVariable("NET_HOST");
        bool isHost = string.Equals(netHostEnv, "true", StringComparison.OrdinalIgnoreCase);

        // Read port from environment or use default
        string? netPortEnv = Environment.GetEnvironmentVariable("NET_PORT");
        int port = int.TryParse(netPortEnv, out int parsedPort) && parsedPort > 0
            ? parsedPort
            : NetworkService.DefaultPort;

        int localPlayerSlot = _appLaunch.LocalPlayerSlot;

        // If slot wasn't explicitly set, use defaults
        if (localPlayerSlot < 0)
        {
            string? netSlotEnv = Environment.GetEnvironmentVariable("NET_PLAYER_SLOT");
            if (int.TryParse(netSlotEnv, out int parsedSlot) && parsedSlot >= 0)
            {
                localPlayerSlot = parsedSlot;
            }
            else
            {
                localPlayerSlot = isHost ? 0 : 1;
            }
        }

        _rootStore.App.LocalClient.PlayerSlot = localPlayerSlot;
        _rootStore.App.LocalClient.DisplayName = isHost ? "Host" : $"Player {localPlayerSlot + 1}";

        _log.Information("DebugMain: StartNetworkConnection expectedPlayerCount={ExpectedPlayerCount}, localPlayerSlot={LocalPlayerSlot}, isHost={IsHost}, port={Port}",
            _appLaunch.ExpectedPlayerCount, localPlayerSlot, isHost, port);

        if (_appLaunch.ExpectedPlayerCount == 1)
        {
            _lobbyCoordinator.StartLocalGame();
        }
        else if (isHost)
        {
            _lobbyCoordinator.HostGame(port);
            _lobbyCoordinator.ToggleReady();
        }
        else
        {
            _lobbyCoordinator.JoinGame("127.0.0.1", port);
        }
    }
}
