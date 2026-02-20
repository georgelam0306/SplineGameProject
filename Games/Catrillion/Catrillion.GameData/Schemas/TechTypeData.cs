using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Static data for technology types.
/// Each tech uses its Id as the bit index in PlayerStateRow.UnlockedTech (0-63).
/// </summary>
[GameDocTable("TechTypes")]
[StructLayout(LayoutKind.Sequential)]
public struct TechTypeData
{
    [PrimaryKey]
    public int Id;  // Also the bit index in UnlockedTech (0-63)

    /// <summary>Name for display and debugging.</summary>
    public GameDataId Name;

    // === Resource Generation Multipliers (0 = no effect, 0.25 = +25%) ===
    /// <summary>Gold generation multiplier bonus.</summary>
    public Fixed64 GoldGenMult;

    /// <summary>Wood generation multiplier bonus.</summary>
    public Fixed64 WoodGenMult;

    /// <summary>Stone generation multiplier bonus.</summary>
    public Fixed64 StoneGenMult;

    /// <summary>Iron generation multiplier bonus.</summary>
    public Fixed64 IronGenMult;

    /// <summary>Oil generation multiplier bonus.</summary>
    public Fixed64 OilGenMult;

    /// <summary>Food generation multiplier bonus.</summary>
    public Fixed64 FoodGenMult;
}
