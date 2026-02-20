using System;
using System.Collections.Generic;
using Serilog;
using DieDrifterDie.GameApp.AppState;
using Networking;
using R3;

namespace DieDrifterDie.Infrastructure.Networking;

/// <summary>
/// Coordinates between IMatchmakingProvider and LobbyCoordinator.
/// Handles the transition from matchmaking discovery to P2P connection.
/// </summary>
public sealed class MatchmakingCoordinator : IDisposable
{
    private readonly ILogger _log;
    private readonly IMatchmakingProvider _provider;
    private readonly LobbyCoordinator _lobbyCoordinator;
    private readonly RootStore _store;
    private readonly CompositeDisposable _eventSubscriptions = new();

    public IMatchmakingProvider Provider => _provider;

    public MatchmakingCoordinator(
        ILogger logger,
        IMatchmakingProvider provider,
        LobbyCoordinator lobbyCoordinator,
        RootStore store,
        AppEventBus eventBus)
    {
        _log = logger.ForContext<MatchmakingCoordinator>();
        _provider = provider;
        _lobbyCoordinator = lobbyCoordinator;
        _store = store;

        // Subscribe to matchmaking events from provider
        _provider.OnLobbyCreated += HandleLobbyCreated;
        _provider.OnLobbyJoined += HandleLobbyJoined;
        _provider.OnLobbyListRefreshed += HandleLobbyListRefreshed;
        _provider.OnError += HandleError;
        _provider.OnPlayerListUpdated += HandlePlayerListUpdated;
        _provider.OnKickedFromLobby += HandleKickedFromLobby;
        _provider.OnReadyStateChanged += HandleReadyStateChanged;
        _provider.OnMatchStarted += HandleMatchStarted;

        // Subscribe to UI events via AppEventBus
        eventBus.LobbyBrowserRequested.Subscribe(_ => ShowLobbyBrowser()).AddTo(_eventSubscriptions);
        eventBus.MatchmakingLeftRequested.Subscribe(_ => LeaveMatchmaking()).AddTo(_eventSubscriptions);
        eventBus.LobbiesRefreshRequested.Subscribe(_ => RefreshLobbies()).AddTo(_eventSubscriptions);
        eventBus.LobbyCreateRequested.Subscribe(p => CreateLobby(p.DisplayName)).AddTo(_eventSubscriptions);
        eventBus.LobbyJoinRequested.Subscribe(p => JoinLobby(p.LobbyId, p.DisplayName)).AddTo(_eventSubscriptions);
        eventBus.HostingStartRequested.Subscribe(_ => StartHosting()).AddTo(_eventSubscriptions);
        eventBus.MatchmakingLobbyLeftRequested.Subscribe(_ => HandleMatchmakingLobbyLeft()).AddTo(_eventSubscriptions);
        eventBus.PlayerKickRequested.Subscribe(p => KickPlayer(p.PlayerId)).AddTo(_eventSubscriptions);

        // Subscribe to ready/start events - only handle on Matchmaking screen
        // Lobby screen ready/start goes through P2P (LobbyCoordinator)
        eventBus.ReadyToggled.Subscribe(_ =>
        {
            if (_store.App.CurrentScreen.Value == ApplicationState.Matchmaking &&
                _store.Matchmaking.State.Value == MatchmakingState.InLobby)
            {
                ToggleReady();
            }
        }).AddTo(_eventSubscriptions);

        eventBus.MatchStartRequested.Subscribe(_ =>
        {
            if (_store.App.CurrentScreen.Value == ApplicationState.Matchmaking &&
                _store.Matchmaking.State.Value == MatchmakingState.InLobby)
            {
                RequestMatchStart();
            }
        }).AddTo(_eventSubscriptions);
    }

    private void HandleMatchmakingLobbyLeft()
    {
        LeaveLobby();
        Cancel();
    }

    /// <summary>
    /// Poll for matchmaking updates. Call every frame when on matchmaking screen.
    /// </summary>
    public void Poll()
    {
        _provider.Update();

        // Sync provider state to store
        _store.Matchmaking.SetState(_provider.State);
        _store.Matchmaking.SetRefreshing(_provider.IsRefreshingLobbyList);
        _store.Matchmaking.SetIsHost(_provider.IsHost);
        _store.Matchmaking.SetLobbyPlayers(_provider.LobbyPlayers);
        _store.Matchmaking.SetWasKicked(_provider.WasKicked);
        _store.Matchmaking.SetMatchStarted(_provider.MatchStarted);

        if (_provider.CurrentLobby.HasValue)
        {
            _store.Matchmaking.SetCurrentLobby(_provider.CurrentLobby.Value);
        }

        // Compute all players ready state (for display on matchmaking screen)
        var players = _provider.LobbyPlayers;
        bool allReady = players.Count > 0;
        for (int i = 0; i < players.Count; i++)
        {
            if (!players[i].IsReady)
            {
                allReady = false;
                break;
            }
        }
        _store.Matchmaking.SetAllPlayersReady(allReady);

        // Update local player ready state from player list
        if (_provider.CurrentLobby.HasValue)
        {
            var localPlayerId = _provider.CurrentLobby.Value.LocalPlayerId;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].PlayerId == localPlayerId)
                {
                    _store.Matchmaking.SetLocalPlayerReady(players[i].IsReady);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Navigate to matchmaking screen and refresh lobby list.
    /// </summary>
    public void ShowLobbyBrowser()
    {
        _provider.Reset();
        _store.Matchmaking.Reset();
        _store.App.TransitionToMatchmaking();
        RefreshLobbies();
    }

    /// <summary>
    /// Request refresh of available lobbies.
    /// </summary>
    public void RefreshLobbies()
    {
        _provider.RequestLobbyListRefresh();
    }

    /// <summary>
    /// Create a new lobby as host.
    /// </summary>
    public void CreateLobby(string displayName)
    {
        _provider.CreateLobby(displayName, MatchmakingConfig.DefaultPort);
    }

    /// <summary>
    /// Join an existing lobby.
    /// </summary>
    public void JoinLobby(string lobbyId, string displayName)
    {
        _provider.JoinLobby(lobbyId, displayName, MatchmakingConfig.DefaultPort);
    }

    /// <summary>
    /// Start hosting after lobby is created.
    /// Transitions from matchmaking to P2P hosting with NAT punch.
    /// Orleans continues for heartbeats only; P2P handles lobby state.
    /// </summary>
    public void StartHosting()
    {
        if (_provider.State != MatchmakingState.InLobby || !_provider.CurrentLobby.HasValue)
        {
            return;
        }

        var lobby = _provider.CurrentLobby.Value;
        _log.Information("Starting hosting with NAT punch, matchId={MatchId}, playerId={PlayerId}",
            lobby.LobbyId, lobby.LocalPlayerId);
        _lobbyCoordinator.HostGameWithNatPunch(lobby.LobbyId, lobby.LocalPlayerId, lobby.HostPort);
    }

    /// <summary>
    /// Cancel current operation and return to idle.
    /// </summary>
    public void Cancel()
    {
        _provider.Cancel();
        _store.Matchmaking.ClearError();
    }

    /// <summary>
    /// Send heartbeat to server. Should be called periodically while in lobby.
    /// </summary>
    public void SendHeartbeat()
    {
        _provider.SendHeartbeat();
    }

    /// <summary>
    /// Kick a player from the lobby (host only).
    /// </summary>
    public void KickPlayer(Guid playerId)
    {
        _provider.KickPlayer(playerId);
    }

    /// <summary>
    /// Leave the current lobby gracefully.
    /// </summary>
    public void LeaveLobby()
    {
        _provider.LeaveLobby();
    }

    /// <summary>
    /// Toggle local player's ready state on the server.
    /// </summary>
    public void ToggleReady()
    {
        bool newReady = !_store.Matchmaking.LocalPlayerReady.Value;
        _provider.SetReady(newReady);
    }

    /// <summary>
    /// Request match start from server (host only).
    /// </summary>
    public void RequestMatchStart()
    {
        if (!_provider.IsHost)
        {
            _log.Warning("Only host can request match start");
            return;
        }

        _provider.RequestMatchStart();
    }

    /// <summary>
    /// Leave matchmaking screen and return to main menu.
    /// </summary>
    public void LeaveMatchmaking()
    {
        _provider.Reset();
        _store.Matchmaking.Reset();
        _store.App.TransitionToMainMenu();
    }

    // --- Event Handlers ---

    private void HandleLobbyCreated(LobbyCreatedArgs args)
    {
        _log.Information("Lobby created: {LobbyId}", args.LobbyId);
        if (_provider.CurrentLobby.HasValue)
        {
            var lobby = _provider.CurrentLobby.Value;
            _store.Matchmaking.SetCurrentLobby(lobby);

            // Update LocalClient.ClientId to match matchmaking-assigned PlayerId
            _log.Information("Updating LocalClient.ClientId from {OldClientId} to {NewClientId}",
                _store.App.LocalClient.ClientId, lobby.LocalPlayerId);
            _store.App.LocalClient.ClientId = lobby.LocalPlayerId;
        }
        // Stay on matchmaking screen in InLobby state until user clicks "Start Hosting"
    }

    private void HandleLobbyJoined(LobbyJoinedArgs args)
    {
        _log.Information("Lobby joined, connecting with NAT punch to {HostAddress}:{HostPort}, matchId={LobbyId}, localPlayerId={LocalPlayerId}, hostPlayerId={HostPlayerId}",
            args.HostAddress, args.HostPort, args.LobbyId, args.LocalPlayerId, args.HostPlayerId);

        // Update LocalClient.ClientId to match matchmaking-assigned PlayerId
        _log.Information("Updating LocalClient.ClientId from {OldClientId} to {NewClientId}",
            _store.App.LocalClient.ClientId, args.LocalPlayerId);
        _store.App.LocalClient.ClientId = args.LocalPlayerId;

        // Transition to LobbyCoordinator for P2P connection with NAT punch
        _lobbyCoordinator.JoinGameWithNatPunch(args.LobbyId, args.LocalPlayerId, args.HostPlayerId, args.HostAddress, args.HostPort);
    }

    private void HandleLobbyListRefreshed()
    {
        _log.Information("Lobby list refreshed: {LobbyCount} lobbies", _provider.AvailableLobbies.Count);
        _store.Matchmaking.SetAvailableLobbies(_provider.AvailableLobbies);
    }

    private void HandleError(string error)
    {
        _log.Warning("Error: {Error}", error);
        _store.Matchmaking.SetError(error);
    }

    private void HandlePlayerListUpdated(IReadOnlyList<LobbyPlayerInfo> players)
    {
        _log.Information("Player list updated: {PlayerCount} players", players.Count);
        _store.Matchmaking.SetLobbyPlayers(players);
    }

    private void HandleKickedFromLobby()
    {
        _log.Information("Kicked from lobby");
        _store.Matchmaking.SetWasKicked(true);
        _store.Matchmaking.SetState(MatchmakingState.Idle);
        _store.Matchmaking.SetCurrentLobby(null);
    }

    private void HandleReadyStateChanged(bool isReady)
    {
        _log.Information("Ready state changed: {IsReady}", isReady);
        _store.Matchmaking.SetLocalPlayerReady(isReady);
    }

    private void HandleMatchStarted()
    {
        _log.Information("Server signaled match started, transitioning to P2P game phase");
        _store.Matchmaking.SetMatchStarted(true);

        // Host triggers P2P match start with SessionSeed distribution
        if (_provider.IsHost && _provider.CurrentLobby.HasValue)
        {
            _lobbyCoordinator.StartMatchFromServer();
        }
        else
        {
            // Non-host players transition to loading
            // They will receive SessionSeed via P2P LobbySync message
            _store.App.TransitionToLoading();
        }
    }

    public void Dispose()
    {
        _eventSubscriptions.Dispose();

        _provider.OnLobbyCreated -= HandleLobbyCreated;
        _provider.OnLobbyJoined -= HandleLobbyJoined;
        _provider.OnLobbyListRefreshed -= HandleLobbyListRefreshed;
        _provider.OnError -= HandleError;
        _provider.OnPlayerListUpdated -= HandlePlayerListUpdated;
        _provider.OnKickedFromLobby -= HandleKickedFromLobby;
        _provider.OnReadyStateChanged -= HandleReadyStateChanged;
        _provider.OnMatchStarted -= HandleMatchStarted;
        _provider.Dispose();
    }
}
