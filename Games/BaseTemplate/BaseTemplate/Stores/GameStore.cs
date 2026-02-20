using System;
using Raylib_cs;
using Serilog;
using BaseTemplate.GameApp.Stores;
using Networking;

using GameAppAlias = BaseTemplate.GameApp;

namespace BaseTemplate.GameApp.AppState;

/// <summary>
/// Game instance management store.
/// Provides explicit lifecycle control for the Game instance.
/// </summary>
public sealed class GameStore : IDisposable
{
    private readonly ILogger _logger;
    private readonly AppStore _appStore;
    private readonly LobbyStore _lobbyStore;
    private readonly NetworkStore _networkStore;
    private readonly NetworkService _networkService;
    private readonly AppEventBus _eventBus;
    private readonly InputStore _inputStore;
    private readonly GameplayStore _gameplayStore;
    private GameComposition? _composition;
    private Game? _currentGame;

    public GameStore(
        ILogger logger,
        AppStore appStore,
        LobbyStore lobbyStore,
        NetworkStore networkStore,
        NetworkService networkService,
        AppEventBus eventBus,
        InputStore inputStore,
        GameplayStore gameplayStore)
    {
        _logger = logger;
        _appStore = appStore;
        _lobbyStore = lobbyStore;
        _networkStore = networkStore;
        _networkService = networkService;
        _eventBus = eventBus;
        _inputStore = inputStore;
        _gameplayStore = gameplayStore;
    }

    /// <summary>
    /// The current Game instance (null when no game is active).
    /// </summary>
    public Game? CurrentGame => _currentGame;

    /// <summary>
    /// Whether a game is currently created.
    /// </summary>
    public bool IsGameCreated => _currentGame != null;

    /// <summary>
    /// Creates a new game using current app state.
    /// Gathers player configuration from lobby and app stores.
    /// Disposes any existing game first.
    /// </summary>
    public void CreateGame()
    {
        var lobby = _lobbyStore.State.Value;
        var localClient = _appStore.LocalClient;

        var config = new PlayerConfig(
            MaxPlayers: LobbyState.MaxPlayers,
            PlayerCount: lobby.PlayerCount,
            LocalPlayerSlot: localClient.PlayerSlot
        );

        bool isCoordinator = _networkStore.IsCoordinator;

        CreateGameInternal(config, isCoordinator, lobby);
    }

    private void CreateGameInternal(PlayerConfig config, bool isCoordinator, LobbyState lobbyState)
    {
        DisposeGame();

        var replayConfig = new ReplayConfig(Program.ReplayFilePath, Program.RecordFilePath);

        // Extract session seed from lobby state - use lobbyState if available, fallback for single-player
        var sessionSeed = new SessionSeed(lobbyState.SessionSeed != 0
            ? lobbyState.SessionSeed
            : Environment.TickCount);

        var initialCoordinator = new InitialCoordinator(isCoordinator);

        _composition = new GameComposition(config, _networkService, replayConfig, sessionSeed, initialCoordinator, _eventBus, _logger, _inputStore, _gameplayStore, _networkStore);
        _currentGame = _composition.Game;

    }

    /// <summary>
    /// Disposes the current game and clears references.
    /// </summary>
    public void DisposeGame()
    {
        _currentGame?.Dispose();
        _currentGame = null;
        _composition = null;
    }

    /// <summary>
    /// Updates the camera view size (call on window resize).
    /// With pixel-perfect rendering, the native resolution is fixed, so this is a no-op.
    /// </summary>
    public void UpdateViewSize(int width, int height)
    {
        // No-op: pixel-perfect rendering uses fixed native resolution
    }

    public void Dispose()
    {
        DisposeGame();
    }
}
