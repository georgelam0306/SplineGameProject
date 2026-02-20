using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Configuration for terrain biome properties.
/// Each entry maps to a TerrainType enum value.
/// </summary>
[GameDocTable("TerrainBiomeConfig")]
[StructLayout(LayoutKind.Sequential)]
public struct TerrainBiomeConfigData
{
    [PrimaryKey]
    public int Id;  // Maps to TerrainType enum value

    /// <summary>Name for debugging.</summary>
    public GameDataId Name;

    // === Passability ===
    /// <summary>Can zombies and units walk on this terrain?</summary>
    public bool IsPassable;

    /// <summary>Can buildings be placed on this terrain?</summary>
    public bool IsPlaceable;

    /// <summary>Movement speed multiplier (1.0 = normal).</summary>
    public Fixed64 MovementCostMultiplier;

    // === Visual ===
    /// <summary>Number of tile variants for visual variety.</summary>
    public int TileVariantCount;

    // === Resource Spawning ===
    /// <summary>Can gold resource nodes spawn on this terrain?</summary>
    public bool CanSpawnGold;

    /// <summary>Can energy resource nodes spawn on this terrain?</summary>
    public bool CanSpawnEnergy;

    /// <summary>Can wood/forest nodes spawn on this terrain?</summary>
    public bool CanSpawnWood;

    /// <summary>Can stone deposits spawn on this terrain?</summary>
    public bool CanSpawnStone;

    /// <summary>Can iron deposits spawn on this terrain?</summary>
    public bool CanSpawnIron;

    /// <summary>Can oil deposits spawn on this terrain?</summary>
    public bool CanSpawnOil;
}
