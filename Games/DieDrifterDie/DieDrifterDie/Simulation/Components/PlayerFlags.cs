using System;

namespace DieDrifterDie.Simulation.Components;

/// <summary>
/// Flags for player state.
/// </summary>
[Flags]
public enum PlayerFlags : byte
{
    None = 0,
    Active = 1 << 0,
    Ready = 1 << 1,
}
