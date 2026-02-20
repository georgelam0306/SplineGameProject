using System;
using SimTable;

namespace Catrillion.Simulation.Components;

[Flags, GenerateFlags]
public enum MortalFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsDead = 1 << 1,
    /// <summary>Wave-spawned zombie that returns to WaveChase after Attack.</summary>
    IsWaveZombie = 1 << 2,
}

[Flags, GenerateFlags]
public enum BuildingFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsDead = 1 << 1,
    RequiresPower = 1 << 2,
    IsPowered = 1 << 3,
    IsUnderConstruction = 1 << 4,
    IsRepairing = 1 << 5,
}

[Flags, GenerateFlags]
public enum ProjectileFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsHoming = 1 << 1,
    SplashFalloff = 1 << 2,  // Linear damage falloff from center to edge
}

[Flags, GenerateFlags]
public enum PlayerFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsReady = 1 << 1,
    IsAlive = 1 << 2,
}

[Flags, GenerateFlags]
public enum ResourceNodeFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsDepleted = 1 << 1,
}

[Flags, GenerateFlags]
public enum CommandQueueFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
}

[Flags, GenerateFlags]
public enum WaveStateFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    HordeActive = 1 << 1,
    MiniWaveActive = 1 << 2,
    WarningActive = 1 << 3,
    IsFinalWave = 1 << 4,
    FinalWaveActivated = 1 << 5,
}

[Flags, GenerateFlags]
public enum MapGridFlags : byte
{
    None = 0,
    FlowFieldDirty = 1 << 0,
}
