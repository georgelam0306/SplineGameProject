using System;
using System.Collections.Generic;
using BaseTemplate.GameApp.Core;
using BaseTemplate.GameApp.Stores;
using BaseTemplate.Presentation.Entities;
using BaseTemplate.GameData.Schemas;
using BaseTemplate.Infrastructure.Rollback;
using BaseTemplate.Simulation.Components;
using BaseTemplate.Simulation.Systems;
using BaseTemplate.Simulation.Systems.SimTable;
using DerpTech.Rollback;
using DerpTech.DesyncDetection;
using Profiling;

namespace BaseTemplate.Simulation;


public sealed class GameSimulation : IDisposable, IGameSimulation
{
    private readonly SimWorld _simWorld;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly RollbackManager<GameInput> _rollbackManager;
    private readonly EntitySpawner _entitySpawner;
    private readonly IReadOnlyList<SimTableSystem> _simTableSystems;
    private readonly DerivedSystemRunner _derivedRunner;
    private readonly ProfilingService _profilingService;

    private int _currentFrame;
    private int _sessionSeed;

    public int CurrentFrame => _currentFrame;
    public int SessionSeed => _sessionSeed;
    public SimWorld SimWorld => _simWorld;
    public RollbackManager<GameInput> RollbackManager => _rollbackManager;
    public EntitySpawner EntitySpawner => _entitySpawner;
    public GameDataManager<GameDocDb> GameData => _gameData;
    public ProfilingService Profiler => _profilingService;

    // Per-system hash debugging - data populated by background re-simulation
    // when a desync is detected.
    private readonly string[] _systemNamesForHash;
    private readonly Dictionary<int, ulong[]> _perSystemHashHistory = new();
    private const int MaxStoredPerSystemHashes = 120;

    /// <summary>
    /// System names for hash debugging (BeginFrame + all systems).
    /// </summary>
    public ReadOnlySpan<string> SystemNames => _systemNamesForHash;

    /// <summary>
    /// Get per-system hashes for a specific frame (if stored).
    /// Data is populated by background re-simulation when desync is detected.
    /// </summary>
    public bool TryGetPerSystemHashesForFrame(int frame, out ulong[]? hashes)
    {
        return _perSystemHashHistory.TryGetValue(frame, out hashes);
    }

    /// <summary>
    /// Store per-system hashes computed by background re-simulation.
    /// Called by BackgroundSyncValidator when desync is detected.
    /// </summary>
    public void StorePerSystemHashes(int frame, ulong[] hashes)
    {
        // Copy hashes to history
        var hashCopy = new ulong[hashes.Length];
        hashes.CopyTo(hashCopy, 0);
        _perSystemHashHistory[frame] = hashCopy;

        // Clean up old entries
        int oldestAllowed = frame - MaxStoredPerSystemHashes;
        if (oldestAllowed > 0 && _perSystemHashHistory.ContainsKey(oldestAllowed))
        {
            _perSystemHashHistory.Remove(oldestAllowed);
        }
    }

    public GameSimulation(
        SimWorld simWorld,
        GameDataManager<GameDocDb> gameData,
        RollbackManager<GameInput> rollbackManager,
        EntitySpawner entitySpawner,
        IReadOnlyList<SimTableSystem> simTableSystems,
        DerivedSystemRunner derivedRunner,
        ProfilingService profilingService,
        SessionSeed sessionSeed)
    {
        _simWorld = simWorld;
        _gameData = gameData;
        _rollbackManager = rollbackManager;
        _entitySpawner = entitySpawner;
        _simTableSystems = simTableSystems;
        _derivedRunner = derivedRunner;
        _profilingService = profilingService;

        _currentFrame = 0;
        _sessionSeed = sessionSeed.Value;

        // Initialize system names for hash debugging (BeginFrame + all systems)
        _systemNamesForHash = new string[simTableSystems.Count + 1];
        _systemNamesForHash[0] = "BeginFrame";
        for (int i = 0; i < simTableSystems.Count; i++)
        {
            _systemNamesForHash[i + 1] = simTableSystems[i].GetType().Name;
        }
    }

    /// <summary>
    /// Run one simulation tick. Inputs should already be stored in the input buffer
    /// by the orchestrator (via RollbackManager.StoreLocalInput and PredictMissingInputs).
    /// </summary>
    public void Tick()
    {
        SimulateTick();
    }

    /// <summary>
    /// Set the current frame number. Used by the orchestrator when managing frame counting.
    /// </summary>
    public void SetFrame(int frame)
    {
        _currentFrame = frame;
    }

    /// <summary>
    /// Get remaining countdown frames (0 = inactive/complete).
    /// </summary>
    public int GetCountdownFrames()
    {
        return _simWorld.CountdownStateRows.TryGetRow(0, out var row)
            ? row.FramesRemaining
            : 0;
    }

    private void SimulateTick()
    {
        _simWorld.BeginFrame();
        _derivedRunner.RebuildAll();

        var context = new SimulationContext(_currentFrame, _rollbackManager.PlayerCount, _sessionSeed, _rollbackManager.InputBuffer);

        for (int systemIndex = 0; systemIndex < _simTableSystems.Count; systemIndex++)
        {
            var system = _simTableSystems[systemIndex];

            // Skip systems that shouldn't tick this frame
            int interval = system.TickInterval;
            if (interval > 1 && (_currentFrame - system.TickOffset) % interval != 0)
                continue;

            // Each system has its own ProfileScope for timing
            system.Tick(in context);
        }

        // End profiler frame to update rolling averages
        _profilingService.EndFrame(_currentFrame);
    }

    /// <summary>
    /// Simulate a single tick with per-system hash tracking enabled.
    /// Used for desync debugging - computes and stores hash after each system.
    /// </summary>
    public void SimulateTickWithHashing(int frame)
    {
        _currentFrame = frame;
        _simWorld.BeginFrame();
        _derivedRunner.RebuildAll();

        var context = new SimulationContext(_currentFrame, _rollbackManager.PlayerCount, _sessionSeed, _rollbackManager.InputBuffer);

        int hashCount = _simTableSystems.Count + 1; // BeginFrame + all systems
        var hashes = new ulong[hashCount];

        // Hash after BeginFrame
        hashes[0] = _simWorld.ComputeStateHash();

        for (int systemIndex = 0; systemIndex < _simTableSystems.Count; systemIndex++)
        {
            var system = _simTableSystems[systemIndex];

            // Skip systems that shouldn't tick this frame (consistent with SimulateTick)
            int interval = system.TickInterval;
            if (interval > 1 && (frame - system.TickOffset) % interval != 0)
            {
                // Still store hash even if system didn't tick (state unchanged)
                hashes[systemIndex + 1] = hashes[systemIndex];
                continue;
            }

            system.Tick(in context);
            hashes[systemIndex + 1] = _simWorld.ComputeStateHash();
        }

        StorePerSystemHashes(frame, hashes);
    }

    /// <summary>
    /// Reset the simulation for a game restart.
    /// Clears all entities and resets state so spawning will happen on frame 0.
    /// </summary>
    public void Reset()
    {
        // Completely reset all tables to initial state
        // This resets count, freeListHead, nextStableId, and all data
        // Note: Singleton tables with AutoAllocate=true are re-allocated with defaults
        _simWorld.ResetAllTables();

        // Reset frame counter
        _currentFrame = 0;

        // Generate new session seed for local/singleplayer games
        // For multiplayer, SetSessionSeed should be called by coordinator before Reset
        if (_sessionSeed == 0)
        {
            _sessionSeed = Environment.TickCount;
        }

        // Clear per-system hash history
        _perSystemHashHistory.Clear();

        // Reset profiler timing data
        _profilingService.Reset();
    }

    /// <summary>
    /// Set the session seed for deterministic random. Must be called before Reset() for multiplayer.
    /// Coordinator generates and broadcasts this seed to all clients.
    /// </summary>
    public void SetSessionSeed(int seed)
    {
        _sessionSeed = seed;
    }

    /// <summary>
    /// Generate a new session seed. Used when restarting the game.
    /// </summary>
    public void GenerateNewSessionSeed()
    {
        _sessionSeed = Environment.TickCount ^ (_sessionSeed * 31);
    }

    /// <summary>
    /// Get per-table state hashes for desync debugging.
    /// </summary>
    public Dictionary<string, ulong> GetPerTableHashes()
    {
        return _simWorld.ComputePerTableHashes();
    }

    public void Dispose()
    {
        _profilingService.Dispose();
        _simWorld.Dispose();
    }
}
