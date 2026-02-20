using System.Diagnostics;
using Catrillion.Config;
using Catrillion.GameData.Schemas;
using ConfigRefresh;
using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

public enum OrderType : byte
{
    None,
    Move,
    AttackMove,
    Hold,
    Patrol,
    EnterGarrison,
    ExitGarrison
}

[SimTable(Capacity = 10_000, CellSize = GameConfig.Map.TileSize, GridSize = GameConfig.Map.WidthTiles)]
[ConfigSource(typeof(UnitTypeData))]
public partial struct CombatUnitRow
{
    // Transform
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;
    public Fixed64Vec2 PreferredVelocity;   // Desired velocity before RVO avoidance
    public Fixed64Vec2 SmoothedSeparation;  // EMA-smoothed separation force
    public Fixed64 AgentRadius;             // Collision radius for RVO (default ~8px)

    // Identity
    public byte OwnerPlayerId;        // 0-7 player slot
    [TypeIdField] public UnitTypeId TypeId;

    // Core stats (cached from UnitTypeData)
    [CachedStat] public Fixed64 MoveSpeed;
    public int Health;
    [CachedStat("Health")] public int MaxHealth;

    // Combat stats (cached from UnitTypeData)
    [CachedStat] public int Damage;
    [CachedStat("Range")] public Fixed64 AttackRange;
    [CachedStat("AttackCooldown")] public Fixed32 BaseAttackCooldown;  // Seconds between attacks
    [CachedStat] public int Armor;

    // Threat/noise stats (cached from UnitTypeData)
    [CachedStat] public int ThreatLevel;
    [CachedStat] public int NoiseLevel;

    // Vision stats (cached from UnitTypeData)
    [CachedStat] public int SightRange;  // Vision radius in tiles for fog of war

    // Selection priority (cached from UnitTypeData)
    [CachedStat] public int SelectionPriority;  // Higher = selected first in drag-select

    // Combat state
    public Fixed32 AttackTimer;       // Seconds until next attack (counts down from BaseAttackCooldown)
    public SimHandle TargetHandle;    // Current attack target

    // Orders
    public OrderType CurrentOrder;
    public Fixed64Vec2 OrderTarget;
    public Fixed64Vec2 PatrolStart;      // Start position for patrol (unit returns here after reaching OrderTarget)
    public SimHandle OrderTargetHandle;  // Target entity for garrison/attack orders
    public int GroupId;               // 0 = no group, >0 = move group
    public int SelectedByPlayerId;    // -1 = unselected, 0+ = selecting player

    // Garrison state
    public SimHandle GarrisonedInHandle;  // Building this unit is garrisoned in (Invalid = not garrisoned)

    // Veterancy
    public byte VeterancyLevel;       // 0-3 (Rookie, Veteran, Elite, Hero)
    public ushort KillCount;

    // State
    public MortalFlags Flags;
    public int DeathFrame;

    // Computed state (not serialized, recalculated after rollback/hot-reload)
    public int EffectiveDamage;

    // Setup function - parsed by generator at compile time to generate RecomputeAll()
    [Conditional("COMPUTED_STATE_SETUP")]
    static void Setup(IComputedStateBuilder<CombatUnitRow> b) =>
        b.Compute(r => r.EffectiveDamage, r => r.Damage + r.VeterancyLevel * 5);
}
