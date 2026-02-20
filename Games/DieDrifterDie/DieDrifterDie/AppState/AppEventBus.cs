using System;
using R3;

namespace DieDrifterDie.GameApp.AppState;

// Payload types for events with data
public readonly record struct HostGamePayload(int Port);
public readonly record struct JoinGamePayload(string Address, int Port);
public readonly record struct LobbyCreatePayload(string DisplayName);
public readonly record struct LobbyJoinPayload(string LobbyId, string DisplayName);
public readonly record struct PlayerKickPayload(Guid PlayerId);

/// <summary>
/// Centralized event bus for app-level events using R3 Subject.
/// Publishers call Publish* methods, subscribers subscribe to Observable properties.
/// </summary>
public sealed class AppEventBus : IDisposable
{
    // Game lifecycle events
    private readonly Subject<int> _sessionSeedGenerated = new();
    private readonly Subject<Unit> _countdownRequested = new();
    private readonly Subject<Unit> _restartRequested = new();

    // Lobby action events
    private readonly Subject<Unit> _localGameRequested = new();
    private readonly Subject<HostGamePayload> _hostGameRequested = new();
    private readonly Subject<JoinGamePayload> _joinGameRequested = new();
    private readonly Subject<Unit> _readyToggled = new();
    private readonly Subject<Unit> _lobbyLeftRequested = new();
    private readonly Subject<Unit> _matchStartRequested = new();

    // Matchmaking events
    private readonly Subject<Unit> _lobbyBrowserRequested = new();
    private readonly Subject<Unit> _matchmakingLeftRequested = new();
    private readonly Subject<Unit> _lobbiesRefreshRequested = new();
    private readonly Subject<LobbyCreatePayload> _lobbyCreateRequested = new();
    private readonly Subject<LobbyJoinPayload> _lobbyJoinRequested = new();
    private readonly Subject<Unit> _hostingStartRequested = new();
    private readonly Subject<Unit> _matchmakingLobbyLeftRequested = new();
    private readonly Subject<PlayerKickPayload> _playerKickRequested = new();

    // Screen transition events
    private readonly Subject<Unit> _countdownComplete = new();
    private readonly Subject<Unit> _mainMenuEntered = new();
    private readonly Subject<Unit> _returnToMainMenuRequested = new();

    // P2P mesh events
    private readonly Subject<Unit> _meshConnected = new();

    // Bug report events
    private readonly Subject<string?> _bugReportRequested = new();

    // Game lifecycle observables
    public Observable<int> SessionSeedGenerated => _sessionSeedGenerated;
    public Observable<Unit> CountdownRequested => _countdownRequested;
    public Observable<Unit> RestartRequested => _restartRequested;

    // Lobby action observables
    public Observable<Unit> LocalGameRequested => _localGameRequested;
    public Observable<HostGamePayload> HostGameRequested => _hostGameRequested;
    public Observable<JoinGamePayload> JoinGameRequested => _joinGameRequested;
    public Observable<Unit> ReadyToggled => _readyToggled;
    public Observable<Unit> LobbyLeftRequested => _lobbyLeftRequested;
    public Observable<Unit> MatchStartRequested => _matchStartRequested;

    // Matchmaking observables
    public Observable<Unit> LobbyBrowserRequested => _lobbyBrowserRequested;
    public Observable<Unit> MatchmakingLeftRequested => _matchmakingLeftRequested;
    public Observable<Unit> LobbiesRefreshRequested => _lobbiesRefreshRequested;
    public Observable<LobbyCreatePayload> LobbyCreateRequested => _lobbyCreateRequested;
    public Observable<LobbyJoinPayload> LobbyJoinRequested => _lobbyJoinRequested;
    public Observable<Unit> HostingStartRequested => _hostingStartRequested;
    public Observable<Unit> MatchmakingLobbyLeftRequested => _matchmakingLobbyLeftRequested;
    public Observable<PlayerKickPayload> PlayerKickRequested => _playerKickRequested;

    // Screen transition observables
    public Observable<Unit> CountdownComplete => _countdownComplete;
    public Observable<Unit> MainMenuEntered => _mainMenuEntered;
    public Observable<Unit> ReturnToMainMenuRequested => _returnToMainMenuRequested;

    // P2P mesh observables
    public Observable<Unit> MeshConnected => _meshConnected;

    // Bug report observables
    public Observable<string?> BugReportRequested => _bugReportRequested;

    // Game lifecycle publishers
    public void PublishSessionSeed(int seed) => _sessionSeedGenerated.OnNext(seed);
    public void PublishCountdownRequested() => _countdownRequested.OnNext(Unit.Default);
    public void PublishRestartRequested() => _restartRequested.OnNext(Unit.Default);

    // Lobby action publishers
    public void PublishLocalGameRequested() => _localGameRequested.OnNext(Unit.Default);
    public void PublishHostGameRequested(int port) => _hostGameRequested.OnNext(new HostGamePayload(port));
    public void PublishJoinGameRequested(string address, int port) => _joinGameRequested.OnNext(new JoinGamePayload(address, port));
    public void PublishReadyToggled() => _readyToggled.OnNext(Unit.Default);
    public void PublishLobbyLeftRequested() => _lobbyLeftRequested.OnNext(Unit.Default);
    public void PublishMatchStartRequested() => _matchStartRequested.OnNext(Unit.Default);

    // Matchmaking publishers
    public void PublishLobbyBrowserRequested() => _lobbyBrowserRequested.OnNext(Unit.Default);
    public void PublishMatchmakingLeftRequested() => _matchmakingLeftRequested.OnNext(Unit.Default);
    public void PublishLobbiesRefreshRequested() => _lobbiesRefreshRequested.OnNext(Unit.Default);
    public void PublishLobbyCreateRequested(string displayName) => _lobbyCreateRequested.OnNext(new LobbyCreatePayload(displayName));
    public void PublishLobbyJoinRequested(string lobbyId, string displayName) => _lobbyJoinRequested.OnNext(new LobbyJoinPayload(lobbyId, displayName));
    public void PublishHostingStartRequested() => _hostingStartRequested.OnNext(Unit.Default);
    public void PublishMatchmakingLobbyLeftRequested() => _matchmakingLobbyLeftRequested.OnNext(Unit.Default);
    public void PublishPlayerKickRequested(Guid playerId) => _playerKickRequested.OnNext(new PlayerKickPayload(playerId));

    // Screen transition publishers
    public void PublishCountdownComplete() => _countdownComplete.OnNext(Unit.Default);
    public void PublishMainMenuEntered() => _mainMenuEntered.OnNext(Unit.Default);
    public void PublishReturnToMainMenu() => _returnToMainMenuRequested.OnNext(Unit.Default);

    // P2P mesh publishers
    public void PublishMeshConnected() => _meshConnected.OnNext(Unit.Default);

    // Bug report publishers
    public void PublishBugReportRequested(string? description) => _bugReportRequested.OnNext(description);

    public void Dispose()
    {
        _sessionSeedGenerated.Dispose();
        _countdownRequested.Dispose();
        _restartRequested.Dispose();

        _localGameRequested.Dispose();
        _hostGameRequested.Dispose();
        _joinGameRequested.Dispose();
        _readyToggled.Dispose();
        _lobbyLeftRequested.Dispose();
        _matchStartRequested.Dispose();

        _lobbyBrowserRequested.Dispose();
        _matchmakingLeftRequested.Dispose();
        _lobbiesRefreshRequested.Dispose();
        _lobbyCreateRequested.Dispose();
        _lobbyJoinRequested.Dispose();
        _hostingStartRequested.Dispose();
        _matchmakingLobbyLeftRequested.Dispose();
        _playerKickRequested.Dispose();

        _countdownComplete.Dispose();
        _mainMenuEntered.Dispose();
        _returnToMainMenuRequested.Dispose();

        _meshConnected.Dispose();

        _bugReportRequested.Dispose();
    }
}
