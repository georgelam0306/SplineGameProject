using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using Catrillion.AppState;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Rollback;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;
using Core;
using DerpTech.Rollback;
using LiteNetLib.Utils;
using Networking;

namespace Catrillion.OnlineServices;

/// <summary>
/// Handles game-time network coordination: input sync, sync checks, desync detection, and restart.
/// This is a game-level component - instantiated per game session.
/// </summary>
public sealed class NetworkCoordinator : IDisposable
{
    private readonly ILogger _log;
    private readonly NetworkService _networkService;
    private readonly RollbackManager<GameInput> _rollbackManager;
    private readonly GameSimulation _gameSimulation;
    private readonly SimWorld _simWorld;
    private readonly DerivedSystemRunner _derivedRunner;
    private readonly AppEventBus _eventBus;
    private readonly GameDataManager<GameDocDb> _gameDataManager;

    // Coordinator status - can change via election if host disconnects
    private bool _isCoordinator;
    private int _coordinatorSlot;

    // Dev-only: Hot-reload sync - frames to delay before applying data changes
    private const int HotReloadFrameDelay = 30;

    // Pending restart seed (used during restart coordination)
    private int _pendingRestartSeed;

    // Restart ready tracking
    private readonly bool[] _restartReadyFlags;

    // Writer for serializing game-specific network messages
    private readonly NetDataWriter _writer = new();

    // Game-time network I/O state
    private int _localPlayerSlot;
    private int _playerCount;
    private int _lastSyncCheckFrame = -SyncCheckInterval;

    // Constants for game-time networking
    private const int SyncCheckInterval = 1;  // Every tick
    private const int MaxFramesAheadOfConfirmed = 4;  // Pause if too far ahead of remote inputs
    private static readonly int GameInputSize = Unsafe.SizeOf<GameInput>();  // Blittable size for serialization

    // Optional background sync validation (standalone desync detection)
    private BackgroundSyncValidator? _syncValidator;
    private int _lastValidatorFrame = -1;  // Track last frame sent to validator
    private DesyncExportHandler? _desyncExportHandler;  // Handles export on main thread

    /// <summary>
    /// Returns true if we're in a networked game with an active connection.
    /// </summary>
    public bool IsNetworked => _playerCount > 1 && _networkService.IsConnected;

    /// <summary>
    /// Returns true if this is a single-player game (no network connection).
    /// </summary>
    public bool IsSinglePlayer => !_networkService.IsConnected;

    /// <summary>
    /// Returns true if this client is the coordinator.
    /// </summary>
    public bool IsCoordinator => _isCoordinator;

    /// <summary>
    /// Local player slot.
    /// </summary>
    public int LocalSlot => _networkService.LocalSlot;

    /// <summary>
    /// Number of connected peers.
    /// </summary>
    public int ConnectedPeerCount => _networkService.ConnectedPeerCount;

    /// <summary>
    /// Get ping values for all peer slots.
    /// </summary>
    public void GetPeerPings(Span<int> pings) => _networkService.GetPeerPings(pings);

    /// <summary>
    /// Whether latency simulation is enabled.
    /// </summary>
    public bool IsSimulatingLatency => _networkService.IsSimulatingLatency;

    /// <summary>
    /// Configured simulated latency range in ms (min, max). Returns (0,0) if disabled.
    /// </summary>
    public (int Min, int Max) SimulatedLatencyRange =>
        (_networkService.SimulatedMinLatency, _networkService.SimulatedMaxLatency);

    public NetworkCoordinator(
        ILogger logger,
        NetworkService networkService,
        RollbackManager<GameInput> rollbackManager,
        GameSimulation gameSimulation,
        SimWorld simWorld,
        DerivedSystemRunner derivedRunner,
        InitialCoordinator isInitialCoordinator,
        AppEventBus eventBus,
        GameDataManager<GameDocDb> gameDataManager)
    {
        _log = logger.ForContext<NetworkCoordinator>();
        _networkService = networkService;
        _rollbackManager = rollbackManager;
        _gameSimulation = gameSimulation;
        _simWorld = simWorld;
        _derivedRunner = derivedRunner;
        _eventBus = eventBus;
        _gameDataManager = gameDataManager;
        _isCoordinator = isInitialCoordinator.Value;
        _gameDataManager.IsCoordinator = _isCoordinator;
        _coordinatorSlot = isInitialCoordinator.Value ? 0 : -1;  // Coordinator is slot 0 initially
        _restartReadyFlags = new bool[LobbyState.MaxPlayers];

        // Subscribe to raw message routing for game-specific messages
        _networkService.OnRawMessageReceived += HandleRawMessage;

        // Subscribe to peer disconnect for coordinator election
        _networkService.OnPeerDisconnected += HandlePeerDisconnected;

        // Dev-only: Subscribe to game data changes for hot-reload sync
        _gameDataManager.OnDataChangeDetected += HandleGameDataChangeDetected;
    }

    /// <summary>
    /// Set the optional background sync validator for standalone desync detection.
    /// When enabled, confirmed frame snapshots are sent to the validator for
    /// background hash computation and comparison with remote hashes.
    /// </summary>
    public void SetSyncValidator(BackgroundSyncValidator? validator)
    {
        _syncValidator = validator;
        _lastValidatorFrame = -1;
        if (validator != null)
        {
            validator.LocalPlayerId = (byte)_localPlayerSlot;
            // Create export handler for main thread export (background thread shouldn't touch SimWorld)
            _desyncExportHandler = new DesyncExportHandler(_simWorld, _rollbackManager, _gameSimulation, _derivedRunner, _log);
            _desyncExportHandler.SetLocalPlayerId((byte)_localPlayerSlot);
        }
    }

    // --- Coordinator Election ---

    private void HandlePeerDisconnected(int playerSlot)
    {
        if (IsSinglePlayer) return;

        // Check if coordinator disconnected
        if (playerSlot == _coordinatorSlot)
        {
            ElectNewCoordinator();
        }
    }

    private void ElectNewCoordinator()
    {
        int lowestSlot = _networkService.FindLowestConnectedSlot();
        _coordinatorSlot = lowestSlot;

        if (lowestSlot == _localPlayerSlot)
        {
            _isCoordinator = true;
            _gameDataManager.IsCoordinator = true;
            _log.Information("This client elected as new coordinator (slot {Slot})", lowestSlot);
            _networkService.SendCoordinatorAnnounce();
        }
        else
        {
            _isCoordinator = false;
            _gameDataManager.IsCoordinator = false;
            _log.Information("New coordinator elected: slot {Slot}", lowestSlot);
        }
    }

    // --- Game-time network I/O (outgoing) ---

    /// <summary>
    /// Configure player info for game-time networking.
    /// Call this when starting a game.
    /// </summary>
    public void SetPlayerInfo(int playerCount, int localPlayerSlot)
    {
        _playerCount = playerCount;
        _localPlayerSlot = localPlayerSlot;
        _lastSyncCheckFrame = -SyncCheckInterval;
        ClearRestartReadyFlags();

        // Set coordinator slot - coordinator is always slot 0 at game start
        if (_isCoordinator)
        {
            _coordinatorSlot = localPlayerSlot;
        }
    }

    /// <summary>
    /// Send local input to all peers. Call this every tick.
    /// </summary>
    public void SendInput(int frame, in GameInput input, MultiPlayerInputBuffer<GameInput> inputBuffer)
    {
        if (!IsNetworked) return;

        SendInputInternal(frame, _localPlayerSlot, in input, inputBuffer);
    }

    /// <summary>
    /// Check if we should pause simulation due to being too far ahead of confirmed inputs.
    /// This prevents drift between clients with different frame rates.
    /// </summary>
    public bool ShouldPauseForInputSync(int currentFrame, RollbackManager<GameInput> rollbackManager)
    {
        if (!IsNetworked) return false;

        int oldestUnconfirmed = rollbackManager.GetOldestUnconfirmedFrame();
        int framesAhead = currentFrame - oldestUnconfirmed + 1;
        return framesAhead > MaxFramesAheadOfConfirmed;
    }

    /// <summary>
    /// Send sync check if interval has elapsed. Call this every tick.
    /// Only sends sync checks for frames where ALL inputs are confirmed.
    /// </summary>
    public void TrySendSyncCheck(int currentFrame, RollbackManager<GameInput> rollbackManager)
    {
        if (!IsNetworked || _syncValidator == null) return;

        // Queue snapshot for background hash computation at sync check intervals
        if (currentFrame - _lastSyncCheckFrame >= SyncCheckInterval)
        {
            // Find the most recent confirmed frame that we can sync check
            int oldestUnconfirmed = rollbackManager.GetOldestUnconfirmedFrame();
            int syncCheckFrame = oldestUnconfirmed > 0 ? oldestUnconfirmed - 1 : currentFrame;

            if (syncCheckFrame > _lastValidatorFrame)
            {
                // Queue snapshot for background hash computation + sync check sending
                if (rollbackManager.TryGetSnapshotBytes(syncCheckFrame, currentFrame, out var snapshot))
                {
                    _syncValidator.QueueSyncCheck(syncCheckFrame, snapshot);
                    _lastValidatorFrame = syncCheckFrame;
                }
            }
        }

        // Poll for computed hashes ready to send (background thread finished)
        while (_syncValidator.PollPendingSyncCheck() is { } pending)
        {
            _networkService.SendSyncCheck(pending.Frame, pending.Hash);
            _lastSyncCheckFrame = pending.Frame;
        }
    }

    // --- Game Restart Coordination ---

    /// <summary>
    /// Request a restart (when local player is ready).
    /// For single player, restarts immediately.
    /// For multiplayer, sends ready flag and waits for all players.
    /// </summary>
    public void RequestRestart()
    {
        _log.Debug("RequestRestart called, IsSinglePlayer={IsSinglePlayer}", IsSinglePlayer);

        if (IsSinglePlayer)
        {
            // Single player - restart immediately with new seed
            _log.Debug("Single player - restarting immediately");
            _gameSimulation.GenerateNewSessionSeed();
            PerformRestart();
            return;
        }

        // Multiplayer coordinator: generate and broadcast new seed BEFORE sending RestartReady
        if (IsCoordinator)
        {
            BroadcastRestartSeed();
        }

        // Mark local ready
        if (_localPlayerSlot >= 0 && _localPlayerSlot < _restartReadyFlags.Length)
        {
            _restartReadyFlags[_localPlayerSlot] = true;
        }

        // Send to peers
        _networkService.SendRestartReady();

        // Check if all ready
        CheckAllReadyForRestart();
    }

    /// <summary>
    /// Broadcast a new restart seed to all peers (coordinator only).
    /// </summary>
    private void BroadcastRestartSeed()
    {
        _pendingRestartSeed = Environment.TickCount;
        _log.Debug("Broadcasting restart seed: {Seed}", _pendingRestartSeed);

        // Notify app layer about the new seed (for LobbyState update)
        _eventBus.PublishSessionSeed(_pendingRestartSeed);

        // Broadcast via LobbySync mechanism (this will be handled by the callback)
    }

    /// <summary>
    /// Get the restart ready state for all players.
    /// </summary>
    public bool[] GetRestartReadyFlags() => _restartReadyFlags;

    /// <summary>
    /// Get the ready count for restart.
    /// </summary>
    public int GetRestartReadyCount()
    {
        int count = 0;
        for (int i = 0; i < _playerCount; i++)
        {
            if (_restartReadyFlags[i])
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Check if a specific player is ready for restart.
    /// </summary>
    public bool IsPlayerRestartReady(int playerSlot)
    {
        if (playerSlot < 0 || playerSlot >= _restartReadyFlags.Length) return false;
        return _restartReadyFlags[playerSlot];
    }

    private void CheckAllReadyForRestart()
    {
        for (int i = 0; i < _playerCount; i++)
        {
            if (!_restartReadyFlags[i]) return;
        }

        // All ready - coordinator sends countdown signal
        if (IsCoordinator || IsSinglePlayer)
        {
            InitiateRestartCountdown();
        }
    }

    private void InitiateRestartCountdown()
    {
        _log.Information("InitiateRestartCountdown - starting synchronized restart");

        // Reset game state FIRST (before countdown)
        PerformRestart();

        // Send countdown signal to peers
        if (!IsSinglePlayer)
        {
            _networkService.SendStartCountdown();
        }

        // Notify app layer to transition to countdown screen
        _eventBus.PublishCountdownRequested();
    }

    private void PerformRestart()
    {
        ClearRestartReadyFlags();

        if (!IsSinglePlayer && _pendingRestartSeed != 0)
        {
            _gameSimulation.SetSessionSeed(_pendingRestartSeed);
        }

        // Publish restart event (Game subscribes and calls Restart())
        _eventBus.PublishRestartRequested();
    }

    private void ClearRestartReadyFlags()
    {
        for (int i = 0; i < _restartReadyFlags.Length; i++)
        {
            _restartReadyFlags[i] = false;
        }
    }

    // --- Dev-only: Hot-Reload Sync ---

    /// <summary>
    /// Called when GameDataManager detects a file change.
    /// If coordinator, broadcasts the target frame to all clients.
    /// </summary>
    private void HandleGameDataChangeDetected()
    {
        if (!IsNetworked)
        {
            // Single player - apply immediately at next frame
            _gameDataManager.ScheduleReload(_gameSimulation.CurrentFrame + 1);
            return;
        }

        if (!IsCoordinator)
        {
            // Non-coordinator clients wait for coordinator's broadcast
            _log.Debug("Game data change detected, waiting for coordinator to broadcast target frame");
            return;
        }

        // Coordinator: broadcast target frame to all clients
        int targetFrame = _gameSimulation.CurrentFrame + HotReloadFrameDelay;
        _log.Information("Game data change detected, broadcasting reload at frame {TargetFrame}", targetFrame);

        // Schedule local reload
        _gameDataManager.ScheduleReload(targetFrame);

        // Broadcast to all peers
        _writer.Reset();
        _writer.Put(targetFrame);
        _networkService.BroadcastReliable((byte)NetMessageType.GameDataReload, _writer.Data.AsSpan(0, _writer.Length));
    }

    /// <summary>
    /// Check and apply any scheduled game data reload.
    /// Call this from the game loop at the start of each simulation tick.
    /// </summary>
    public void TryApplyScheduledGameDataReload(int currentFrame)
    {
        _gameDataManager.TryApplyScheduledReload(currentFrame);
    }

    // --- Sync Check Processing ---

    /// <summary>
    /// Process pending sync checks after simulation update.
    /// Call this from Game after Update() to handle desyncs.
    /// </summary>
    public void ProcessPendingSyncChecks()
    {
        bool wasDesync = _rollbackManager.DesyncDetected;
        bool desyncDetected = _rollbackManager.ProcessPendingSyncChecks();

        // Notify peers when desync first detected (export handled by background validator)
        if (!wasDesync && desyncDetected)
        {
            _networkService.SendDesyncNotify(
                _rollbackManager.DesyncFrame,
                _rollbackManager.LocalDesyncHash,
                _rollbackManager.RemoteDesyncHash
            );
        }

        // Poll background validator for desync results (if enabled)
        if (_syncValidator?.Enabled == true)
        {
            var desyncInfo = _syncValidator.PollDesyncResult();
            if (desyncInfo.HasValue)
            {
                var info = desyncInfo.Value;
                _log.Warning("Background validator detected desync at frame {Frame} (local={LocalHash:X16} remote={RemoteHash:X16})",
                    info.Frame, info.LocalHash, info.RemoteHash);

                // Set desync state on rollback manager to pause game
                _rollbackManager.SetDesyncFromLocal(info.Frame, info.LocalHash, info.RemoteHash, info.RemotePlayerId);

                // Notify peers
                _networkService.SendDesyncNotify(info.Frame, info.LocalHash, info.RemoteHash);

                // Queue export for main thread processing (safe to call RestoreSnapshot here)
                _desyncExportHandler?.QueueExport(info);
            }
        }

        // Process pending desync export on main thread (safe to call RestoreSnapshot)
        _desyncExportHandler?.ProcessPendingExport();
    }

    // --- Message Handlers ---

    private void HandleInputReceived(int frame, int playerSlot, GameInput input)
    {
        // Queue input for batch processing at frame start
        _rollbackManager.EnqueueInput(frame, playerSlot, in input);
    }

    private void HandleSyncCheckReceived(byte playerId, int frame, ulong hash)
    {
        // Queue sync check for processing after simulation update
        _rollbackManager.EnqueueSyncCheck(playerId, frame, hash);

        // Also route to background validator if enabled
        _syncValidator?.OnRemoteHash(playerId, frame, hash);
    }

    private void HandleDesyncNotifyReceived(byte playerId, int frame, ulong peerLocalHash, ulong peerRemoteHash)
    {
        bool wasDesync = _rollbackManager.DesyncDetected;
        _rollbackManager.SetDesyncFromPeer(playerId, frame, peerLocalHash, peerRemoteHash);

        // Export handled by background validator when enabled
    }

    private void HandleRestartReadyReceived(int playerSlot)
    {
        if (playerSlot >= 0 && playerSlot < _restartReadyFlags.Length)
        {
            _restartReadyFlags[playerSlot] = true;
        }

        // Check if all ready for restart
        CheckAllReadyForRestart();
    }

    private void HandleStartCountdown()
    {
        // This is a restart countdown
        _log.Debug("HandleStartCountdown - restart case, resetting game");
        ClearRestartReadyFlags();
        PerformRestart();

        // Notify app layer to transition to countdown screen
        _eventBus.PublishCountdownRequested();
    }

    // --- Serialization helpers ---

    internal static void SerializeGameInput(NetDataWriter writer, in GameInput input)
    {
        // Zero-copy blittable serialization - automatically includes all fields
        Span<byte> buffer = stackalloc byte[GameInputSize];
        MemoryMarshal.Write(buffer, in input);
        writer.Put(buffer);
    }

    internal static GameInput DeserializeGameInput(NetDataReader reader)
    {
        // Zero-copy blittable deserialization - read directly from underlying buffer
        var result = MemoryMarshal.Read<GameInput>(reader.RawData.AsSpan(reader.Position, GameInputSize));
        reader.SkipBytes(GameInputSize);
        return result;
    }

    // --- Send methods ---

    /// <summary>
    /// Send local input to all peers (unreliable + redundancy for low latency recovery).
    /// </summary>
    private void SendInputInternal(int frame, int playerSlot, in GameInput input, MultiPlayerInputBuffer<GameInput> inputBuffer)
    {
        _writer.Reset();
        _writer.Put(playerSlot);

        // Count how many valid redundant frames exist (only those actually stored in buffer)
        // Max 4 total (1 current + up to 3 previous) to stay under packet size limit
        int validRedundantFrames = 1; // Always have current frame
        for (int i = 1; i < 4 && frame - i >= 0; i++)
        {
            if (inputBuffer.HasInput(frame - i, playerSlot))
                validRedundantFrames++;
            else
                break; // Stop at first missing frame to maintain contiguity
        }
        _writer.Put((byte)validRedundantFrames);

        // Write current frame
        _writer.Put(frame);
        SerializeGameInput(_writer, in input);

        // Write previous frames for redundancy (only those that exist)
        for (int i = 1; i < validRedundantFrames; i++)
        {
            int pastFrame = frame - i;
            ref readonly var pastInput = ref inputBuffer.GetInput(pastFrame, playerSlot);
            _writer.Put(pastFrame);
            SerializeGameInput(_writer, in pastInput);
        }

        _networkService.BroadcastUnreliable((byte)NetMessageType.Input, _writer.Data.AsSpan(0, _writer.Length));
    }

    /// <summary>
    /// Handle raw messages routed from NetworkService.
    /// Only handles game-specific messages (Input, SyncCheck, DesyncNotify, RestartReady, StartCountdown).
    /// </summary>
    private void HandleRawMessage(int fromSlot, byte messageType, NetDataReader reader)
    {
        var type = (NetMessageType)messageType;
        switch (type)
        {
            case NetMessageType.Input:
                {
                    // Need at least: playerSlot(4) + frameCount(1)
                    if (reader.AvailableBytes < 5) return;

                    int playerSlot = reader.GetInt();
                    int frameCount = reader.GetByte();

                    // Each frame needs: frame(4) + GameInput (blittable size)
                    for (int i = 0; i < frameCount && reader.AvailableBytes >= 4 + GameInputSize; i++)
                    {
                        int frame = reader.GetInt();
                        var input = DeserializeGameInput(reader);
                        HandleInputReceived(frame, playerSlot, input);
                    }
                }
                break;

            case NetMessageType.SyncCheck:
                {
                    // Need: slot(4) + frame(4) + hash(8) = 16 bytes
                    if (reader.AvailableBytes < 16) return;

                    int playerSlot = reader.GetInt();
                    int frame = reader.GetInt();
                    ulong hash = reader.GetULong();

                    HandleSyncCheckReceived((byte)playerSlot, frame, hash);
                }
                break;

            case NetMessageType.DesyncNotify:
                {
                    // Need: slot(4) + frame(4) + localHash(8) + remoteHash(8) = 24 bytes
                    if (reader.AvailableBytes < 24) return;

                    int playerSlot = reader.GetInt();
                    int frame = reader.GetInt();
                    ulong localHash = reader.GetULong();
                    ulong remoteHash = reader.GetULong();

                    HandleDesyncNotifyReceived((byte)playerSlot, frame, localHash, remoteHash);
                }
                break;

            case NetMessageType.RestartReady:
                {
                    if (reader.AvailableBytes < 4) return;
                    int slot = reader.GetInt();
                    HandleRestartReadyReceived(slot);
                }
                break;

            case NetMessageType.StartCountdown:
                // Handle restart countdown
                HandleStartCountdown();
                break;

            case NetMessageType.GameDataReload:
                {
                    // Dev-only: Coordinator broadcasts target frame for synchronized data reload
                    if (reader.AvailableBytes < 4) return;
                    int targetFrame = reader.GetInt();
                    HandleGameDataReloadReceived(targetFrame);
                }
                break;
        }
    }

    private void HandleGameDataReloadReceived(int targetFrame)
    {
        _log.Information("Received game data reload command for frame {TargetFrame}", targetFrame);
        _gameDataManager.ScheduleReload(targetFrame);
    }

    public void Dispose()
    {
        _networkService.OnRawMessageReceived -= HandleRawMessage;
        _networkService.OnPeerDisconnected -= HandlePeerDisconnected;
        _gameDataManager.OnDataChangeDetected -= HandleGameDataChangeDetected;
    }
}
