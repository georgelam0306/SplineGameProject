using System;
using R3;

namespace DieDrifterDie.GameApp.AppState;

/// <summary>
/// App-level state store.
/// Manages screen state and local client identity.
/// Lobby, network, and game state are in dedicated stores.
/// </summary>
public sealed class AppStore : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    /// <summary>
    /// The local client (this application instance).
    /// </summary>
    public LocalClient LocalClient { get; }

    /// <summary>
    /// Current screen being displayed.
    /// </summary>
    public ReactiveProperty<ApplicationState> CurrentScreen { get; }

    public AppStore()
    {
        LocalClient = new LocalClient();
        CurrentScreen = new ReactiveProperty<ApplicationState>(ApplicationState.MainMenu);

        _disposables.Add(CurrentScreen);
    }

    /// <summary>
    /// Transition to matchmaking screen.
    /// </summary>
    public void TransitionToMatchmaking()
    {
        CurrentScreen.Value = ApplicationState.Matchmaking;
    }

    /// <summary>
    /// Transition to lobby screen.
    /// </summary>
    public void TransitionToLobby()
    {
        CurrentScreen.Value = ApplicationState.Lobby;
    }

    /// <summary>
    /// Transition to loading screen.
    /// </summary>
    public void TransitionToLoading()
    {
        CurrentScreen.Value = ApplicationState.Loading;
    }

    /// <summary>
    /// Transition to countdown screen.
    /// </summary>
    public void TransitionToCountdown()
    {
        CurrentScreen.Value = ApplicationState.Countdown;
    }

    /// <summary>
    /// Transition to active game.
    /// </summary>
    public void TransitionToGame()
    {
        CurrentScreen.Value = ApplicationState.InGame;
    }

    /// <summary>
    /// Transition back to main menu.
    /// </summary>
    public void TransitionToMainMenu()
    {
        LocalClient.PlayerSlot = -1;
        CurrentScreen.Value = ApplicationState.MainMenu;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
