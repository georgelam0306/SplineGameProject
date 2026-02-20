using System;
using DieDrifterDie.Infrastructure.Rollback;
using DieDrifterDie.Simulation;
using DerpTech.Rollback;

namespace DieDrifterDie.GameApp.Core;

/// <summary>
/// Interface for game composition, allowing different bootstrappers
/// (main game vs headless) to provide their own DI implementations.
/// </summary>
public interface IGameComposition : IDisposable
{
    /// <summary>
    /// The main game simulation.
    /// </summary>
    GameSimulation GameSimulation { get; }

    /// <summary>
    /// Rollback manager for netcode.
    /// </summary>
    RollbackManager<GameInput> RollbackManager { get; }

    /// <summary>
    /// Replay manager for recording/playback.
    /// </summary>
    ReplayManager<GameInput> ReplayManager { get; }
}
