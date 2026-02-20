using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Static data for building types.
/// </summary>
[GameDocTable("BuildingTypes")]
[StructLayout(LayoutKind.Sequential)]
public struct BuildingTypeData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Name for display and debugging.</summary>
    public GameDataId Name;

    /// <summary>Base health points.</summary>
    public int Health;

    // === Build Costs (multi-resource) ===
    /// <summary>Gold cost to build.</summary>
    public int CostGold;
    /// <summary>Wood cost to build.</summary>
    public int CostWood;
    /// <summary>Stone cost to build.</summary>
    public int CostStone;
    /// <summary>Iron cost to build.</summary>
    public int CostIron;
    /// <summary>Oil cost to build.</summary>
    public int CostOil;

    /// <summary>Build time in frames (60 = 1 second).</summary>
    public int BuildTime;

    // === Resource Generation (per second) ===
    /// <summary>Gold generated per second (0 = none).</summary>
    public int GeneratesGold;
    /// <summary>Wood generated per second (0 = none).</summary>
    public int GeneratesWood;
    /// <summary>Stone generated per second (0 = none).</summary>
    public int GeneratesStone;
    /// <summary>Iron generated per second (0 = none).</summary>
    public int GeneratesIron;
    /// <summary>Oil generated per second (0 = none).</summary>
    public int GeneratesOil;
    /// <summary>Food generated per second (0 = none).</summary>
    public int GeneratesFood;

    /// <summary>Production cycle duration in frames (0 = use default 300 = 5 seconds). Resources delivered in batch at cycle end.</summary>
    public int ProductionCycleDuration;

    // === Upkeep Costs (per second) ===
    /// <summary>Gold upkeep per second (0 = none).</summary>
    public int UpkeepGold;
    /// <summary>Wood upkeep per second (0 = none).</summary>
    public int UpkeepWood;
    /// <summary>Stone upkeep per second (0 = none).</summary>
    public int UpkeepStone;
    /// <summary>Iron upkeep per second (0 = none).</summary>
    public int UpkeepIron;
    /// <summary>Oil upkeep per second (0 = none).</summary>
    public int UpkeepOil;
    /// <summary>Food upkeep per second (0 = none).</summary>
    public int UpkeepFood;

    // === Storage Capacity Contribution ===
    /// <summary>Max gold storage provided by this building.</summary>
    public int ProvidesMaxGold;
    /// <summary>Max wood storage provided by this building.</summary>
    public int ProvidesMaxWood;
    /// <summary>Max stone storage provided by this building.</summary>
    public int ProvidesMaxStone;
    /// <summary>Max iron storage provided by this building.</summary>
    public int ProvidesMaxIron;
    /// <summary>Max oil storage provided by this building.</summary>
    public int ProvidesMaxOil;
    /// <summary>Max food storage provided by this building.</summary>
    public int ProvidesMaxFood;

    // === Population ===
    /// <summary>Max population capacity provided by this building (housing).</summary>
    public int ProvidesMaxPopulation;

    // === Area Effect Modifiers ===
    /// <summary>Radius for area effect in world units (0 = no area effect).</summary>
    public Fixed64 EffectRadius;
    /// <summary>Gold production bonus applied to nearby buildings (0.20 = +20%).</summary>
    public Fixed64 AreaGoldBonus;
    /// <summary>Wood production bonus applied to nearby buildings (0.20 = +20%).</summary>
    public Fixed64 AreaWoodBonus;
    /// <summary>Stone production bonus applied to nearby buildings (0.20 = +20%).</summary>
    public Fixed64 AreaStoneBonus;
    /// <summary>Iron production bonus applied to nearby buildings (0.20 = +20%).</summary>
    public Fixed64 AreaIronBonus;
    /// <summary>Oil production bonus applied to nearby buildings (0.20 = +20%).</summary>
    public Fixed64 AreaOilBonus;
    /// <summary>Food production bonus applied to nearby buildings (0.20 = +20%).</summary>
    public Fixed64 AreaFoodBonus;

    // === Environment Requirements ===
    /// <summary>Required resource node type nearby (-1 = none, -2 = any ore for quarry). Uses ResourceTypeId values.</summary>
    public int RequiredResourceNodeType;
    /// <summary>Required terrain type nearby (-1 = none). Uses TerrainType values (e.g., Water=4 for fishing).</summary>
    public int RequiredTerrainType;
    /// <summary>Radius in tiles to search for required nodes/terrain.</summary>
    public int EnvironmentQueryRadius;
    /// <summary>If true, building must be placed directly on top of a matching resource node (for oil refineries).</summary>
    public bool RequiresOnTopOfNode;
    /// <summary>Base production rate per matching node or terrain tile. Used with linear scaling formula.</summary>
    public int BaseRatePerNode;
    /// <summary>Maximum number of nodes/tiles that contribute to production (0 = no cap).</summary>
    public int MaxNodeBonus;

    /// <summary>Power consumption (negative = generates power).</summary>
    public int PowerConsumption;

    /// <summary>Whether this building requires power to function.</summary>
    public bool RequiresPower;

    /// <summary>Size in tiles (width).</summary>
    public int Width;

    /// <summary>Size in tiles (height).</summary>
    public int Height;

    /// <summary>Attack damage (for turrets).</summary>
    public int Damage;

    /// <summary>Attack range in tiles (for turrets).</summary>
    public Fixed64 Range;

    /// <summary>Seconds between attacks (for turrets).</summary>
    public Fixed32 AttackCooldown;

    /// <summary>Threat level emitted to threat grid (attracts zombies).</summary>
    public int ThreatLevel;

    /// <summary>Noise level emitted per frame (contributes to noise grid).</summary>
    public int NoiseLevel;

    /// <summary>Splash damage radius (0 = no splash, >0 = AOE radius in world units).</summary>
    public Fixed64 SplashRadius;

    /// <summary>If true, splash damage falls off linearly from center to edge. If false, full damage throughout.</summary>
    public bool SplashFalloff;

    /// <summary>If true, this building deals AOE damage in a circle around itself (no projectiles).</summary>
    public bool IsSelfCenteredAOE;

    /// <summary>If true, self-centered AOE damage falls off linearly from center to edge.</summary>
    public bool HasDamageFalloff;

    /// <summary>Maximum number of units that can garrison inside this building (0 = cannot garrison).</summary>
    public int GarrisonCapacity;

    // === UI Category & Unlock ===
    /// <summary>Category ID for build menu grouping (FK to BuildingCategoryData).</summary>
    public int CategoryId;

    /// <summary>Tech ID required to unlock (0 = always available, 1-63 = requires that tech bit).</summary>
    public int RequiredTechId;

    /// <summary>Building type this can upgrade to (-1 = cannot upgrade).</summary>
    public int UpgradesTo;

    /// <summary>If true, only one of this building type can be built per player.</summary>
    public bool IsUnique;

    /// <summary>Sort order within category (lower = first).</summary>
    public int DisplayOrder;

    // === Constraint System ===
    /// <summary>Power connection range in world units (0 = cannot relay power, >0 = can relay to buildings within this range).</summary>
    public Fixed64 PowerConnectionRadius;

    /// <summary>Constraint flags for this building type (used with RequiresNearbyBuildingFlag/ExcludesNearbyBuildingFlag).</summary>
    public BuildingConstraintFlags ConstraintFlags;

    /// <summary>Vision radius in tiles for fog of war.</summary>
    public int SightRange;

    /// <summary>Selection priority for drag-select (higher = selected first when mixed with other entity types).</summary>
    public int SelectionPriority;

    // === Display/Rendering ===
    /// <summary>Sprite filename for rendering (e.g., "command_center.png"). Empty = use fallback color.</summary>
    public StringHandle SpriteFile;

    /// <summary>3-letter abbreviation for UI display (e.g., "CMD", "BAR").</summary>
    public StringHandle Abbreviation;

    /// <summary>Fallback color for rendering when sprite is not available.</summary>
    public Color32 Color;
}
