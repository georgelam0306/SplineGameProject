using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Static data for player-controlled unit types.
/// Maps 1:1 with UnitTypeId enum values.
/// </summary>
[GameDocTable("UnitTypes")]
[StructLayout(LayoutKind.Sequential)]
public struct UnitTypeData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Name for display and debugging (e.g., "Soldier", "Ranger").</summary>
    public GameDataId Name;

    /// <summary>Base health points.</summary>
    public int Health;

    /// <summary>Base damage per attack.</summary>
    public int Damage;

    /// <summary>Attack range in tiles.</summary>
    public Fixed64 Range;

    /// <summary>Movement speed in tiles per second.</summary>
    public Fixed64 MoveSpeed;

    /// <summary>Damage reduction from attacks.</summary>
    public int Armor;

    /// <summary>Seconds between attacks.</summary>
    public Fixed32 AttackCooldown;

    /// <summary>Visual scale multiplier for rendering.</summary>
    public Fixed64 Scale;

    /// <summary>Threat level emitted to threat grid (attracts zombies).</summary>
    public int ThreatLevel;

    /// <summary>Noise level emitted per frame (contributes to noise grid).</summary>
    public int NoiseLevel;

    /// <summary>Whether this unit type can garrison inside buildings.</summary>
    public bool CanGarrison;

    /// <summary>Vision radius in tiles for fog of war.</summary>
    public int SightRange;

    // === Training Configuration ===

    /// <summary>Gold cost to train this unit.</summary>
    public int CostGold;

    /// <summary>Wood cost to train this unit.</summary>
    public int CostWood;

    /// <summary>Stone cost to train this unit.</summary>
    public int CostStone;

    /// <summary>Iron cost to train this unit.</summary>
    public int CostIron;

    /// <summary>Oil cost to train this unit.</summary>
    public int CostOil;

    /// <summary>Training time in frames (60 = 1 second).</summary>
    public int BuildTime;

    /// <summary>Tech ID required to train (0 = always available, 1-63 = requires that tech bit).</summary>
    public int RequiredTechId;

    /// <summary>Building type ID that trains this unit (-1 = not trainable, 0+ = BuildingTypeId).</summary>
    public int TrainedAtBuildingType;

    /// <summary>Population cost for this unit (consumed when trained).</summary>
    public int PopulationCost;

    /// <summary>Selection priority for drag-select (higher = selected first when mixed with other entity types).</summary>
    public int SelectionPriority;

    // === Display/Rendering ===
    /// <summary>Sprite filename for rendering (e.g., "ranger.png"). Empty = use fallback color.</summary>
    public StringHandle SpriteFile;

    /// <summary>3-letter abbreviation for UI display (e.g., "RNG", "SOL").</summary>
    public StringHandle Abbreviation;

    /// <summary>Fallback color for rendering when sprite is not available.</summary>
    public Color32 Color;
}
