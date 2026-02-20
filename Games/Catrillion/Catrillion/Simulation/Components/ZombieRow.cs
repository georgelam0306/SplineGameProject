using Catrillion.Config;
using Catrillion.GameData.Schemas;
using ConfigRefresh;
using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// AI-controlled enemy units.
/// </summary>
[SimTable(Capacity = 20_000, CellSize = GameConfig.Map.TileSize, GridSize = GameConfig.Map.WidthTiles)]
[ConfigSource(typeof(ZombieTypeData))]
public partial struct ZombieRow
{
    // Transform
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;
    public Fixed64Vec2 PreferredVelocity;   // Desired velocity before RVO avoidance
    public Fixed64Vec2 SmoothedSeparation;  // EMA-smoothed separation force
    public Fixed64 FacingAngle;
    public Fixed64 AgentRadius;             // Collision radius for RVO (default ~8px)

    // Identity
    [TypeIdField] public ZombieTypeId TypeId;

    // Stats (cached from ZombieTypeData, refreshed on hot-reload)
    public int Health;
    [CachedStat("Health")] public int MaxHealth;
    [CachedStat] public int Damage;
    [CachedStat] public Fixed64 AttackRange;
    public Fixed64 AttackSpeed;  // Computed from AttackCooldown, not directly cached
    [CachedStat] public Fixed64 MoveSpeed;
    [CachedStat] public int InfectionDamage;
    [CachedStat] public int ThreatSearchRadius;
    [CachedStat] public int NoiseSearchRadius;

    // AI timing (cached from ZombieTypeData)
    [CachedStat] public int IdleDurationMin;
    [CachedStat] public int IdleDurationMax;
    [CachedStat] public int WanderDurationMin;
    [CachedStat] public int WanderDurationMax;

    // Target acquisition (cached from ZombieTypeData, in pixels)
    [CachedStat] public int TargetAcquisitionRange;
    [CachedStat] public int TargetLossRange;

    // AI State
    public ZombieState State;           // Current behavioral state
    public int StateTimer;              // Frames remaining in current state
    public int WanderDirectionSeed;     // Deterministic seed for wander direction
    [CachedStat] public Fixed32 AttackCooldown;  // Seconds between attacks
    public SimHandle TargetHandle;   // SimHandle.Invalid = none
    public byte TargetType;          // 0=Building, 1=Unit
    public int NoiseAttraction;
    public byte AggroLevel;          // 0=calm, increases when damaged/alerted, decays over time
    public SimHandle AggroHandle;    // Entity that last attacked this zombie (prioritized for retaliation)

    // Pathfinding
    public int ZoneId;
    public Fixed64Vec2 Flow;

    // State
    public MortalFlags Flags;
    public int DeathFrame;

    // Target distribution (reset each tick by CombatUnitTargetAcquisitionSystem)
    public int IncomingDamage;  // Sum of damage from all combat units targeting this zombie
}
