using System;
using Serilog;
using BaseTemplate.GameApp.AppState;
using Core;
using LiteNetLib.Utils;
using Networking;
using R3;

namespace BaseTemplate.Infrastructure.Networking;

/// <summary>
/// Coordinates lobby operations between NetworkService and RootStore.
/// Handles joining, hosting, ready state, and match start flow.
/// </summary>
public sealed class LobbyCoordinator : IDisposable
{
    private readonly ILogger _log;
    private readonly NetworkService _networkService;
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;
    private readonly CompositeDisposable _eventSubscriptions = new();

    // Load confirmation tracking (coordinator-side only)
    private readonly bool[] _loadConfirmFlags;
    private bool _waitingForLoadConfirmations;

    // Writer for serializing lobby messages
    private readonly NetDataWriter _writer = new();

    public bool IsWaitingForLoadConfirmations => _waitingForLoadConfirmations;

    public LobbyCoordinator(ILogger logger, NetworkService networkService, RootStore store, AppEventBus eventBus)
    {
        _log = logger.ForContext<LobbyCoordinator>();
        _networkService = networkService;
        _store = store;
        _eventBus = eventBus;
        _loadConfirmFlags = new bool[LobbyState.MaxPlayers];

        // Subscribe to infrastructure events
        _networkService.OnPeerConnected += HandlePeerConnected;
        _networkService.OnPeerDisconnected += HandlePeerDisconnected;
        _networkService.OnPeerJoinReceived += HandlePeerJoin;

        // Mesh events
        _networkService.OnPeerListReceived += HandlePeerList;
        _networkService.OnMeshReadyReceived += HandleMeshReady;
        _networkService.OnCoordinatorAnnounceReceived += HandleCoordinatorAnnounce;

        // Raw message routing for lobby-specific messages
        _networkService.OnRawMessageReceived += HandleRawMessage;

        // Subscribe to UI events via AppEventBus
        _eventBus.LocalGameRequested.Subscribe(_ => StartLocalGame()).AddTo(_eventSubscriptions);
        _eventBus.HostGameRequested.Subscribe(p => HostGame(p.Port)).AddTo(_eventSubscriptions);
        _eventBus.JoinGameRequested.Subscribe(p => JoinGame(p.Address, p.Port)).AddTo(_eventSubscriptions);
        _eventBus.ReadyToggled.Subscribe(_ => ToggleReady()).AddTo(_eventSubscriptions);
        _eventBus.LobbyLeftRequested.Subscribe(_ => LeaveLobby()).AddTo(_eventSubscriptions);
        _eventBus.MatchStartRequested.Subscribe(_ => StartMatch()).AddTo(_eventSubscriptions);
        _eventBus.MainMenuEntered.Subscribe(_ => HandleMainMenuEntered()).AddTo(_eventSubscriptions);
    }

    private void HandleMainMenuEntered()
    {
        // Cleanup if we were in a lobby when returning to main menu
        if (_store.Network.State.Value != ConnectionState.Disconnected || _store.Network.IsSinglePlayer.Value)
        {
            LeaveLobby();
        }
    }

    /// <summary>
    /// Poll network events. Call every frame.
    /// </summary>
    public void Poll()
    {
        if (!_store.Network.IsSinglePlayer.Value)
        {
            _networkService.Poll();
        }
    }

    /// <summary>
    /// Start a single-player game (no network).
    /// </summary>
    public void StartLocalGame()
    {
        _store.Network.SetupAsSinglePlayer();
        _store.Lobby.CreateAsHost();
        _store.Lobby.ToggleLocalReady();
        _store.App.TransitionToLoading();
    }

    /// <summary>
    /// Host a multiplayer game as the bootstrap coordinator (direct connection, no NAT punch).
    /// </summary>
    public void HostGame(int port = NetworkService.DefaultPort)
    {
        _networkService.StartAsCoordinator(
            _store.App.LocalClient.ClientId,
            _store.App.LocalClient.DisplayName,
            port
        );
        _store.Network.SetupAsCoordinator(port);
        _store.Lobby.CreateAsHost();
        _store.App.TransitionToLobby();
    }

    /// <summary>
    /// Host a multiplayer game with NAT punch-through registration.
    /// </summary>
    public void HostGameWithNatPunch(string matchId, Guid matchmakingPlayerId, int port = NetworkService.DefaultPort)
    {
        _networkService.StartAsCoordinatorWithNatPunch(
            matchmakingPlayerId,
            _store.App.LocalClient.DisplayName,
            matchId,
            port
        );
        _store.Network.SetupAsCoordinator(port);
        _store.Lobby.CreateAsHost();
        _store.App.TransitionToLobby();
    }

    /// <summary>
    /// Join an existing mesh via the bootstrap coordinator (direct connection, no NAT punch).
    /// </summary>
    public void JoinGame(string coordinatorAddress, int port = NetworkService.DefaultPort)
    {
        _store.Network.SetupAsJoiningPeer(coordinatorAddress, port);
        _networkService.JoinMesh(
            _store.App.LocalClient.ClientId,
            _store.App.LocalClient.DisplayName,
            coordinatorAddress,
            port
        );
    }

    /// <summary>
    /// Join an existing mesh using NAT punch-through.
    /// Client waits for mesh to complete before transitioning to Lobby.
    /// </summary>
    public void JoinGameWithNatPunch(string matchId, Guid localPlayerId, Guid hostPlayerId, string coordinatorAddress, int port = NetworkService.DefaultPort)
    {
        _store.Network.SetupAsJoiningPeer(coordinatorAddress, port);
        _store.Network.IsWaitingForMesh.Value = true;
        _networkService.JoinMeshWithNatPunch(
            localPlayerId,
            _store.App.LocalClient.DisplayName,
            matchId,
            hostPlayerId,
            coordinatorAddress,
            port
        );
        // Stay on Matchmaking screen - will transition to Lobby when mesh completes
    }

    /// <summary>
    /// Toggle local ready state and broadcast to peers.
    /// Handles ready on Lobby screen; Matchmaking screen uses Orleans.
    /// </summary>
    public void ToggleReady()
    {
        // Only handle ready on Lobby screen - Matchmaking screen uses Orleans
        if (_store.App.CurrentScreen.Value != ApplicationState.Lobby)
        {
            return;
        }

        _store.Lobby.ToggleLocalReady();

        if (!_store.Network.IsSinglePlayer.Value)
        {
            var client = _store.Lobby.State.Value.GetClient(_store.App.LocalClient.ClientId);
            if (client.HasValue)
            {
                _networkService.SendReady(client.Value.IsReady);
            }

            if (_store.Network.IsCoordinator)
            {
                SendLobbySync(_store.Lobby.State.Value);
            }
        }
    }

    /// <summary>
    /// Start the match (coordinator only).
    /// Handles start on Lobby screen; Matchmaking screen uses Orleans.
    /// </summary>
    public void StartMatch()
    {
        if (!_store.Network.IsCoordinator) return;

        // Only handle start on Lobby screen - Matchmaking screen uses Orleans
        if (_store.App.CurrentScreen.Value != ApplicationState.Lobby)
        {
            return;
        }

        if (!_store.Lobby.CanStartGame.CurrentValue) return;

        if (!_store.Network.IsSinglePlayer.Value)
        {
            var sessionSeed = Environment.TickCount;
            var lobbyWithSeed = _store.Lobby.State.Value with { SessionSeed = sessionSeed };
            _store.Lobby.SetState(lobbyWithSeed);

            ClearLoadConfirmFlags();
            SendLobbySync(_store.Lobby.State.Value);
            _networkService.SendMatchStart();
            BeginWaitingForLoadConfirmations();
        }

        _store.App.TransitionToLoading();
    }

    /// <summary>
    /// Start match after server has confirmed all players ready.
    /// Host only - generates SessionSeed and broadcasts via P2P.
    /// </summary>
    public void StartMatchFromServer()
    {
        if (!_store.Network.IsCoordinator)
        {
            _log.Warning("StartMatchFromServer called on non-coordinator");
            return;
        }

        _log.Information("Starting match from server signal");

        // Generate SessionSeed and distribute via P2P
        var sessionSeed = Environment.TickCount;
        var lobbyWithSeed = _store.Lobby.State.Value with { SessionSeed = sessionSeed };
        _store.Lobby.SetState(lobbyWithSeed);

        ClearLoadConfirmFlags();
        SendLobbySync(lobbyWithSeed);
        _networkService.SendMatchStart();
        BeginWaitingForLoadConfirmations();

        _store.App.TransitionToLoading();
    }

    /// <summary>
    /// Notify that local client has finished loading.
    /// </summary>
    public void NotifyLocalLoadComplete()
    {
        _store.Lobby.MarkLocalClientLoaded();

        if (_store.Network.IsSinglePlayer.Value)
        {
            _store.App.TransitionToCountdown();
        }
        else
        {
            _networkService.SendLoadComplete();

            if (_store.Network.IsCoordinator)
            {
                int localSlot = _store.Network.LocalSlot.Value;
                if (localSlot >= 0 && localSlot < _loadConfirmFlags.Length)
                {
                    _loadConfirmFlags[localSlot] = true;
                }
                CheckAllLoaded();
            }
        }
    }

    /// <summary>
    /// Leave the current game/lobby.
    /// </summary>
    public void LeaveLobby()
    {
        _networkService.Stop();
        _store.Network.Reset();
        _store.Lobby.Clear();
        _store.App.TransitionToMainMenu();
    }

    /// <summary>
    /// Send lobby state sync to all peers.
    /// </summary>
    public void SendLobbySync()
    {
        SendLobbySync(_store.Lobby.State.Value);
    }

    /// <summary>
    /// Whether this is a single-player game.
    /// </summary>
    public bool IsSinglePlayer => _store.Network.IsSinglePlayer.Value;

    /// <summary>
    /// Whether this client is the coordinator.
    /// </summary>
    public bool IsCoordinator => _store.Network.IsCoordinator;

    /// <summary>
    /// Initiate countdown transition (for restart or initial game start).
    /// </summary>
    public void InitiateCountdown()
    {
        _store.App.TransitionToCountdown();
    }

    // --- Private helpers ---

    private void ClearLoadConfirmFlags()
    {
        for (int i = 0; i < _loadConfirmFlags.Length; i++)
        {
            _loadConfirmFlags[i] = false;
        }
    }

    private void BeginWaitingForLoadConfirmations()
    {
        _waitingForLoadConfirmations = true;
    }

    private void CheckAllLoaded()
    {
        if (!_store.Network.IsCoordinator) return;
        if (!_waitingForLoadConfirmations) return;

        int playerCount = _store.Lobby.State.Value.PlayerCount;
        for (int i = 0; i < playerCount; i++)
        {
            var lobby = _store.Lobby.State.Value;
            bool foundInSlot = false;
            for (int j = 0; j < lobby.Clients.Length; j++)
            {
                if (lobby.Clients[j].PlayerSlot == i)
                {
                    foundInSlot = true;
                    if (!_loadConfirmFlags[i])
                    {
                        return;
                    }
                    break;
                }
            }
            if (!foundInSlot)
            {
                continue;
            }
        }

        _waitingForLoadConfirmations = false;
        _networkService.SendStartCountdown();
        _store.App.TransitionToCountdown();
    }

    // --- Event handlers ---

    private void HandlePeerConnected(int playerSlot)
    {
        _store.Network.SetPeerCount(_networkService.ConnectedPeerCount);
        _store.Network.SetMeshComplete(_networkService.IsMeshComplete);

        if (!_store.Network.IsCoordinator)
        {
            _store.Network.MarkConnected();
        }

        CheckMeshCompleteForLobbyTransition();
    }

    private void HandlePeerDisconnected(int playerSlot)
    {
        _store.Network.SetPeerCount(_networkService.ConnectedPeerCount);
        _store.Network.SetMeshComplete(_networkService.IsMeshComplete);

        if (_store.Network.IsCoordinator)
        {
            var lobby = _store.Lobby.State.Value;
            for (int i = 0; i < lobby.Clients.Length; i++)
            {
                if (lobby.Clients[i].PlayerSlot == playerSlot)
                {
                    _store.Lobby.RemoveClient(lobby.Clients[i].ClientId);
                    SendLobbySync(_store.Lobby.State.Value);
                    break;
                }
            }
        }
        else if (playerSlot == _store.Network.CoordinatorSlot.Value)
        {
            HandleCoordinatorLost();
        }
    }

    private void HandlePeerJoin(int playerSlot, Guid clientId, string displayName)
    {
        if (_store.Network.IsCoordinator)
        {
            var lobby = _store.Lobby.State.Value;
            bool found = false;
            for (int i = 0; i < lobby.Clients.Length; i++)
            {
                if (lobby.Clients[i].ClientId == clientId)
                {
                    var updated = lobby.Clients[i] with { PlayerSlot = playerSlot, DisplayName = displayName };
                    var newClients = new ConnectedClient[lobby.Clients.Length];
                    Array.Copy(lobby.Clients, newClients, lobby.Clients.Length);
                    newClients[i] = updated;
                    _store.Lobby.SetState(lobby with { Clients = newClients });
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var newClient = new ConnectedClient(
                    ClientId: clientId,
                    PlayerSlot: playerSlot,
                    DisplayName: displayName,
                    IsReady: false,
                    IsLoaded: false
                );
                _store.Lobby.AddClient(newClient);
            }

            SendLobbySync(_store.Lobby.State.Value);
            _networkService.SendPeerList();
        }
    }

    private void HandleReady(int playerSlot, bool ready)
    {
        var lobby = _store.Lobby.State.Value;
        for (int i = 0; i < lobby.Clients.Length; i++)
        {
            if (lobby.Clients[i].PlayerSlot == playerSlot)
            {
                _store.Lobby.SetClientReady(lobby.Clients[i].ClientId, ready);

                if (_store.Network.IsCoordinator)
                {
                    SendLobbySync(_store.Lobby.State.Value);
                }
                break;
            }
        }
    }

    private void HandleMatchStart()
    {
        // Session seed was already applied via LobbySync that preceded this message
        _store.App.TransitionToLoading();
    }

    private void HandleLoadComplete(int playerSlot)
    {
        if (_store.Network.IsCoordinator && _waitingForLoadConfirmations)
        {
            if (playerSlot >= 0 && playerSlot < _loadConfirmFlags.Length)
            {
                _loadConfirmFlags[playerSlot] = true;
            }

            var lobby = _store.Lobby.State.Value;
            for (int i = 0; i < lobby.Clients.Length; i++)
            {
                if (lobby.Clients[i].PlayerSlot == playerSlot)
                {
                    _store.Lobby.MarkClientLoaded(lobby.Clients[i].ClientId);
                    break;
                }
            }

            CheckAllLoaded();
        }
    }

    private void HandleStartCountdown(bool isRestart)
    {
        if (isRestart)
        {
            // Restart case - handled by GameCoordinator
            // Just fire an event or let GameCoordinator handle via its own subscription
            return;
        }

        _store.App.TransitionToCountdown();
    }

    private void HandleLobbySync(LobbyState lobby)
    {
        _store.Lobby.SetState(lobby);

        var localClientId = _store.App.LocalClient.ClientId;
        for (int i = 0; i < lobby.Clients.Length; i++)
        {
            if (lobby.Clients[i].ClientId == localClientId)
            {
                int assignedSlot = lobby.Clients[i].PlayerSlot;
                _store.Network.SetLocalSlot(assignedSlot);
                _store.App.LocalClient.PlayerSlot = assignedSlot;
                _networkService.SetLocalSlot(assignedSlot);
                break;
            }
        }

        if (_store.App.CurrentScreen.Value == ApplicationState.MainMenu ||
            _store.App.CurrentScreen.Value == ApplicationState.Matchmaking)
        {
            if (_networkService.IsMeshComplete)
            {
                _store.Network.IsWaitingForMesh.Value = false;
                _store.App.TransitionToLobby();
            }
            else
            {
                _store.Network.IsWaitingForMesh.Value = true;
                _log.Information("Waiting for mesh to complete before transitioning to lobby");
            }
        }
    }

    private void HandlePeerList(PeerInfo[] peers)
    {
        _store.Network.SetPeerCount(_networkService.ConnectedPeerCount);
        _store.Network.SetMeshComplete(_networkService.IsMeshComplete);
    }

    private void HandleMeshReady(int playerSlot)
    {
        _store.Network.SetMeshComplete(_networkService.IsMeshComplete);
        CheckMeshCompleteForLobbyTransition();
    }

    private void CheckMeshCompleteForLobbyTransition()
    {
        if (!_networkService.IsMeshComplete)
        {
            return;
        }

        // Transition to lobby when waiting for mesh to complete
        if (_store.Network.IsWaitingForMesh.Value)
        {
            _store.Network.IsWaitingForMesh.Value = false;
            _log.Information("Mesh complete, transitioning to lobby");

            if (_store.App.CurrentScreen.Value == ApplicationState.MainMenu ||
                _store.App.CurrentScreen.Value == ApplicationState.Matchmaking)
            {
                _store.App.TransitionToLobby();
            }
        }
    }

    private void HandleCoordinatorAnnounce(int newCoordinatorSlot)
    {
        _store.Network.SetCoordinator(newCoordinatorSlot);
    }

    private void HandleCoordinatorLost()
    {
        int lowestSlot = _networkService.FindLowestConnectedSlot();

        if (lowestSlot == _store.Network.LocalSlot.Value)
        {
            _networkService.BecomeCoordinator();
            _store.Network.SetCoordinator(_store.Network.LocalSlot.Value);
            _networkService.SendCoordinatorAnnounce();
            SendLobbySync(_store.Lobby.State.Value);
        }
        else if (lowestSlot >= 0)
        {
            _store.Network.SetCoordinator(lowestSlot);
        }
        else
        {
            _store.Network.MarkDisconnected();
        }
    }

    // --- Serialization helpers ---

    internal static void SerializeLobbyState(NetDataWriter writer, in LobbyState lobby)
    {
        writer.Put((byte)lobby.Clients.Length);
        for (int i = 0; i < lobby.Clients.Length; i++)
        {
            var c = lobby.Clients[i];
            writer.PutBytesWithLength(c.ClientId.ToByteArray());
            writer.Put(c.PlayerSlot);
            writer.Put(c.DisplayName);
            writer.Put(c.IsReady);
            writer.Put(c.IsLoaded);
        }
        writer.Put(lobby.SessionSeed);
    }

    internal static LobbyState DeserializeLobbyState(NetDataReader reader)
    {
        int clientCount = reader.GetByte();
        var clients = new ConnectedClient[clientCount];
        for (int i = 0; i < clientCount; i++)
        {
            var clientId = new Guid(reader.GetBytesWithLength());
            int slot = reader.GetInt();
            string name = reader.GetString();
            bool ready = reader.GetBool();
            bool loaded = reader.GetBool();
            clients[i] = new ConnectedClient(clientId, slot, name, ready, loaded);
        }
        int sessionSeed = reader.GetInt();
        return new LobbyState { Clients = clients, SessionSeed = sessionSeed };
    }

    private void SendLobbySync(in LobbyState lobby)
    {
        _writer.Reset();
        SerializeLobbyState(_writer, in lobby);
        _networkService.BroadcastReliable((byte)NetMessageType.LobbySync, _writer.Data.AsSpan(0, _writer.Length));
    }

    // --- Raw message routing ---

    private void HandleRawMessage(int fromSlot, byte messageType, NetDataReader reader)
    {
        var type = (NetMessageType)messageType;
        switch (type)
        {
            case NetMessageType.LobbySync:
                {
                    var lobby = DeserializeLobbyState(reader);
                    HandleLobbySync(lobby);
                }
                break;

            case NetMessageType.Ready:
                {
                    if (reader.AvailableBytes < 5) return;
                    int slot = reader.GetInt();
                    bool ready = reader.GetBool();
                    HandleReady(slot, ready);
                }
                break;

            case NetMessageType.MatchStart:
                HandleMatchStart();
                break;

            case NetMessageType.LoadComplete:
                {
                    if (reader.AvailableBytes < 4) return;
                    int slot = reader.GetInt();
                    HandleLoadComplete(slot);
                }
                break;

            case NetMessageType.StartCountdown:
                // Check if this is initial start or restart
                bool isRestart = _store.App.CurrentScreen.Value == ApplicationState.InGame;
                HandleStartCountdown(isRestart);
                break;

            // Game-specific messages are handled by GameCoordinator (NetworkCoordinator)
            default:
                break;
        }
    }

    public void Dispose()
    {
        _eventSubscriptions.Dispose();

        _networkService.OnPeerConnected -= HandlePeerConnected;
        _networkService.OnPeerDisconnected -= HandlePeerDisconnected;
        _networkService.OnPeerJoinReceived -= HandlePeerJoin;
        _networkService.OnPeerListReceived -= HandlePeerList;
        _networkService.OnMeshReadyReceived -= HandleMeshReady;
        _networkService.OnCoordinatorAnnounceReceived -= HandleCoordinatorAnnounce;
        _networkService.OnRawMessageReceived -= HandleRawMessage;
    }
}
