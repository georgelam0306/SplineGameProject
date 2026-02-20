using System;
using R3;
using DieDrifterDie.GameApp.AppState;
using DieDrifterDie.Infrastructure.Networking;

namespace DieDrifterDie.GameApp.Core;

/// <summary>
/// Handles game creation during the loading phase.
/// Separates game creation orchestration from UI concerns.
/// </summary>
public sealed class LoadingManager : IDisposable
{
    private readonly RootStore _store;
    private readonly LobbyCoordinator _lobbyCoordinator;
    private readonly CompositeDisposable _disposables = new();
    private bool _wasActive;
    private bool _gameCreated;

    public LoadingManager(RootStore store, LobbyCoordinator lobbyCoordinator, AppEventBus eventBus)
    {
        _store = store;
        _lobbyCoordinator = lobbyCoordinator;

        // Subscribe to game events
        eventBus.CountdownRequested
            .Subscribe(_ => _lobbyCoordinator.InitiateCountdown())
            .AddTo(_disposables);

        eventBus.SessionSeedGenerated
            .Subscribe(seed =>
            {
                var updatedLobby = _store.Lobby.State.Value with { SessionSeed = seed };
                _store.Lobby.SetState(updatedLobby);
                _lobbyCoordinator.SendLobbySync();
            })
            .AddTo(_disposables);

        eventBus.CountdownComplete
            .Subscribe(_ => _store.App.TransitionToGame())
            .AddTo(_disposables);

        eventBus.ReturnToMainMenuRequested
            .Subscribe(_ => HandleReturnToMainMenu())
            .AddTo(_disposables);
    }

    private void HandleReturnToMainMenu()
    {
        // Dispose current game
        _store.Game.DisposeGame();

        // Transition to main menu
        _store.App.CurrentScreen.Value = ApplicationState.MainMenu;
    }

    public void Update()
    {
        bool isActive = _store.App.CurrentScreen.Value == ApplicationState.Loading;

        // Reset on transition to loading
        if (isActive && !_wasActive)
        {
            _gameCreated = false;
        }
        _wasActive = isActive;

        if (!isActive || _gameCreated)
        {
            return;
        }

        _store.Game.CreateGame();
        _lobbyCoordinator.NotifyLocalLoadComplete();
        _gameCreated = true;
    }

    public void Dispose() => _disposables.Dispose();
}
