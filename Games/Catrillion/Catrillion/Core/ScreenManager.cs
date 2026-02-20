using System;
using Catrillion.AppState;
using Catrillion.OnlineServices;
using Catrillion.Stores;
using Catrillion.UI;

namespace Catrillion.Core;

/// <summary>
/// Centralized screen state machine.
/// Dispatches update/draw calls to the current screen.
/// Polls network events each frame.
/// </summary>
public sealed class ScreenManager : IDisposable
{
    private readonly RootStore _store;
    private readonly LobbyCoordinator _lobbyCoordinator;
    private readonly BugReportCoordinator _bugReportCoordinator;

    // Screens
    private readonly MainMenuScreen _mainMenu;
    private readonly MatchmakingScreen _matchmaking;
    private readonly LobbyScreen _lobby;
    private readonly LoadingScreen _loading;
    private readonly CountdownScreen _countdown;
    private readonly InGameScreen _inGame;
    private readonly GameOverScreen _gameOver;

    private ApplicationState _previousScreen;

    public ScreenManager(
        RootStore store,
        LobbyCoordinator lobbyCoordinator,
        BugReportCoordinator bugReportCoordinator,
        AppEventBus eventBus,
        InputStore inputStore)
    {
        _store = store;
        _lobbyCoordinator = lobbyCoordinator;
        _bugReportCoordinator = bugReportCoordinator;

        // Create screens - pass eventBus to screens that publish events
        _mainMenu = new MainMenuScreen(store, eventBus);
        _matchmaking = new MatchmakingScreen(store, eventBus);
        _lobby = new LobbyScreen(store, eventBus);
        _loading = new LoadingScreen(store);
        _countdown = new CountdownScreen(store, eventBus);
        _inGame = new InGameScreen(store, eventBus, inputStore);
        _gameOver = new GameOverScreen(store, eventBus);

        _previousScreen = store.App.CurrentScreen.Value;
    }

    /// <summary>
    /// Update the current screen. Handles screen transitions and network polling.
    /// </summary>
    public void Update(float deltaTime)
    {
        // Poll network events
        _lobbyCoordinator.Poll();

        var currentScreen = _store.App.CurrentScreen.Value;

        // Handle screen transitions
        if (currentScreen != _previousScreen)
        {
            OnScreenExit(_previousScreen);
            OnScreenEnter(currentScreen);
            _previousScreen = currentScreen;
        }

        // Update current screen
        switch (currentScreen)
        {
            case ApplicationState.MainMenu:
                _mainMenu.Update(deltaTime);
                break;
            case ApplicationState.Matchmaking:
                _matchmaking.Update(deltaTime);
                break;
            case ApplicationState.Lobby:
                _lobby.Update(deltaTime);
                break;
            case ApplicationState.Loading:
                _loading.Update(deltaTime);
                break;
            case ApplicationState.Countdown:
                _countdown.Update(deltaTime);
                break;
            case ApplicationState.InGame:
                _inGame.Update(deltaTime);
                break;
            case ApplicationState.GameOver:
                _gameOver.Update(deltaTime);
                break;
        }
    }

    /// <summary>
    /// Draw the current screen.
    /// </summary>
    public void Draw(float deltaTime)
    {
        var currentScreen = _store.App.CurrentScreen.Value;

        switch (currentScreen)
        {
            case ApplicationState.MainMenu:
                _mainMenu.Draw(deltaTime);
                break;
            case ApplicationState.Matchmaking:
                _matchmaking.Draw(deltaTime);
                break;
            case ApplicationState.Lobby:
                _lobby.Draw(deltaTime);
                break;
            case ApplicationState.Loading:
                _loading.Draw(deltaTime);
                break;
            case ApplicationState.Countdown:
                _countdown.Draw(deltaTime);
                break;
            case ApplicationState.InGame:
                _inGame.Draw(deltaTime);
                break;
            case ApplicationState.GameOver:
                _gameOver.Draw(deltaTime);
                break;
        }
    }

    private void OnScreenEnter(ApplicationState screen)
    {
        switch (screen)
        {
            case ApplicationState.MainMenu:
                _mainMenu.OnEnter();
                break;
            case ApplicationState.Matchmaking:
                _matchmaking.OnEnter();
                break;
            case ApplicationState.Lobby:
                _lobby.OnEnter();
                break;
            case ApplicationState.Loading:
                _loading.OnEnter();
                break;
            case ApplicationState.Countdown:
                _countdown.OnEnter();
                break;
            case ApplicationState.InGame:
                _inGame.OnEnter();
                break;
            case ApplicationState.GameOver:
                _gameOver.OnEnter();
                break;
        }
    }

    private void OnScreenExit(ApplicationState screen)
    {
        switch (screen)
        {
            case ApplicationState.MainMenu:
                _mainMenu.OnExit();
                break;
            case ApplicationState.Matchmaking:
                _matchmaking.OnExit();
                break;
            case ApplicationState.Lobby:
                _lobby.OnExit();
                break;
            case ApplicationState.Loading:
                _loading.OnExit();
                break;
            case ApplicationState.Countdown:
                _countdown.OnExit();
                break;
            case ApplicationState.InGame:
                _inGame.OnExit();
                break;
            case ApplicationState.GameOver:
                _gameOver.OnExit();
                break;
        }
    }

    /// <summary>
    /// Handle window resize - notify game store if game exists.
    /// </summary>
    public void OnWindowResized(int width, int height)
    {
        _store.Game.UpdateViewSize(width, height);
    }

    public void Dispose()
    {
        _bugReportCoordinator.Dispose();
    }
}
