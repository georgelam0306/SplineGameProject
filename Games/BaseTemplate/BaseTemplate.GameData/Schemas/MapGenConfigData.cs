using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace BaseTemplate.GameData.Schemas;

/// <summary>
/// Configuration for procedural map generation.
/// Singleton table (Id=0).
/// </summary>
[GameDocTable("MapGenConfig")]
[StructLayout(LayoutKind.Sequential)]
public struct MapGenConfigData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Name for debugging.</summary>
    public GameDataId Name;

    // === Safe Zone ===
    /// <summary>Radius in tiles from center with no zombies or obstacles.</summary>
    public int SafeZoneRadiusTiles;

    /// <summary>Extended radius with guaranteed grass terrain for base building.</summary>
    public int BuildableZoneRadiusTiles;

    /// <summary>Width in tiles of carved paths from cardinal edges to center.</summary>
    public int CardinalPathWidth;

    // === Terrain Noise Settings ===
    /// <summary>Scale for primary terrain noise (lower = larger features).</summary>
    public Fixed64 TerrainNoiseScale;

    /// <summary>Number of octaves for fractal noise.</summary>
    public int TerrainNoiseOctaves;

    /// <summary>Amplitude decay per octave (typically 0.5).</summary>
    public Fixed64 TerrainNoisePersistence;

    // === Biome Thresholds (noise value 0-1) ===
    /// <summary>Noise below this = water.</summary>
    public Fixed64 WaterThreshold;

    /// <summary>Noise below this = grass (above water).</summary>
    public Fixed64 GrassThreshold;

    /// <summary>Noise below this = forest/dirt (above grass).</summary>
    public Fixed64 ForestThreshold;

    // Above ForestThreshold = mountain

    // === Resource Settings ===
    /// <summary>Number of gold deposits to spawn per tier zone.</summary>
    public int GoldDepositsPerZone;

    /// <summary>Number of energy deposits to spawn per tier zone.</summary>
    public int EnergyDepositsPerZone;

    /// <summary>Number of wood/forest nodes to spawn per tier zone (for sawmills).</summary>
    public int WoodNodesPerZone;

    /// <summary>Number of stone deposits to spawn per tier zone (for quarries).</summary>
    public int StoneDepositsPerZone;

    /// <summary>Number of iron deposits to spawn per tier zone (for quarries).</summary>
    public int IronDepositsPerZone;

    /// <summary>Number of oil deposits to spawn per tier zone (for refineries).</summary>
    public int OilDepositsPerZone;

    /// <summary>Minimum resource amount per node.</summary>
    public int ResourceAmountMin;

    /// <summary>Maximum resource amount per node.</summary>
    public int ResourceAmountMax;

    // === Starter Resources (guaranteed near base) ===
    /// <summary>Number of starter stone nodes to spawn very close to command center.</summary>
    public int StarterStoneNodes;

    /// <summary>Number of starter iron nodes to spawn near command center (optional).</summary>
    public int StarterIronNodes;

    // === Zombie Settings ===
    /// <summary>Total number of zombies to spawn on the map.</summary>
    public int TotalZombieCount;

    /// <summary>Maximum attempts to find valid spawn positions.</summary>
    public int ZombiePlacementAttempts;
}
