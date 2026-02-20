using System;
using System.Net;
using System.Net.Sockets;
using Core;
using LiteNetLib;
using LiteNetLib.Utils;
using Serilog;

namespace Networking;

/// <summary>
/// Low-level LiteNetLib networking wrapper for P2P mesh networking.
/// Each peer connects to all other peers for direct communication.
/// Supports NAT punch-through for connections behind NAT.
/// </summary>
public sealed class NetworkService : INetEventListener, INatPunchListener, IDisposable
{
    private readonly ILogger _log;
    private readonly NetManager _netManager;
    private readonly NetDataWriter _writer;

    // Mesh peer tracking
    private readonly PeerInfo[] _meshPeers;
    private int _localSlot;
    private Guid _localClientId;
    private string _localDisplayName = string.Empty;
    private bool _isCoordinator;
    private int _coordinatorSlot;
    private int _listenPort;

    // NAT punch-through state
    private string _localToken = string.Empty;
    private string _matchId = string.Empty;  // Stored for computing peer NatTokens
    private string _targetHostToken = string.Empty;
    private IPEndPoint? _natPunchServerEndpoint;
    private bool _waitingForNatPunch;
    private bool _hostNatPunchRegistered;
    private bool _peerNatPunchRegistered;  // Non-coordinator peers also register for P2P
    private bool _natPunchRegistrationConfirmed;
    private DateTime _natPunchStartTime;
    private DateTime _lastNatPunchRequest;
    private DateTime _lastHostRegistration;
    private DateTime _lastPeerRegistration;  // For non-coordinator re-registration
    private string? _pendingCoordinatorAddress;
    private int _pendingCoordinatorPort;

    // Peer-to-peer NAT punch state (for mesh connections)
    private readonly bool[] _pendingPeerPunch = new bool[MaxPlayers];
    private readonly DateTime[] _peerPunchStartTime = new DateTime[MaxPlayers];
    private readonly DateTime[] _lastPeerPunchRequest = new DateTime[MaxPlayers];

    private const double NatPunchTimeoutSeconds = 10.0;
    private const double NatPunchRetryIntervalSeconds = 1.0;
    private const double HostRegistrationIntervalSeconds = 5.0;
    private const double HostRegistrationIntervalSecondsInitial = 1.0;

    public const int MaxPlayers = 8;
    public const int DefaultPort = 7778;
    private const string ConnectionKey = "Catrillion_v1";
    private const int InputRedundancyCount = 3;  // Send last N frames for packet loss recovery

    // Public accessors
    public int LocalSlot => _localSlot;
    public Guid LocalClientId => _localClientId;
    public bool IsCoordinator => _isCoordinator;
    public int CoordinatorSlot => _coordinatorSlot;
    public int ListenPort => _listenPort;
    public bool WaitingForNatPunch => _waitingForNatPunch;

    /// <summary>
    /// Number of connected peers (excluding self).
    /// </summary>
    public int ConnectedPeerCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (_meshPeers[i].IsValid && _meshPeers[i].PlayerSlot != _localSlot && _meshPeers[i].IsConnected)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Whether we have at least one connection (to coordinator or any peer).
    /// </summary>
    public bool IsConnected => ConnectedPeerCount > 0 || _isCoordinator;

    /// <summary>
    /// Whether the mesh is fully connected (all known peers connected).
    /// </summary>
    public bool IsMeshComplete
    {
        get
        {
            int expectedPeers = 0;
            int connectedPeers = 0;
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (_meshPeers[i].IsValid && _meshPeers[i].PlayerSlot != _localSlot)
                {
                    expectedPeers++;
                    if (_meshPeers[i].IsConnected)
                    {
                        connectedPeers++;
                    }
                }
            }
            return expectedPeers == connectedPeers;
        }
    }

    /// <summary>
    /// Whether latency simulation is enabled.
    /// </summary>
    public bool IsSimulatingLatency => _netManager.SimulateLatency;

    /// <summary>
    /// Configured minimum simulated latency in ms (0 if disabled).
    /// </summary>
    public int SimulatedMinLatency => _netManager.SimulateLatency ? _netManager.SimulationMinLatency : 0;

    /// <summary>
    /// Configured maximum simulated latency in ms (0 if disabled).
    /// </summary>
    public int SimulatedMaxLatency => _netManager.SimulateLatency ? _netManager.SimulationMaxLatency : 0;

    // Infrastructure events (used by NetworkCoordinator)
    public event Action<int>? OnPeerConnected;
    public event Action<int>? OnPeerDisconnected;
    public event Action<int, Guid, string>? OnPeerJoinReceived;
    public event Action<PeerInfo[]>? OnPeerListReceived;
    public event Action<int, Guid, string>? OnPeerHelloReceived;
    public event Action<int>? OnPeerAckReceived;
    public event Action<int>? OnMeshReadyReceived;
    public event Action<int>? OnCoordinatorAnnounceReceived;
    public event Action<bool, string>? OnNatPunchResult;  // success, message

    // Raw message event for game-specific protocol handling (used by NetworkCoordinator)
    public event Action<int, byte, NetDataReader>? OnRawMessageReceived;  // slot, messageType, reader

    /// <summary>
    /// Create NetworkService with default config and logger (for tests).
    /// </summary>
    public NetworkService() : this(new NetworkConfig(), Log.Logger)
    {
    }

    /// <summary>
    /// Create NetworkService with config and default logger (backwards compat).
    /// </summary>
    public NetworkService(NetworkConfig config) : this(config, Log.Logger)
    {
    }

    public NetworkService(NetworkConfig config, ILogger logger)
    {
        _log = logger.ForContext<NetworkService>();
        _netManager = new NetManager(this)
        {
            AutoRecycle = true,
            UpdateTime = 15,
            PingInterval = 1000,
            DisconnectTimeout = 10000,
            ReconnectDelay = 500,
            MaxConnectAttempts = 10,
            NatPunchEnabled = true,
            UnconnectedMessagesEnabled = true
        };

        // Apply network simulation profile
        var (enabled, minLatency, maxLatency, packetLoss) =
            NetworkProfilesConfig.GetSettings(config.Profile);
        if (enabled)
        {
            _netManager.SimulateLatency = true;
            _netManager.SimulationMinLatency = minLatency;
            _netManager.SimulationMaxLatency = maxLatency;
            _netManager.SimulatePacketLoss = true;
            _netManager.SimulationPacketLossChance = packetLoss;
            _log.Debug("Simulating: {MinLatency}-{MaxLatency}ms latency, {PacketLoss}% packet loss", minLatency, maxLatency, packetLoss);
        }

        _netManager.NatPunchModule.Init(this);

        _writer = new NetDataWriter();
        _meshPeers = new PeerInfo[MaxPlayers];

        for (int i = 0; i < MaxPlayers; i++)
        {
            _meshPeers[i] = PeerInfo.Empty;
        }

        _localSlot = -1;
        _coordinatorSlot = 0;

        // Use NAT punch server override if provided (for testing), otherwise resolve from config
        if (config.DisableNatPunch)
        {
            _natPunchServerEndpoint = null;
            _log.Debug("NAT punch disabled");
        }
        else if (config.NatPunchServerOverride != null)
        {
            _natPunchServerEndpoint = config.NatPunchServerOverride;
            _log.Debug("NAT punch server override: {Endpoint}", _natPunchServerEndpoint);
        }
        else
        {
            // Resolve NAT punch server endpoint from config
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(MatchmakingConfig.NatPunchHost);
                if (addresses.Length > 0)
                {
                    _natPunchServerEndpoint = new IPEndPoint(addresses[0], MatchmakingConfig.NatPunchPort);
                    _log.Debug("NAT punch server resolved: {Endpoint}", _natPunchServerEndpoint);
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Failed to resolve NAT punch server: {Error}", ex.Message);
            }
        }
    }

    // --- Startup methods ---

    /// <summary>
    /// Start as the bootstrap coordinator (first player to host).
    /// </summary>
    public void StartAsCoordinator(Guid clientId, string displayName, int port = DefaultPort)
    {
        _listenPort = port;
        _isCoordinator = true;
        _localSlot = 0;
        _coordinatorSlot = 0;
        _localClientId = clientId;
        _localDisplayName = displayName;

        // Add self to mesh
        _meshPeers[0] = PeerInfo.CreateLocal(clientId, 0, displayName);

        _netManager.Start(port);
    }

    /// <summary>
    /// Start as a joining peer and connect to the bootstrap coordinator.
    /// </summary>
    public void JoinMesh(Guid clientId, string displayName, string coordinatorAddress, int coordinatorPort = DefaultPort)
    {
        _log.Debug("JoinMesh: connecting to coordinator at {Address}:{Port}", coordinatorAddress, coordinatorPort);

        _isCoordinator = false;
        _localSlot = -1; // Will be assigned by coordinator
        _coordinatorSlot = 0;
        _localClientId = clientId;
        _localDisplayName = displayName;

        // Start listening on a random port for incoming mesh connections
        _netManager.Start(0);
        _listenPort = _netManager.LocalPort;
        _log.Debug("Listening on local port {Port}", _listenPort);

        // Connect to coordinator
        _netManager.Connect(coordinatorAddress, coordinatorPort, ConnectionKey);
    }

    /// <summary>
    /// Start as the bootstrap coordinator with NAT punch registration.
    /// </summary>
    public void StartAsCoordinatorWithNatPunch(Guid clientId, string displayName, string matchId, int port = DefaultPort)
    {
        _log.Debug("StartAsCoordinatorWithNatPunch: matchId={MatchId}, port={Port}", matchId, port);

        _listenPort = port;
        _isCoordinator = true;
        _localSlot = 0;
        _coordinatorSlot = 0;
        _localClientId = clientId;
        _localDisplayName = displayName;
        _matchId = matchId;
        _localToken = $"{matchId}:{clientId}";
        _hostNatPunchRegistered = true;
        _lastHostRegistration = DateTime.UtcNow;

        // Add self to mesh with our NatToken
        _meshPeers[0] = PeerInfo.CreateLocal(clientId, 0, displayName, _localToken);

        _netManager.Start(port);
        RegisterWithNatPunchServer();
    }

    /// <summary>
    /// Start as a joining peer using NAT punch to connect to the coordinator.
    /// </summary>
    public void JoinMeshWithNatPunch(Guid clientId, string displayName, string matchId, Guid hostPlayerId, string coordinatorAddress, int coordinatorPort = DefaultPort)
    {
        _log.Debug("JoinMeshWithNatPunch: matchId={MatchId}, host={HostPlayerId}", matchId, hostPlayerId);

        _isCoordinator = false;
        _localSlot = -1;
        _coordinatorSlot = 0;
        _localClientId = clientId;
        _localDisplayName = displayName;
        _matchId = matchId;
        _localToken = $"{matchId}:{clientId}";
        _targetHostToken = $"{matchId}:{hostPlayerId}";
        _pendingCoordinatorAddress = coordinatorAddress;
        _pendingCoordinatorPort = coordinatorPort;

        // Start listening on a random port
        _netManager.Start(0);
        _listenPort = _netManager.LocalPort;
        _log.Debug("Listening on local port {Port}", _listenPort);

        // Request NAT punch introduction
        RequestNatPunchIntroduction();
    }

    // --- NAT Punch methods ---

    private void RegisterWithNatPunchServer()
    {
        if (_natPunchServerEndpoint == null)
        {
            _log.Warning("NAT punch server not configured");
            return;
        }

        _writer.Reset();
        _writer.Put((byte)1);  // Register message type
        _writer.Put(_localToken);
        _netManager.SendUnconnectedMessage(_writer, _natPunchServerEndpoint);

        if (!_natPunchRegistrationConfirmed)
        {
            _log.Debug("Registering with NAT punch server as {Token}", _localToken);
        }
    }

    private void RequestNatPunchIntroduction()
    {
        if (_natPunchServerEndpoint == null)
        {
            _log.Warning("NAT punch server not configured, falling back to direct connect");
            OnNatPunchResult?.Invoke(false, "NAT punch server not configured");

            // Fall back to direct connection
            if (!string.IsNullOrEmpty(_pendingCoordinatorAddress))
            {
                _log.Debug("Trying direct connect to {Address}:{Port}", _pendingCoordinatorAddress, _pendingCoordinatorPort);
                _netManager.Connect(_pendingCoordinatorAddress, _pendingCoordinatorPort, ConnectionKey);
            }
            else
            {
                // Try localhost fallback for same-machine testing
                _log.Debug("No coordinator address, trying localhost:{Port}", _pendingCoordinatorPort);
                _netManager.Connect("127.0.0.1", _pendingCoordinatorPort, ConnectionKey);
            }
            return;
        }

        _waitingForNatPunch = true;
        _natPunchStartTime = DateTime.UtcNow;
        _lastNatPunchRequest = DateTime.UtcNow;

        SendNatPunchRequest();
    }

    private void SendNatPunchRequest()
    {
        if (_natPunchServerEndpoint == null) return;

        _writer.Reset();
        _writer.Put((byte)2);  // Punch request message type
        _writer.Put(_localToken);
        _writer.Put(_targetHostToken);
        _netManager.SendUnconnectedMessage(_writer, _natPunchServerEndpoint);
        _log.Debug("Sending NAT punch request: {LocalToken} -> {TargetToken}", _localToken, _targetHostToken);
    }

    /// <summary>
    /// Poll for network events. Call every frame.
    /// </summary>
    public void Poll()
    {
        _netManager.PollEvents();
        _netManager.NatPunchModule.PollEvents();

        // Handle NAT punch timeout and retry
        if (_waitingForNatPunch)
        {
            double elapsed = (DateTime.UtcNow - _natPunchStartTime).TotalSeconds;
            if (elapsed > NatPunchTimeoutSeconds)
            {
                _waitingForNatPunch = false;
                _log.Warning("NAT punch timed out, falling back to direct connect");
                OnNatPunchResult?.Invoke(false, "NAT punch timed out");

                // Fall back to direct connection
                if (!string.IsNullOrEmpty(_pendingCoordinatorAddress))
                {
                    _log.Debug("Trying direct connect to {Address}:{Port}", _pendingCoordinatorAddress, _pendingCoordinatorPort);
                    _netManager.Connect(_pendingCoordinatorAddress, _pendingCoordinatorPort, ConnectionKey);
                }
                else
                {
                    // Try localhost fallback for same-machine testing
                    _log.Debug("No coordinator address, trying localhost:{Port}", _pendingCoordinatorPort);
                    _netManager.Connect("127.0.0.1", _pendingCoordinatorPort, ConnectionKey);
                }
            }
            else
            {
                double sinceLast = (DateTime.UtcNow - _lastNatPunchRequest).TotalSeconds;
                if (sinceLast > NatPunchRetryIntervalSeconds)
                {
                    _lastNatPunchRequest = DateTime.UtcNow;
                    SendNatPunchRequest();
                }
            }
        }

        // Host: periodically re-register with NAT punch server while waiting for connections
        if (_hostNatPunchRegistered && ConnectedPeerCount == 0)
        {
            double interval = _natPunchRegistrationConfirmed
                ? HostRegistrationIntervalSeconds
                : HostRegistrationIntervalSecondsInitial;
            double sinceLast = (DateTime.UtcNow - _lastHostRegistration).TotalSeconds;
            if (sinceLast > interval)
            {
                _lastHostRegistration = DateTime.UtcNow;
                RegisterWithNatPunchServer();
            }
        }

        // Non-coordinator: periodically re-register with NAT punch server until mesh is complete
        if (_peerNatPunchRegistered && !IsMeshComplete)
        {
            double interval = _natPunchRegistrationConfirmed
                ? HostRegistrationIntervalSeconds
                : HostRegistrationIntervalSecondsInitial;
            double sinceLast = (DateTime.UtcNow - _lastPeerRegistration).TotalSeconds;
            if (sinceLast > interval)
            {
                _lastPeerRegistration = DateTime.UtcNow;
                RegisterWithNatPunchServer();
            }
        }

        // Handle peer-to-peer NAT punch timeouts and retries
        PollPeerNatPunches();
    }

    /// <summary>
    /// Handle peer-to-peer NAT punch timeout and retry logic.
    /// </summary>
    private void PollPeerNatPunches()
    {
        for (int slot = 0; slot < MaxPlayers; slot++)
        {
            if (!_pendingPeerPunch[slot]) continue;

            double elapsed = (DateTime.UtcNow - _peerPunchStartTime[slot]).TotalSeconds;
            if (elapsed > NatPunchTimeoutSeconds)
            {
                // Timeout - fall back to direct connection
                _pendingPeerPunch[slot] = false;
                _log.Warning("Peer NAT punch timed out for slot {Slot}, falling back to direct connect", slot);

                // Try direct connection using stored endpoint
                if (_meshPeers[slot].IsValid && !_meshPeers[slot].IsConnected)
                {
                    string address = _meshPeers[slot].EndpointAddress;
                    int port = _meshPeers[slot].EndpointPort;
                    if (!string.IsNullOrEmpty(address) && port > 0)
                    {
                        try
                        {
                            _netManager.Connect(address, port, ConnectionKey);
                        }
                        catch (Exception ex)
                        {
                            _log.Warning("Direct connect fallback failed for slot {Slot}: {Error}", slot, ex.Message);
                        }
                    }
                }
            }
            else
            {
                // Retry NAT punch request
                double sinceLast = (DateTime.UtcNow - _lastPeerPunchRequest[slot]).TotalSeconds;
                if (sinceLast > NatPunchRetryIntervalSeconds)
                {
                    _lastPeerPunchRequest[slot] = DateTime.UtcNow;
                    SendPeerNatPunchRequest(slot);
                }
            }
        }
    }

    /// <summary>
    /// Send NAT punch request for a specific peer.
    /// </summary>
    private void SendPeerNatPunchRequest(int slot)
    {
        if (_natPunchServerEndpoint == null) return;
        if (!_meshPeers[slot].IsValid) return;

        string targetToken = _meshPeers[slot].NatToken;
        if (string.IsNullOrEmpty(targetToken)) return;

        _writer.Reset();
        _writer.Put((byte)2);  // Punch request message type
        _writer.Put(_localToken);
        _writer.Put(targetToken);
        _netManager.SendUnconnectedMessage(_writer, _natPunchServerEndpoint);
        _log.Debug("Sending peer NAT punch request: {LocalToken} -> {TargetToken} (slot {Slot})", _localToken, targetToken, slot);
    }

    /// <summary>
    /// Stop networking and disconnect all peers.
    /// </summary>
    public void Stop()
    {
        _netManager.Stop();

        for (int i = 0; i < MaxPlayers; i++)
        {
            _meshPeers[i] = PeerInfo.Empty;
        }

        _localSlot = -1;
        _isCoordinator = false;
    }

    // --- Mesh peer access ---

    /// <summary>
    /// Get a copy of the mesh peers array for reading.
    /// </summary>
    public void GetMeshPeers(PeerInfo[] destination)
    {
        for (int i = 0; i < MaxPlayers && i < destination.Length; i++)
        {
            destination[i] = _meshPeers[i];
        }
    }

    /// <summary>
    /// Get peer info for a specific slot.
    /// </summary>
    public PeerInfo GetPeer(int slot)
    {
        if (slot >= 0 && slot < MaxPlayers)
        {
            return _meshPeers[slot];
        }
        return PeerInfo.Empty;
    }

    /// <summary>
    /// Get ping values for all peer slots. Returns -1 for empty/disconnected slots.
    /// </summary>
    public void GetPeerPings(Span<int> pings)
    {
        for (int i = 0; i < MaxPlayers && i < pings.Length; i++)
        {
            pings[i] = _meshPeers[i].Connection?.Ping ?? -1;
        }
    }

    /// <summary>
    /// Find lowest connected slot (for coordinator election).
    /// </summary>
    public int FindLowestConnectedSlot()
    {
        // Always include self
        int lowest = _localSlot;

        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_meshPeers[i].IsValid && _meshPeers[i].IsConnected && _meshPeers[i].PlayerSlot < lowest)
            {
                lowest = _meshPeers[i].PlayerSlot;
            }
        }

        return lowest;
    }

    // --- Send methods (Coordinator functions) ---

    /// <summary>
    /// Send peer list to all connected peers (coordinator only).
    /// Called when a new peer joins so all peers can connect to each other.
    /// </summary>
    public void SendPeerList()
    {
        _log.Debug("SendPeerList called");
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.PeerList);

        // Count valid peers
        int peerCount = 0;
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_meshPeers[i].IsValid)
            {
                peerCount++;
            }
        }

        _log.Debug("SendPeerList: peerCount={PeerCount}", peerCount);
        _writer.Put((byte)peerCount);

        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_meshPeers[i].IsValid)
            {
                SerializePeerInfo(_writer, _meshPeers[i]);
            }
        }

        BroadcastToMesh();
    }

    /// <summary>
    /// Send match start signal to all peers (coordinator only).
    /// Session seed is already distributed via SendLobbySync before this is called.
    /// </summary>
    public void SendMatchStart()
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.MatchStart);
        BroadcastToMesh();
    }

    /// <summary>
    /// Send start countdown signal to all peers (coordinator only).
    /// </summary>
    public void SendStartCountdown()
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.StartCountdown);
        BroadcastToMesh();
    }

    /// <summary>
    /// Announce that we are the new coordinator (after election).
    /// </summary>
    public void SendCoordinatorAnnounce()
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.CoordinatorAnnounce);
        _writer.Put(_localSlot);
        BroadcastToMesh();
    }

    // --- Send methods (Any peer) ---

    /// <summary>
    /// Send join request to coordinator with our identity.
    /// </summary>
    public void SendPeerJoin()
    {
        _log.Debug("SendPeerJoin: clientId={ClientId}, displayName={DisplayName}, port={Port}", _localClientId, _localDisplayName, _listenPort);
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.ClientJoin);
        _writer.PutBytesWithLength(_localClientId.ToByteArray());
        _writer.Put(_localDisplayName);
        _writer.Put(_listenPort);  // Include our listening port for P2P mesh
        SendToCoordinator();
    }

    /// <summary>
    /// Send ready state to coordinator.
    /// </summary>
    public void SendReady(bool ready)
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.Ready);
        _writer.Put(_localSlot);
        _writer.Put(ready);
        SendToCoordinator();
    }

    /// <summary>
    /// Send load complete notification.
    /// </summary>
    public void SendLoadComplete()
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.LoadComplete);
        _writer.Put(_localSlot);
        SendToCoordinator();
    }

    /// <summary>
    /// Send restart ready notification to all peers.
    /// </summary>
    public void SendRestartReady()
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.RestartReady);
        _writer.Put(_localSlot);
        BroadcastToMesh();
    }

    /// <summary>
    /// Send hello to a specific peer when we connect to them.
    /// </summary>
    public void SendPeerHello(NetPeer peer)
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.PeerHello);
        _writer.Put(_localSlot);
        _writer.PutBytesWithLength(_localClientId.ToByteArray());
        _writer.Put(_localDisplayName);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Send acknowledgment to a peer after receiving their hello.
    /// </summary>
    public void SendPeerAck(NetPeer peer)
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.PeerAck);
        _writer.Put(_localSlot);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Announce to all peers that we are fully connected to the mesh.
    /// </summary>
    public void SendMeshReady()
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.MeshReady);
        _writer.Put(_localSlot);
        BroadcastToMesh();
    }

    // --- Routing helpers ---

    private void BroadcastToMesh()
    {
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_meshPeers[i].IsValid && _meshPeers[i].PlayerSlot != _localSlot && _meshPeers[i].Connection != null)
            {
                _meshPeers[i].Connection.Send(_writer, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    private void BroadcastToMeshUnreliable()
    {
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_meshPeers[i].IsValid && _meshPeers[i].PlayerSlot != _localSlot && _meshPeers[i].Connection != null)
            {
                _meshPeers[i].Connection.Send(_writer, DeliveryMethod.Unreliable);
            }
        }
    }

    // --- Raw bytes API (for game-agnostic message passing) ---

    /// <summary>
    /// Send raw message to a specific peer slot (reliable).
    /// </summary>
    public void SendReliable(int slot, byte messageType, ReadOnlySpan<byte> data)
    {
        if (slot < 0 || slot >= MaxPlayers || !_meshPeers[slot].IsValid || _meshPeers[slot].Connection == null)
            return;

        _writer.Reset();
        _writer.Put(messageType);
        _writer.Put(data.ToArray());
        _meshPeers[slot].Connection.Send(_writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Send raw message to coordinator (reliable).
    /// </summary>
    public void SendReliableToCoordinator(byte messageType, ReadOnlySpan<byte> data)
    {
        SendReliable(_coordinatorSlot, messageType, data);
    }

    /// <summary>
    /// Broadcast raw message to all peers (reliable).
    /// </summary>
    public void BroadcastReliable(byte messageType, ReadOnlySpan<byte> data)
    {
        _writer.Reset();
        _writer.Put(messageType);
        _writer.Put(data.ToArray());
        BroadcastToMesh();
    }

    /// <summary>
    /// Broadcast raw message to all peers (unreliable, for low latency).
    /// </summary>
    public void BroadcastUnreliable(byte messageType, ReadOnlySpan<byte> data)
    {
        _writer.Reset();
        _writer.Put(messageType);
        _writer.Put(data.ToArray());
        BroadcastToMeshUnreliable();
    }

    /// <summary>
    /// Send state hash for desync detection (reliable ordered).
    /// </summary>
    public void SendSyncCheck(int frame, ulong stateHash)
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.SyncCheck);
        _writer.Put(_localSlot);
        _writer.Put(frame);
        _writer.Put(stateHash);
        BroadcastToMesh();
    }

    public void SendDesyncNotify(int frame, ulong localHash, ulong remoteHash)
    {
        _writer.Reset();
        _writer.Put((byte)InfraMessageType.DesyncNotify);
        _writer.Put(_localSlot);
        _writer.Put(frame);
        _writer.Put(localHash);
        _writer.Put(remoteHash);
        BroadcastToMesh();
    }

    private void SendToCoordinator()
    {
        // If we are coordinator, this is a no-op (or handle locally)
        if (_isCoordinator) return;

        var coordinatorPeer = _meshPeers[_coordinatorSlot];
        if (coordinatorPeer.Connection != null)
        {
            coordinatorPeer.Connection.Send(_writer, DeliveryMethod.ReliableOrdered);
        }
    }

    /// <summary>
    /// Find the player slot for a given peer connection.
    /// Returns -1 if not found.
    /// </summary>
    private int GetSlotFromPeer(NetPeer peer)
    {
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_meshPeers[i].Connection == peer)
            {
                return _meshPeers[i].PlayerSlot;
            }
        }
        return -1;
    }

    /// <summary>
    /// Connect to a peer from the peer list.
    /// </summary>
    public void ConnectToPeer(string address, int port)
    {
        _netManager.Connect(address, port, ConnectionKey);
    }

    // --- Coordinator management ---

    /// <summary>
    /// Assign a slot to a new peer (coordinator only).
    /// Returns the assigned slot or -1 if full.
    /// </summary>
    public int AssignSlotForNewPeer(Guid clientId, string displayName, NetPeer peer, int listenPort)
    {
        // Find first empty slot (starting from 1, since 0 is coordinator)
        for (int slot = 1; slot < MaxPlayers; slot++)
        {
            if (!_meshPeers[slot].IsValid)
            {
                // Compute NatToken for this peer using the shared matchId
                string natToken = string.IsNullOrEmpty(_matchId)
                    ? string.Empty
                    : $"{_matchId}:{clientId}";

                // Use the client's listen port, not the ephemeral connection port
                _meshPeers[slot] = new PeerInfo
                {
                    ClientId = clientId,
                    PlayerSlot = slot,
                    Connection = peer,
                    EndpointAddress = peer.Address.ToString(),
                    EndpointPort = listenPort,  // Use client's actual listening port
                    Status = PeerStatus.Connected,
                    DisplayName = displayName,
                    IsReady = false,
                    IsLoaded = false,
                    IsMeshReady = false,
                    NatToken = natToken
                };
                return slot;
            }
        }
        return -1;
    }

    /// <summary>
    /// Become the coordinator (after election).
    /// </summary>
    public void BecomeCoordinator()
    {
        _isCoordinator = true;
        _coordinatorSlot = _localSlot;
    }

    /// <summary>
    /// Update coordinator slot (when we receive CoordinatorAnnounce).
    /// </summary>
    public void SetCoordinatorSlot(int slot)
    {
        _coordinatorSlot = slot;
        _isCoordinator = (_localSlot == slot);
    }

    /// <summary>
    /// Set local slot (after coordinator assigns it).
    /// </summary>
    public void SetLocalSlot(int slot)
    {
        _localSlot = slot;

        // Add self to mesh at the assigned slot with our NatToken
        _meshPeers[slot] = PeerInfo.CreateLocal(_localClientId, slot, _localDisplayName, _localToken);

        // Non-coordinator peers should register with NAT server for peer-to-peer connections
        if (!_isCoordinator && !string.IsNullOrEmpty(_matchId))
        {
            _peerNatPunchRegistered = true;
            _lastPeerRegistration = DateTime.UtcNow;
            RegisterWithNatPunchServer();
        }
    }

    /// <summary>
    /// Update a peer's ready state.
    /// </summary>
    public void SetPeerReady(int slot, bool ready)
    {
        if (slot >= 0 && slot < MaxPlayers && _meshPeers[slot].IsValid)
        {
            _meshPeers[slot].IsReady = ready;
        }
    }

    /// <summary>
    /// Update a peer's loaded state.
    /// </summary>
    public void SetPeerLoaded(int slot, bool loaded)
    {
        if (slot >= 0 && slot < MaxPlayers && _meshPeers[slot].IsValid)
        {
            _meshPeers[slot].IsLoaded = loaded;
        }
    }

    /// <summary>
    /// Update a peer's mesh ready state.
    /// </summary>
    public void SetPeerMeshReady(int slot, bool meshReady)
    {
        if (slot >= 0 && slot < MaxPlayers && _meshPeers[slot].IsValid)
        {
            _meshPeers[slot].IsMeshReady = meshReady;
        }
    }

    // --- Serialization helpers ---

    private static void SerializePeerInfo(NetDataWriter writer, in PeerInfo peer)
    {
        writer.PutBytesWithLength(peer.ClientId.ToByteArray());
        writer.Put(peer.PlayerSlot);
        writer.Put(peer.DisplayName);
        writer.Put(peer.EndpointAddress);
        writer.Put(peer.EndpointPort);
        writer.Put(peer.IsReady);
        writer.Put(peer.IsLoaded);
        writer.Put(peer.NatToken ?? string.Empty);
    }

    private static PeerInfo DeserializePeerInfo(NetDataReader reader)
    {
        var clientId = new Guid(reader.GetBytesWithLength());
        int slot = reader.GetInt();
        string name = reader.GetString();
        string address = reader.GetString();
        int port = reader.GetInt();
        bool ready = reader.GetBool();
        bool loaded = reader.GetBool();
        string natToken = reader.GetString();

        return new PeerInfo
        {
            ClientId = clientId,
            PlayerSlot = slot,
            Connection = null, // Connection will be established separately
            EndpointAddress = address,
            EndpointPort = port,
            Status = PeerStatus.Unknown,
            DisplayName = name,
            IsReady = ready,
            IsLoaded = loaded,
            IsMeshReady = false,
            NatToken = natToken
        };
    }

    // --- INetEventListener implementation ---

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        _log.Debug("OnPeerConnected: {Address}:{Port}", peer.Address, peer.Port);

        // When a peer connects, we need to identify them
        // Coordinator: wait for ClientJoin message
        // Non-coordinator: could be coordinator or another peer from PeerList

        if (_isCoordinator)
        {
            // Coordinator waits for ClientJoin to identify and assign slot
            // Store temporarily until identified
        }
        else
        {
            // We connected to someone
            if (_localSlot < 0)
            {
                // First connection - we're connecting to coordinator
                // Store coordinator connection so SendToCoordinator() works
                _meshPeers[_coordinatorSlot] = new PeerInfo
                {
                    ClientId = Guid.Empty, // Will be set when we receive peer list
                    PlayerSlot = _coordinatorSlot,
                    Connection = peer,
                    EndpointAddress = peer.Address.ToString(),
                    EndpointPort = peer.Port,
                    Status = PeerStatus.Connected,
                    DisplayName = string.Empty,
                    IsReady = false,
                    IsLoaded = false,
                    IsMeshReady = false
                };

                // Now send PeerJoin to request slot assignment
                SendPeerJoin();
            }
            else
            {
                // We already have a slot - connecting to another peer from PeerList
                SendPeerHello(peer);
            }
        }
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _log.Debug("OnPeerDisconnected: {Address}:{Port}, reason={Reason}", peer.Address, peer.Port, disconnectInfo.Reason);

        // Find which peer disconnected
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_meshPeers[i].Connection == peer)
            {
                int slot = _meshPeers[i].PlayerSlot;
                _meshPeers[i].Status = PeerStatus.Disconnected;
                _meshPeers[i].Connection = null;
                OnPeerDisconnected?.Invoke(slot);

                // Check if coordinator disconnected
                if (slot == _coordinatorSlot)
                {
                    // Trigger election
                    int newCoordinatorSlot = FindLowestConnectedSlot();
                    if (newCoordinatorSlot == _localSlot)
                    {
                        BecomeCoordinator();
                        SendCoordinatorAnnounce();
                    }
                }

                break;
            }
        }
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (reader.AvailableBytes < 1) return;

        byte messageTypeByte = reader.GetByte();
        int fromSlot = GetSlotFromPeer(peer);

        // Try to interpret as infrastructure message type
        var infraType = (InfraMessageType)messageTypeByte;

        switch (infraType)
        {
            // --- Mixed messages: infrastructure update + route to game layer ---
            case InfraMessageType.MatchStart:
            case InfraMessageType.StartCountdown:
            case InfraMessageType.SyncCheck:
            case InfraMessageType.DesyncNotify:
            case InfraMessageType.RestartReady:
                // These are sent by NetworkService but handled by game coordinator
                OnRawMessageReceived?.Invoke(fromSlot, messageTypeByte, reader);
                break;

            case InfraMessageType.Ready:
                {
                    // Infrastructure: update peer ready state
                    if (reader.AvailableBytes >= 5)
                    {
                        int slot = reader.PeekInt();
                        bool ready = reader.PeekBool();
                        SetPeerReady(slot, ready);
                    }
                    // Route to game coordinator
                    OnRawMessageReceived?.Invoke(fromSlot, messageTypeByte, reader);
                }
                break;
            case InfraMessageType.LoadComplete:
                {
                    // Infrastructure: update peer loaded state
                    if (reader.AvailableBytes >= 4)
                    {
                        int slot = reader.PeekInt();
                        SetPeerLoaded(slot, true);
                    }
                    // Route to game coordinator
                    OnRawMessageReceived?.Invoke(fromSlot, messageTypeByte, reader);
                }
                break;

            // --- Pure infrastructure messages: handle directly in NetworkService ---
            case InfraMessageType.ClientJoin:
                HandlePeerJoinMessage(peer, reader);
                break;
            case InfraMessageType.PeerList:
                HandlePeerListMessage(reader);
                break;
            case InfraMessageType.PeerHello:
                HandlePeerHelloMessage(peer, reader);
                break;
            case InfraMessageType.PeerAck:
                HandlePeerAckMessage(peer, reader);
                break;
            case InfraMessageType.MeshReady:
                HandleMeshReadyMessage(reader);
                break;
            case InfraMessageType.CoordinatorAnnounce:
                HandleCoordinatorAnnounceMessage(reader);
                break;

            // --- Game-specific messages: route via OnRawMessageReceived ---
            default:
                // Unknown message types are game-specific (e.g., LobbySync, Input)
                OnRawMessageReceived?.Invoke(fromSlot, messageTypeByte, reader);
                break;
        }
    }

    private void HandlePeerJoinMessage(NetPeer peer, NetDataReader reader)
    {
        _log.Debug("HandlePeerJoinMessage: isCoordinator={IsCoordinator}", _isCoordinator);
        // Coordinator receives join request from new peer
        if (!_isCoordinator) return;

        var clientId = new Guid(reader.GetBytesWithLength());
        string displayName = reader.GetString();
        int listenPort = reader.GetInt();  // Client's actual listening port for P2P

        _log.Debug("Received ClientJoin: clientId={ClientId}, displayName={DisplayName}, port={Port}", clientId, displayName, listenPort);

        int assignedSlot = AssignSlotForNewPeer(clientId, displayName, peer, listenPort);
        _log.Debug("Assigned slot={Slot} for clientId={ClientId}", assignedSlot, clientId);
        if (assignedSlot >= 0)
        {
            OnPeerJoinReceived?.Invoke(assignedSlot, clientId, displayName);
        }
    }

    private void HandlePeerListMessage(NetDataReader reader)
    {
        int peerCount = reader.GetByte();
        _log.Debug("HandlePeerListMessage: peerCount={PeerCount}", peerCount);
        var peers = new PeerInfo[peerCount];

        for (int i = 0; i < peerCount; i++)
        {
            peers[i] = DeserializePeerInfo(reader);

            // Update our mesh with peer info
            int slot = peers[i].PlayerSlot;

            if (slot >= 0 && slot < MaxPlayers && slot != _localSlot)
            {
                // Preserve existing connection if we have one
                var existingConnection = _meshPeers[slot].Connection;
                _meshPeers[slot] = peers[i];
                if (existingConnection != null)
                {
                    _meshPeers[slot].Connection = existingConnection;
                    _meshPeers[slot].Status = PeerStatus.Connected;
                }
            }
            else if (slot == _localSlot && _localSlot < 0)
            {
                // This is us - coordinator told us our slot
                SetLocalSlot(slot);
            }
        }

        // Connect to other peers (not coordinator - we're already connected to them)
        ConnectToOtherPeers();

        OnPeerListReceived?.Invoke(peers);
    }

    private void ConnectToOtherPeers()
    {
        if (_netManager == null) return;

        for (int slot = 0; slot < MaxPlayers; slot++)
        {
            // Skip self
            if (slot == _localSlot) continue;

            // Skip coordinator (already connected)
            if (slot == _coordinatorSlot) continue;

            // Skip invalid or already connected peers
            if (!_meshPeers[slot].IsValid) continue;
            if (_meshPeers[slot].IsConnected) continue;

            // Skip peers we're already trying to punch through to
            if (_pendingPeerPunch[slot]) continue;

            // Use NAT punch if peer has a token, otherwise fall back to direct connect
            string peerToken = _meshPeers[slot].NatToken;
            if (!string.IsNullOrEmpty(peerToken) && _natPunchServerEndpoint != null && !string.IsNullOrEmpty(_localToken))
            {
                // Initiate NAT punch for this peer
                _pendingPeerPunch[slot] = true;
                _peerPunchStartTime[slot] = DateTime.UtcNow;
                _lastPeerPunchRequest[slot] = DateTime.UtcNow;
                SendPeerNatPunchRequest(slot);
                _log.Debug("Initiating NAT punch to peer slot {Slot}", slot);
            }
            else
            {
                // Fall back to direct connection (no NAT token or server)
                string address = _meshPeers[slot].EndpointAddress;
                int port = _meshPeers[slot].EndpointPort;

                try
                {
                    _log.Debug("Direct connecting to peer slot {Slot} at {Address}:{Port}", slot, address, port);
                    _netManager.Connect(address, port, ConnectionKey);
                }
                catch (Exception ex)
                {
                    _log.Warning("Direct connect failed for slot {Slot}: {Error}", slot, ex.Message);
                }
            }
        }
    }

    private void HandlePeerHelloMessage(NetPeer peer, NetDataReader reader)
    {
        int slot = reader.GetInt();
        var clientId = new Guid(reader.GetBytesWithLength());
        string displayName = reader.GetString();

        // Update mesh with this peer's connection
        if (slot >= 0 && slot < MaxPlayers)
        {
            _meshPeers[slot] = new PeerInfo
            {
                ClientId = clientId,
                PlayerSlot = slot,
                Connection = peer,
                EndpointAddress = peer.Address.ToString(),
                EndpointPort = peer.Port,
                Status = PeerStatus.Connected,
                DisplayName = displayName,
                IsReady = _meshPeers[slot].IsReady,
                IsLoaded = _meshPeers[slot].IsLoaded,
                IsMeshReady = false
            };
        }

        // Send ack back
        SendPeerAck(peer);

        OnPeerHelloReceived?.Invoke(slot, clientId, displayName);
        OnPeerConnected?.Invoke(slot);

        // Check if mesh is now complete
        if (IsMeshComplete)
        {
            SendMeshReady();
        }
    }

    private void HandlePeerAckMessage(NetPeer peer, NetDataReader reader)
    {
        int slot = reader.GetInt();

        // Update connection reference if we didn't have it
        if (slot >= 0 && slot < MaxPlayers && _meshPeers[slot].Connection == null)
        {
            _meshPeers[slot].Connection = peer;
            _meshPeers[slot].Status = PeerStatus.Connected;
        }

        OnPeerAckReceived?.Invoke(slot);
        OnPeerConnected?.Invoke(slot);

        // Check if mesh is now complete
        if (IsMeshComplete)
        {
            SendMeshReady();
        }
    }

    private void HandleMeshReadyMessage(NetDataReader reader)
    {
        int slot = reader.GetInt();
        SetPeerMeshReady(slot, true);
        OnMeshReadyReceived?.Invoke(slot);
    }

    private void HandleCoordinatorAnnounceMessage(NetDataReader reader)
    {
        int newCoordinatorSlot = reader.GetInt();
        SetCoordinatorSlot(newCoordinatorSlot);
        OnCoordinatorAnnounceReceived?.Invoke(newCoordinatorSlot);
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        _log.Error("Socket error: {SocketError} from {EndPoint}", socketError, endPoint);
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        if (reader.AvailableBytes < 1) return;

        byte msgType = reader.GetByte();

        // Handle NAT punch registration confirmation (msgType 1)
        if (msgType == 1 && reader.AvailableBytes >= 1)
        {
            bool success = reader.GetBool();
            if (success && !_natPunchRegistrationConfirmed)
            {
                _natPunchRegistrationConfirmed = true;
                _log.Debug("NAT punch registration confirmed by server");
            }
        }
    }

    // --- INatPunchListener implementation ---

    void INatPunchListener.OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        _log.Debug("NAT introduction request! local={LocalEndPoint}, remote={RemoteEndPoint}, token={Token}", localEndPoint, remoteEndPoint, token);
    }

    void INatPunchListener.OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        _log.Debug("NAT punch SUCCESS! Target={TargetEndPoint}, type={Type}, token={Token}", targetEndPoint, type, token);

        // Check if this is a coordinator punch (initial connection) or peer-to-peer punch
        if (_waitingForNatPunch && token == _targetHostToken)
        {
            // This is the initial coordinator punch
            _waitingForNatPunch = false;

            if (!_isCoordinator)
            {
                // Client: connect to the punched endpoint (coordinator)
                _log.Debug("Client connecting to coordinator via punched endpoint {TargetEndPoint}", targetEndPoint);
                _netManager.Connect(targetEndPoint, ConnectionKey);
            }
            else
            {
                // Host: wait for connection from the punched endpoint
                _log.Debug("Host waiting for connection from {TargetEndPoint}", targetEndPoint);
            }

            OnNatPunchResult?.Invoke(true, $"Connected via {type}");
        }
        else
        {
            // This is a peer-to-peer punch - find which peer by matching token
            int peerSlot = FindPeerSlotByToken(token);
            if (peerSlot >= 0)
            {
                _pendingPeerPunch[peerSlot] = false;
                _log.Debug("Peer NAT punch SUCCESS for slot {Slot}, connecting to {TargetEndPoint}", peerSlot, targetEndPoint);

                // Connect to the punched endpoint
                _netManager.Connect(targetEndPoint, ConnectionKey);
            }
            else
            {
                // Might be the other direction - the peer punched to us
                // Just connect to whoever punched through
                _log.Debug("Received NAT punch from unknown token {Token}, connecting to {TargetEndPoint}", token, targetEndPoint);
                _netManager.Connect(targetEndPoint, ConnectionKey);
            }
        }
    }

    /// <summary>
    /// Find the peer slot with the given NAT token.
    /// </summary>
    private int FindPeerSlotByToken(string token)
    {
        for (int slot = 0; slot < MaxPlayers; slot++)
        {
            if (_meshPeers[slot].IsValid && _meshPeers[slot].NatToken == token)
            {
                return slot;
            }
        }
        return -1;
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // Could track latency for display
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        _log.Debug("OnConnectionRequest from {RemoteEndPoint}", request.RemoteEndPoint);

        // Accept connections if we have room
        int connectedCount = 0;
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_meshPeers[i].IsValid)
            {
                connectedCount++;
            }
        }

        if (connectedCount < MaxPlayers)
        {
            _log.Debug("Accepting connection ({ConnectedCount}/{MaxPlayers} peers)", connectedCount, MaxPlayers);
            request.AcceptIfKey(ConnectionKey);
        }
        else
        {
            _log.Warning("Rejecting connection (full)");
            request.Reject();
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
