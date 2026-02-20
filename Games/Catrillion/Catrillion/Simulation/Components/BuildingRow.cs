using Catrillion.GameData.Schemas;
using ConfigRefresh;
using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Static structures placed on the tile grid.
/// Has both tile coordinates (for grid operations) and world Position (for spatial queries).
/// </summary>
[SimTable(Capacity = 1000, CellSize = 64, GridSize = 128, ChunkSize = 4096)]
[ConfigSource(typeof(BuildingTypeData))]
public partial struct BuildingRow
{
    // World position (center of building, for spatial queries)
    public Fixed64Vec2 Position;

    // Tile position (for grid operations)
    public ushort TileX;
    public ushort TileY;
    public byte Width;   // Building width in tiles
    public byte Height;  // Building height in tiles

    // Identity
    [TypeIdField] public BuildingTypeId TypeId;
    public byte OwnerPlayerId;

    // Selection (for UI display)
    public int SelectedByPlayerId;  // -1 = unselected, 0+ = selecting player

    // Stats (cached from BuildingTypeData)
    public int Health;
    [CachedStat("Health")] public int MaxHealth;
    [CachedStat] public int Damage;
    [CachedStat("Range")] public Fixed64 AttackRange;
    [CachedStat] public Fixed32 AttackCooldown;  // Seconds between attacks

    // Threat/noise stats (cached from BuildingTypeData)
    [CachedStat] public int ThreatLevel;
    [CachedStat] public int NoiseLevel;

    // Vision stats (cached from BuildingTypeData)
    [CachedStat] public int SightRange;  // Vision radius in tiles for fog of war

    // Selection priority (cached from BuildingTypeData)
    [CachedStat] public int SelectionPriority;  // Higher = selected first in drag-select

    // Splash/AOE stats (cached from BuildingTypeData)
    [CachedStat] public Fixed64 SplashRadius;
    [CachedStat] public bool SplashFalloff;
    [CachedStat] public bool IsSelfCenteredAOE;
    [CachedStat] public bool HasDamageFalloff;

    // === Economy: Base stats (cached from BuildingTypeData) ===
    [CachedStat] public int GeneratesGold;
    [CachedStat] public int GeneratesWood;
    [CachedStat] public int GeneratesStone;
    [CachedStat] public int GeneratesIron;
    [CachedStat] public int GeneratesOil;
    [CachedStat] public int GeneratesFood;
    [CachedStat] public int UpkeepGold;
    [CachedStat] public int UpkeepWood;
    [CachedStat] public int UpkeepStone;
    [CachedStat] public int UpkeepIron;
    [CachedStat] public int UpkeepOil;
    [CachedStat] public int UpkeepFood;
    [CachedStat] public int ProvidesMaxGold;
    [CachedStat] public int ProvidesMaxWood;
    [CachedStat] public int ProvidesMaxStone;
    [CachedStat] public int ProvidesMaxIron;
    [CachedStat] public int ProvidesMaxOil;
    [CachedStat] public int ProvidesMaxFood;
    [CachedStat] public int ProvidesMaxPopulation;
    [CachedStat] public Fixed64 EffectRadius;
    [CachedStat] public Fixed64 AreaGoldBonus;
    [CachedStat] public Fixed64 AreaWoodBonus;
    [CachedStat] public Fixed64 AreaStoneBonus;
    [CachedStat] public Fixed64 AreaIronBonus;
    [CachedStat] public Fixed64 AreaOilBonus;
    [CachedStat] public Fixed64 AreaFoodBonus;
    [CachedStat] public int ProductionCycleDuration;

    // === Environment Requirements (cached from BuildingTypeData) ===
    [CachedStat] public int RequiredResourceNodeType;
    [CachedStat] public int RequiredTerrainType;
    [CachedStat] public int EnvironmentQueryRadius;
    [CachedStat] public bool RequiresOnTopOfNode;
    [CachedStat] public int BaseRatePerNode;
    [CachedStat] public int MaxNodeBonus;

    // === Environment: Runtime calculated values (set at spawn, recalculated when needed) ===
    /// <summary>Number of matching nodes/tiles in radius. Used for linear rate calculation.</summary>
    public byte EnvironmentNodeCount;
    /// <summary>Detected resource type for adaptive quarry (-1 = none). Uses ResourceTypeId values.</summary>
    public sbyte DetectedResourceType;

    // === Economy: Effective stats (recalculated each tick by ModifierApplicationSystem) ===
    [ComputedState] public int EffectiveGeneratesGold;
    [ComputedState] public int EffectiveGeneratesWood;
    [ComputedState] public int EffectiveGeneratesStone;
    [ComputedState] public int EffectiveGeneratesIron;
    [ComputedState] public int EffectiveGeneratesOil;
    [ComputedState] public int EffectiveGeneratesFood;

    // Resource accumulator for sub-second generation
    public int ResourceAccumulator;

    // Combat (turrets)
    public SimHandle TargetHandle;
    public Fixed32 AttackTimer;  // Seconds until next attack

    // Garrison slots (fixed for deterministic snapshotting, max 6 units)
    // Array accessor: GarrisonSlot0..GarrisonSlot5, or GarrisonSlotArray for Span
    [Array(6)] public SimHandle GarrisonSlot;
    public byte GarrisonCount;
    [CachedStat] public int GarrisonCapacity;

    // Production queue (255 = empty slot, since 0 is valid UnitTypeId)
    // Array accessor: ProductionQueue0..4, or ProductionQueueArray for Span
    [Array(5)] public byte ProductionQueue;
    public int ProductionProgress;
    public int ProductionBuildTime;  // Cached build time for current unit
    public Fixed64Vec2 RallyPoint;

    // Research (for workshop buildings)
    /// <summary>Research item being researched (0 = none, 1-255 = ResearchItemData.Id + 1).</summary>
    public byte CurrentResearchId;
    /// <summary>Current research progress in frames.</summary>
    public int ResearchProgress;

    // Construction
    public int ConstructionProgress;
    public int ConstructionBuildTime;  // Total frames needed to complete construction

    // State
    public BuildingFlags Flags;
    public int DeathFrame;

    // Repair state (for gradual healing)
    public int RepairProgress;      // Frames since repair started
    public int RepairTargetHealth;  // Health to restore to (MaxHealth)
}
