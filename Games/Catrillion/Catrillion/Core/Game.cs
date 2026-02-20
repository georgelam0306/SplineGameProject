using System;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Pure.DI;
using R3;
using Raylib_cs;
using Serilog;
using Catrillion.AppState;
using Catrillion.Camera;
using Catrillion.Config;
using Catrillion.Input;
using Catrillion.OnlineServices;
using Catrillion.Rollback;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Catrillion.Stores;
using Catrillion.Rendering;
using FlowField;
using Core;
using DerpTech.Rollback;

namespace Catrillion;

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
    private readonly TerrainDataService _terrainData;
    private readonly ZoneFlowService _flowService;
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

    // Render profiler overlay toggle (F7)
    private bool _showRenderProfiler;

    // Update timing breakdown (shown with F9)
    private bool _showUpdateTiming;
    private double _normalUpdateMs, _endFrameMs, _syncChecksMs, _updateRootMs;
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
        TerrainDataService terrainData,
        ZoneFlowService flowService,
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
        _terrainData = terrainData;
        _flowService = flowService;
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
        // Note: InputStore.Update() is called in Main.cs before screens update

        // Profiler controls: F5 = toggle overlay, F6 = toggle recording, F9 = update timing
        HandleProfilerInput();

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

        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _gameSimulation.SimWorld.EndFrame(_store);
        t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _endFrameMs = (t1 - t0) * TicksToMs;

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

        DrawPerfOverlay();
        DrawProfilerOverlay();
        DrawRenderProfilerOverlay();
        DrawUpdateTimingOverlay();
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

        // Invalidate cached flow fields from previous game
        _flowService.InvalidateAllFlows();

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

    private void DrawPerfOverlay()
    {
        // Hide when render profiler is visible (they overlap on right side)
        if (_showRenderProfiler) return;

        int screenWidth = Raylib.GetScreenWidth();
        int lineHeight = 14;
        int startY = 10;
        int currentY = startY;

        // Right-align text
        string fpsText = $"FPS: {Raylib.GetFPS()}";
        int textWidth = Raylib.MeasureText(fpsText, 12);
        Raylib.DrawText(fpsText, screenWidth - textWidth - 10, currentY, 12, Color.Green);
        currentY += lineHeight;

        string frameText = $"Sim frame: {_currentFrame}";
        textWidth = Raylib.MeasureText(frameText, 12);
        Raylib.DrawText(frameText, screenWidth - textWidth - 10, currentY, 12, Color.SkyBlue);
        currentY += lineHeight;

        string unitsText = $"Units: {_gameSimulation.SimWorld.CombatUnitRows.Count} | Zombies: {_gameSimulation.SimWorld.ZombieRows.Count}";
        textWidth = Raylib.MeasureText(unitsText, 12);
        Raylib.DrawText(unitsText, screenWidth - textWidth - 10, currentY, 12, Color.Orange);
        currentY += lineHeight;

        if (_coordinator.IsNetworked)
        {
            Span<int> pings = stackalloc int[8];
            _coordinator.GetPeerPings(pings);

            string peersText = $"Peers: {_coordinator.ConnectedPeerCount}";
            textWidth = Raylib.MeasureText(peersText, 12);
            Raylib.DrawText(peersText, screenWidth - textWidth - 10, currentY, 12, Color.Lime);
            currentY += lineHeight;

            if (_coordinator.IsSimulatingLatency)
            {
                var (minLat, maxLat) = _coordinator.SimulatedLatencyRange;
                string simText = $"Sim: {minLat}-{maxLat}ms";
                textWidth = Raylib.MeasureText(simText, 12);
                Raylib.DrawText(simText, screenWidth - textWidth - 10, currentY, 12, Color.Magenta);
                currentY += lineHeight;
            }

            for (int i = 0; i < pings.Length; i++)
            {
                if (pings[i] >= 0)
                {
                    var color = i == _coordinator.LocalSlot ? Color.Yellow : Color.White;
                    string pingText = $"P{i}: {pings[i]}ms RTT";
                    textWidth = Raylib.MeasureText(pingText, 12);
                    Raylib.DrawText(pingText, screenWidth - textWidth - 10, currentY, 12, color);
                    currentY += lineHeight;
                }
            }
        }
    }

    private void HandleProfilerInput()
    {
        var profiler = _gameSimulation.Profiler;

        // F5: Toggle profiler overlay
        if (Raylib.IsKeyPressed(KeyboardKey.F5))
        {
            profiler.ShowOverlay = !profiler.ShowOverlay;
            profiler.Enabled = profiler.ShowOverlay; // Enable timing when overlay is shown
        }

        // F6: Toggle CSV recording
        if (Raylib.IsKeyPressed(KeyboardKey.F6))
        {
            if (profiler.IsRecording)
            {
                profiler.StopRecording();
                _log.Information("Profiler recording stopped. Frames: {Frames}", profiler.RecordedFrameCount);
            }
            else
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filePath = $"Logs/profiler_{timestamp}.csv";
                profiler.StartRecording(filePath);
                _log.Information("Profiler recording started: {Path}", filePath);
            }
        }

        // F7: Toggle render profiler overlay
        if (Raylib.IsKeyPressed(KeyboardKey.F7))
        {
            _showRenderProfiler = !_showRenderProfiler;
        }

        // F9: Toggle update timing breakdown
        if (Raylib.IsKeyPressed(KeyboardKey.F9))
        {
            _showUpdateTiming = !_showUpdateTiming;
        }
    }

    private void DrawProfilerOverlay()
    {
        var profiler = _gameSimulation.Profiler;
        if (!profiler.ShowOverlay) return;

        const int LineHeight = 11;
        const int FontSize = 10;
        int startX = 10;
        int startY = 10;
        int currentY = startY;

        // Draw header
        var headerColor = new Color(200, 200, 200, 255);
        string header = $"Sim: {profiler.AverageTotalMs:F2}ms ({profiler.TotalFrameMs:F2}ms)";
        Raylib.DrawText(header, startX, currentY, FontSize + 2, headerColor);
        currentY += LineHeight + 4;

        // Draw per-system times
        var names = profiler.SystemNames;
        var avgMs = profiler.AverageMilliseconds;

        for (int i = 0; i < names.Length; i++)
        {
            double ms = avgMs[i];

            // Color based on time: green < 0.1ms, yellow < 0.5ms, orange < 1ms, red >= 1ms
            Color color;
            if (ms < 0.1) color = new Color(100, 200, 100, 255);      // Green
            else if (ms < 0.5) color = new Color(200, 200, 100, 255); // Yellow
            else if (ms < 1.0) color = new Color(255, 165, 0, 255);   // Orange
            else color = new Color(255, 80, 80, 255);                  // Red

            string text = $"{names[i]}: {ms:F3}ms";
            Raylib.DrawText(text, startX, currentY, FontSize, color);
            currentY += LineHeight;
        }

        // Show recording status if active
        if (profiler.IsRecording)
        {
            currentY += 4;
            string recText = $"REC: {profiler.RecordedFrameCount} frames";
            Raylib.DrawText(recText, startX, currentY, FontSize, new Color(255, 50, 50, 255));
        }
    }

    private void DrawRenderProfilerOverlay()
    {
        if (!_showRenderProfiler) return;

        const int LineHeight = 11;
        const int FontSize = 10;
        int screenWidth = Raylib.GetScreenWidth();
        int startY = 10;
        int currentY = startY;

        // Calculate total render time
        double totalMs = 0;
        foreach (var system in _renderRoot.ChildSystems)
        {
            totalMs += system.Perf.LastMs;
        }

        // Draw header (right-aligned)
        var headerColor = new Color(200, 200, 200, 255);
        string header = $"Render: {totalMs:F2}ms";
        int headerWidth = Raylib.MeasureText(header, FontSize + 2);
        Raylib.DrawText(header, screenWidth - headerWidth - 10, currentY, FontSize + 2, headerColor);
        currentY += LineHeight + 4;

        // Draw per-system times
        foreach (var system in _renderRoot.ChildSystems)
        {
            double ms = system.Perf.LastMs;

            // Color based on time
            Color color;
            if (ms < 0.1) color = new Color(100, 200, 100, 255);      // Green
            else if (ms < 0.5) color = new Color(200, 200, 100, 255); // Yellow
            else if (ms < 1.0) color = new Color(255, 165, 0, 255);   // Orange
            else color = new Color(255, 80, 80, 255);                  // Red

            string text = $"{system.Name}: {ms:F3}ms";
            int textWidth = Raylib.MeasureText(text, FontSize);
            Raylib.DrawText(text, screenWidth - textWidth - 10, currentY, FontSize, color);
            currentY += LineHeight;
        }
    }

    private void DrawUpdateTimingOverlay()
    {
        if (!_showUpdateTiming) return;

        const int FontSize = 12;
        int y = Raylib.GetScreenHeight() / 2 - 60;
        int x = 10;

        double total = _normalUpdateMs + _endFrameMs + _syncChecksMs + _updateRootMs;

        Raylib.DrawText("Update Breakdown (F9):", x, y, FontSize, Color.White);
        y += 16;
        Raylib.DrawText($"  NormalUpdate: {_normalUpdateMs:F2}ms ({_ticksLastFrame} ticks)", x, y, FontSize, GetTimingColor(_normalUpdateMs));
        y += 14;
        Raylib.DrawText($"    - Reload: {_reloadMs:F2}ms", x, y, FontSize, GetTimingColor(_reloadMs));
        y += 14;
        Raylib.DrawText($"    - RollbackCheck: {_rollbackCheckMs:F2}ms", x, y, FontSize, GetTimingColor(_rollbackCheckMs));
        y += 14;
        Raylib.DrawText($"    - PauseCheck: {_pauseCheckMs:F2}ms", x, y, FontSize, GetTimingColor(_pauseCheckMs));
        y += 14;
        Raylib.DrawText($"    - InputPoll: {_inputPollMs:F2}ms", x, y, FontSize, GetTimingColor(_inputPollMs));
        y += 14;
        Raylib.DrawText($"    - NetworkSend: {_networkSendMs:F2}ms", x, y, FontSize, GetTimingColor(_networkSendMs));
        y += 14;
        Raylib.DrawText($"    - SaveSnapshot: {_saveSnapshotMs:F2}ms", x, y, FontSize, GetTimingColor(_saveSnapshotMs));
        y += 14;
        Raylib.DrawText($"    - SimTick: {_simTickMs:F2}ms", x, y, FontSize, GetTimingColor(_simTickMs));
        y += 14;
        Raylib.DrawText($"    - RecordFrame: {_recordMs:F2}ms", x, y, FontSize, GetTimingColor(_recordMs));
        y += 14;
        Raylib.DrawText($"    - SyncSend: {_syncSendMs:F2}ms", x, y, FontSize, GetTimingColor(_syncSendMs));
        y += 14;
        double normalSum = _reloadMs + _rollbackCheckMs + _pauseCheckMs + _inputPollMs + _networkSendMs + _saveSnapshotMs + _simTickMs + _recordMs + _syncSendMs;
        double gap = _normalUpdateMs - normalSum;
        Raylib.DrawText($"    = Sum: {normalSum:F2}ms (gap: {gap:F2}ms)", x, y, FontSize, gap > 1.0 ? Color.Red : Color.Green);
        y += 14;
        Raylib.DrawText($"  EndFrame (ECS sync): {_endFrameMs:F2}ms", x, y, FontSize, GetTimingColor(_endFrameMs));
        y += 14;
        Raylib.DrawText($"  SyncChecks: {_syncChecksMs:F2}ms", x, y, FontSize, GetTimingColor(_syncChecksMs));
        y += 14;
        Raylib.DrawText($"  UpdateRoot (ECS): {_updateRootMs:F2}ms", x, y, FontSize, GetTimingColor(_updateRootMs));
        y += 14;
        Raylib.DrawText($"  TOTAL: {total:F2}ms", x, y, FontSize, Color.Yellow);
    }

    private static Color GetTimingColor(double ms)
    {
        if (ms < 1.0) return Color.Green;
        if (ms < 5.0) return Color.Yellow;
        if (ms < 16.0) return Color.Orange;
        return Color.Red;
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
