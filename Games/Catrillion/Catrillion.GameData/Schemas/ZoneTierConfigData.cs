using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Configuration for map zones/tiers.
/// Each tier defines a concentric ring from the map center with different
/// zombie types and resource density.
/// </summary>
[GameDocTable("ZoneTierConfig")]
[StructLayout(LayoutKind.Sequential)]
public struct ZoneTierConfigData
{
    [PrimaryKey]
    public int Id;  // Tier number: 0=safe zone, 1=inner, 2=mid, 3=outer

    /// <summary>Name for debugging (e.g., "SafeZone", "Tier1_Inner").</summary>
    public GameDataId Name;

    // === Zone Bounds ===
    /// <summary>Inner radius as ratio of map half-size (0.0 = center).</summary>
    public Fixed64 InnerRadiusRatio;

    /// <summary>Outer radius as ratio of map half-size (1.0 = edge).</summary>
    public Fixed64 OuterRadiusRatio;

    // === Zombie Distribution ===
    /// <summary>Multiplier for zombie density (0=none, 1=base, 2=double).</summary>
    public Fixed64 ZombieDensityMultiplier;

    /// <summary>
    /// Bitmask for allowed zombie types.
    /// Bit 0=Walker, 1=Runner, 2=Fatty, 3=Spitter, 4=Doom.
    /// E.g., 31 = all types, 1 = Walker only.
    /// </summary>
    public int AllowedZombieTypesMask;

    // === Resource Distribution ===
    /// <summary>Multiplier for resource node density (0=none, 1=base, 2=double).</summary>
    public Fixed64 ResourceDensityMultiplier;
}
