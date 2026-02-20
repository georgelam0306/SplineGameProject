using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Static data for zombie enemy types.
/// Maps 1:1 with ZombieTypeId enum values.
/// </summary>
[GameDocTable("ZombieTypes")]
[StructLayout(LayoutKind.Sequential)]
public struct ZombieTypeData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Name for display and debugging (e.g., "Walker", "Runner").</summary>
    public GameDataId Name;

    /// <summary>Base health points.</summary>
    public int Health;

    /// <summary>Base damage per attack.</summary>
    public int Damage;

    /// <summary>Movement speed in tiles per second.</summary>
    public Fixed64 MoveSpeed;

    /// <summary>Attack range in tiles.</summary>
    public Fixed64 AttackRange;

    /// <summary>Seconds between attacks.</summary>
    public Fixed32 AttackCooldown;

    /// <summary>Infection damage applied to units on hit.</summary>
    public int InfectionDamage;

    /// <summary>Radius in threat grid cells to search for highest threat when chasing.</summary>
    public int ThreatSearchRadius;

    /// <summary>Radius in noise grid cells to detect noise sources.</summary>
    public int NoiseSearchRadius;

    // AI timing (in frames at 60fps, scaled to TickRate at runtime by ZombieSpawnerService)
    /// <summary>Minimum idle duration in frames (at 60fps, scaled at spawn).</summary>
    public int IdleDurationMin;

    /// <summary>Maximum idle duration in frames.</summary>
    public int IdleDurationMax;

    /// <summary>Minimum wander duration in frames.</summary>
    public int WanderDurationMin;

    /// <summary>Maximum wander duration in frames.</summary>
    public int WanderDurationMax;

    // Target acquisition
    /// <summary>Range in pixels to acquire a target.</summary>
    public int TargetAcquisitionRange;

    /// <summary>Range in pixels to lose a target.</summary>
    public int TargetLossRange;

    /// <summary>Visual scale multiplier for rendering.</summary>
    public Fixed64 Scale;

    /// <summary>Spawn weight for wave generation (higher = more common).</summary>
    public int SpawnWeight;

    // === Display/Rendering ===
    /// <summary>Sprite filename for rendering (e.g., "walker.png"). Empty = use fallback color.</summary>
    public StringHandle SpriteFile;

    /// <summary>Fallback color for rendering when sprite is not available.</summary>
    public Color32 Color;
}
