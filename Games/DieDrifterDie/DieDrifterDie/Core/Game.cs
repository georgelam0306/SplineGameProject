using System;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Pure.DI;
using R3;
using Raylib_cs;
using Serilog;
using DieDrifterDie.GameApp.AppState;
using DieDrifterDie.Presentation.Camera;
using DieDrifterDie.GameApp.Config;
using DieDrifterDie.Presentation.Input;
using DieDrifterDie.Infrastructure.Networking;
using DieDrifterDie.Infrastructure.Rollback;
using DieDrifterDie.Simulation;
using DieDrifterDie.Simulation.Components;
using DieDrifterDie.GameApp.Stores;
using DieDrifterDie.Presentation.Rendering;
using Core;
using DerpTech.Rollback;
using DerpTech.DesyncDetection;

namespace DieDrifterDie;

public sealed class Game : IDisposable
{
    private readonly ILogger _log;
    private readonly EntityStore _store;
    private readonly SystemRoot _updateRoot;
    private readonly SystemRoot _renderRoot;
    private readonly CameraManager _cameraManager;
    private readonly UIInputManager _uiInputManager;
    private readonly GameInputManager _gameInputManager;
    private readonly InputStore _inputStore;
    private readonly GameSimulation _gameSimulation;
    private readonly NetworkCoordinator _coordinator;
    private readonly DerivedSystemRunner _derivedRunner;
    private readonly RenderInterpolation _renderInterpolation;
    private readonly int _playerCount;

    // Game loop timing (moved from GameSimulation)
    private int _currentFrame;
    private float _timeAccumulator;
    private static readonly float FixedDeltaTime = 1f / SimulationConfig.TickRate;
    private const int MaxTicksPerFrame = 7;
    private const int MaxRollbackFrames = 7;

    private readonly ReplayManager<GameInput> _replayManager;
    private readonly IDisposable _restartSubscription;
    private readonly int _inputDelayFrames;

#if !PRODUCTION_BUILD
    // Optional background sync validation (standalone desync detection)
    private BackgroundSyncValidator? _syncValidator;
#endif

    // Update timing (exposed for InGameScreen profiler overlays)
    private double _normalUpdateMs, _syncChecksMs, _updateRootMs;
    private static readonly double TicksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    // NormalUpdate breakdown
    private double _rollbackCheckMs, _saveSnapshotMs, _simTickMs, _inputPollMs, _networkSendMs;
    private double _reloadMs, _pauseCheckMs, _recordMs, _syncSendMs;
    private int _ticksLastFrame;

    public CameraManager CameraManager => _cameraManager;
    public RollbackManager<GameInput> RollbackManager => _gameSimulation.RollbackManager;
    public Simulation.Components.SimWorld SimWorld => _gameSimulation.SimWorld;
    public Simulation.GameSimulation GameSimulation => _gameSimulation;
    public NetworkCoordinator NetworkCoordinator => _coordinator;
    public int CurrentFrame => _currentFrame;

    // Expose RenderRoot and timing data for InGameScreen profiler overlays
    public SystemRoot RenderRoot => _renderRoot;
    public double NormalUpdateMs => _normalUpdateMs;
    public double SyncChecksMs => _syncChecksMs;
    public double UpdateRootMs => _updateRootMs;
    public double RollbackCheckMs => _rollbackCheckMs;
    public double SaveSnapshotMs => _saveSnapshotMs;
    public double SimTickMs => _simTickMs;
    public double InputPollMs => _inputPollMs;
    public double NetworkSendMs => _networkSendMs;
    public double ReloadMs => _reloadMs;
    public double PauseCheckMs => _pauseCheckMs;
    public double RecordMs => _recordMs;
    public double SyncSendMs => _syncSendMs;
    public int TicksLastFrame => _ticksLastFrame;

    public Game(
        ILogger logger,
        EntityStore store,
        CameraManager cameraManager,
        UIInputManager uiInputManager,
        GameInputManager gameInputManager,
        InputStore inputStore,
        GameSimulation gameSimulation,
        PlayerConfig playerConfig,
        [Tag("Configured")] NetworkCoordinator coordinator,
        [Tag("UpdateRoot")] SystemRoot updateRoot,
        [Tag("RenderRoot")] SystemRoot renderRoot,
        ReplayManager<GameInput> replayManager,
        DerivedSystemRunner derivedRunner,
        RenderInterpolation renderInterpolation,
        AppEventBus eventBus)
    {
        _log = logger.ForContext<Game>();
        _store = store;
        _cameraManager = cameraManager;
        _uiInputManager = uiInputManager;
        _gameInputManager = gameInputManager;
        _inputStore = inputStore;
        _gameSimulation = gameSimulation;
        _coordinator = coordinator;
        _replayManager = replayManager;
        _derivedRunner = derivedRunner;
        _renderInterpolation = renderInterpolation;
        _updateRoot = updateRoot;
        _renderRoot = renderRoot;

        // Load network config
        ref readonly var networkConfig = ref gameSimulation.GameData.Db.NetworkConfigData.FindById(0);
        _inputDelayFrames = networkConfig.InputDelayFrames;

        // Compute player count - use replay's count if replaying, otherwise config
        _playerCount = replayManager.IsReplaying
            ? replayManager.PlayerCount
            : playerConfig.PlayerCount;

        // Subscribe to restart events from NetworkCoordinator
        _restartSubscription = eventBus.RestartRequested.Subscribe(_ => Restart());

        // Initialize camera to map center
        float mapCenterX = GameConfig.Map.WidthTiles * GameConfig.Map.TileSize / 2f;
        float mapCenterY = GameConfig.Map.HeightTiles * GameConfig.Map.TileSize / 2f;
        _cameraManager.SetTarget(mapCenterX, mapCenterY);

#if !PRODUCTION_BUILD
        // Enable background sync validation for multiplayer desync detection
        SetBackgroundSyncValidation(true);
#endif

        // Restore session seed from replay file (must be set before first tick)
        if (replayManager.IsReplaying)
        {
            _gameSimulation.SetSessionSeed((int)replayManager.Seed);
            _log.Information("Restored session seed from replay: {Seed}", replayManager.Seed);
        }
        else
        {
            // Start recording for initial game (not just restarts)
            // Session seed is already set via DI from SessionSeed parameter
            _replayManager.StartPendingRecording(_playerCount, _gameSimulation.SessionSeed);
        }
    }

    public void Update(float deltaTime)
    {
        // Note: InputStore.Update() is called in Main.cs before game update

        _uiInputManager.BeginFrame();

        // Buffer discrete input actions every render frame (before simulation loop)
        // This ensures clicks aren't lost when sim runs at lower rate than rendering
        _gameInputManager.BufferInput();

        long t0, t1;

        // Replay mode - use recorded inputs
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_replayManager.IsReplaying)
        {
            ReplayUpdate();
        }
        else
        {
            // Normal mode - run the game loop
            NormalUpdate(deltaTime);
        }
        t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _normalUpdateMs = (t1 - t0) * TicksToMs;

        // Process sync checks after simulation update (ensures rollbacks have been applied)
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _coordinator.ProcessPendingSyncChecks();
        t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _syncChecksMs = (t1 - t0) * TicksToMs;

        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var tick = new UpdateTick(deltaTime, 0);
        _updateRoot.Update(tick);
        t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _updateRootMs = (t1 - t0) * TicksToMs;
    }

    private void NormalUpdate(float deltaTime)
    {
        var rollbackManager = _gameSimulation.RollbackManager;

        // Freeze game on desync
        if (rollbackManager.DesyncDetected)
        {
            return;
        }

        // Clamp to max reasonable deltaTime to prevent spiral of death
        const float MaxDeltaTime = 0.1f;
        if (deltaTime > MaxDeltaTime)
        {
            deltaTime = MaxDeltaTime;
        }

        _timeAccumulator += deltaTime;

        // Cap accumulator to prevent runaway catch-up after long stalls
        float maxAccumulator = FixedDeltaTime * MaxTicksPerFrame;
        if (_timeAccumulator > maxAccumulator)
        {
            _timeAccumulator = maxAccumulator;
        }

        int ticksThisFrame = 0;
        long t0, t1;
        double rollbackSum = 0, snapshotSum = 0, tickSum = 0, inputSum = 0, networkSum = 0;
        double reloadSum = 0, pauseCheckSum = 0, recordSum = 0, syncSendSum = 0;

        while (_timeAccumulator >= FixedDeltaTime && ticksThisFrame < MaxTicksPerFrame)
        {
            // 0. Dev-only: Apply scheduled game data reload at synchronized frame
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _coordinator.TryApplyScheduledGameDataReload(_currentFrame);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            reloadSum += (t1 - t0) * TicksToMs;

            // 1. Check for rollback conflicts
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int conflictFrame = rollbackManager.ProcessPendingInputs(_currentFrame);
            if (conflictFrame >= 0)
            {
                DoRollback(conflictFrame);
            }
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            rollbackSum += (t1 - t0) * TicksToMs;

            // 2. Network pause check - don't run too far ahead of confirmed inputs
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool shouldPause = _coordinator.ShouldPauseForInputSync(_currentFrame, rollbackManager);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            pauseCheckSum += (t1 - t0) * TicksToMs;
            if (shouldPause)
            {
                break;
            }

            _timeAccumulator -= FixedDeltaTime;
            ticksThisFrame++;

            // 3. Get and store local input (delayed by InputDelayFrames)
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var gameInput = _gameInputManager.PollGameInput();
            int targetFrame = _currentFrame + _inputDelayFrames;
            rollbackManager.StoreLocalInput(targetFrame, gameInput);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            inputSum += (t1 - t0) * TicksToMs;

            // 4. Send input to peers (using target frame with delay)
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _coordinator.SendInput(targetFrame, in gameInput, rollbackManager.InputBuffer);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            networkSum += (t1 - t0) * TicksToMs;

            // 5. Predict missing inputs and save snapshot
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            rollbackManager.PredictMissingInputs(_currentFrame);
            rollbackManager.SaveSnapshot(_currentFrame);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            snapshotSum += (t1 - t0) * TicksToMs;

            // 6. Tick simulation (inputs already stored via StoreLocalInput + PredictMissingInputs)
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _gameSimulation.SetFrame(_currentFrame);
            _gameSimulation.Tick();
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            tickSum += (t1 - t0) * TicksToMs;

            // 7. Record inputs after tick (if recording)
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _replayManager.RecordFrame(_currentFrame, rollbackManager);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            recordSum += (t1 - t0) * TicksToMs;

#if !PRODUCTION_BUILD
            // 8. Post-tick: send sync check (hash computation moved to BackgroundSyncValidator)
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_coordinator.IsNetworked)
            {
                _coordinator.TrySendSyncCheck(_currentFrame, rollbackManager);
            }
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            syncSendSum += (t1 - t0) * TicksToMs;
#endif

            _currentFrame++;
        }

        // Store timing for display
        _ticksLastFrame = ticksThisFrame;
        _rollbackCheckMs = rollbackSum;
        _saveSnapshotMs = snapshotSum;
        _simTickMs = tickSum;
        _inputPollMs = inputSum;
        _networkSendMs = networkSum;
        _reloadMs = reloadSum;
        _pauseCheckMs = pauseCheckSum;
        _recordMs = recordSum;
        _syncSendMs = syncSendSum;

        // Compute interpolation alpha for render extrapolation
        // This is the fraction of time into the next (unprocessed) tick
        _renderInterpolation.Alpha = _timeAccumulator / FixedDeltaTime;
    }

    private void ReplayUpdate()
    {
        if (_replayManager.ReplayEnded)
        {
            return;
        }

        var rollbackManager = _gameSimulation.RollbackManager;

        // Get inputs from replay file
        Span<GameInput> replayInputs = stackalloc GameInput[_playerCount];
        if (!_replayManager.TryGetReplayInputs(_currentFrame, replayInputs))
        {
            if (_currentFrame > 0)
            {
                _log.Information("Replay ended at frame {Frame}", _currentFrame);
            }
            return;
        }

        // Log first frame
        if (_currentFrame == 0)
        {
            _log.Information("Starting replay with {PlayerCount} players", _playerCount);
        }

        // Store replay inputs into buffer
        for (int i = 0; i < _playerCount; i++)
        {
            rollbackManager.InputBuffer.StoreInput(_currentFrame, i, replayInputs[i]);
        }

        // Tick simulation
        _gameSimulation.SetFrame(_currentFrame);
        _gameSimulation.Tick();

        // Log hash periodically (for replay debugging)
        if (_currentFrame % 60 == 0)
        {
            ulong hash = rollbackManager.GetStateHash();
            _log.Debug("Frame {Frame}: hash={Hash:X16}", _currentFrame, hash);
        }

        _currentFrame++;
    }

    private void DoRollback(int targetFrame)
    {
        if (targetFrame < 0 || targetFrame >= _currentFrame)
        {
            return;
        }

        var rollbackManager = _gameSimulation.RollbackManager;
        rollbackManager.RestoreSnapshot(targetFrame, _currentFrame);
        _derivedRunner.InvalidateAll();

        // Derived systems (grids, zone graph, flow caches) will be rebuilt in BeginFrame

        int savedFrame = _currentFrame;
        _currentFrame = targetFrame;

        for (int resimFrame = targetFrame; resimFrame < savedFrame; resimFrame++)
        {
            rollbackManager.SaveSnapshot(_currentFrame);

            // Tick (inputs already in buffer from previous run or network)
            _gameSimulation.SetFrame(_currentFrame);
            _gameSimulation.Tick();

            _currentFrame++;
        }
    }

    public void Draw(float deltaTime)
    {
        var tick = new UpdateTick(deltaTime, 0);
        _renderRoot.Update(tick);
    }

#if !PRODUCTION_BUILD
    /// <summary>
    /// Enable or disable background sync validation for standalone desync detection.
    /// When enabled, confirmed frame snapshots are sent to a background thread for
    /// hash computation and comparison with remote hashes.
    /// This is completely isolated from game code and can be toggled at runtime.
    /// </summary>
    public void SetBackgroundSyncValidation(bool enabled)
    {
        if (enabled && _syncValidator == null)
        {
            _syncValidator = new BackgroundSyncValidator(_log);
            _syncValidator.Enabled = true;
            _coordinator.SetSyncValidator(_syncValidator);
            _log.Information("Background sync validation enabled");
        }
        else if (!enabled && _syncValidator != null)
        {
            _coordinator.SetSyncValidator(null);
            _syncValidator.Dispose();
            _syncValidator = null;
            _log.Information("Background sync validation disabled");
        }
        else if (_syncValidator != null)
        {
            _syncValidator.Enabled = enabled;
        }
    }
#endif

    /// <summary>
    /// Restart the game - resets simulation state and frame counter.
    /// Call this when all players are ready to restart after game over.
    /// </summary>
    public void Restart()
    {
        // Delete all Friflo ECS entities linked to SimWorld
        // Must be done before resetting SimWorld to avoid orphaned entities
        var simSlotQuery = _store.Query<SimSlotRef>();
        var commandBuffer = _store.GetCommandBuffer();
        foreach (var entity in simSlotQuery.Entities)
        {
            commandBuffer.DeleteEntity(entity.Id);
        }
        commandBuffer.Playback();

        // Reset simulation state (clears all SimWorld tables)
        _gameSimulation.Reset();

        // Clear rollback buffers
        _gameSimulation.RollbackManager.Clear();

        // Restart recording with new file (handles stop + start with new timestamp)
        _replayManager.RestartRecording(_playerCount, _gameSimulation.SessionSeed);

        // Reset timing
        _currentFrame = 0;
        _timeAccumulator = 0f;
    }

    /// <summary>
    /// Check if the game is currently in GameOver state.
    /// </summary>
    public bool IsGameOver
    {
        get
        {
            var gameRulesState = _gameSimulation.SimWorld.GameRulesStateRows;
            if (!gameRulesState.TryGetRow(0, out var rules)) return false;
            return rules.MatchState == MatchState.GameOver;
        }
    }

    /// <summary>
    /// Gets the match outcome (Victory, Defeat, or None if still playing).
    /// </summary>
    public MatchOutcome Outcome
    {
        get
        {
            var gameRulesState = _gameSimulation.SimWorld.GameRulesStateRows;
            if (!gameRulesState.TryGetRow(0, out var rules)) return MatchOutcome.None;
            return rules.MatchOutcome;
        }
    }

    /// <summary>
    /// Gets the frame when the match started (for survival time calculation).
    /// </summary>
    public int MatchStartFrame
    {
        get
        {
            var gameRulesState = _gameSimulation.SimWorld.GameRulesStateRows;
            if (!gameRulesState.TryGetRow(0, out var rules)) return 0;
            return rules.FrameMatchStarted;
        }
    }

    /// <summary>
    /// Gets the match statistics for end-game display.
    /// </summary>
    public (int unitsKilled, int buildingsConstructed, int resourcesGathered) GetMatchStats()
    {
        var stats = _gameSimulation.SimWorld.MatchStatsRows;
        if (!stats.TryGetRow(0, out var row))
            return (0, 0, 0);
        return (row.UnitsKilled, row.BuildingsConstructed, row.ResourcesGathered);
    }

    public void Dispose()
    {
        _restartSubscription.Dispose();
        _replayManager.Dispose();
        _gameSimulation.Dispose();
#if !PRODUCTION_BUILD
        _syncValidator?.Dispose();
#endif
    }
}
