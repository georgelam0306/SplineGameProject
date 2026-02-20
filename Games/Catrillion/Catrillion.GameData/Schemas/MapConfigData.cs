using System.Runtime.InteropServices;
using Core;
using GameDocDatabase;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Configuration data for the game map and rendering settings.
/// Singleton table (single row with Id=0).
/// </summary>
[GameDocTable("MapConfig")]
[StructLayout(LayoutKind.Sequential)]
public struct MapConfigData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Configuration name for identification (e.g., "Default").</summary>
    public GameDataId Name;

    // === Map Settings ===

    /// <summary>Map width in tiles (256 for standard RTS map).</summary>
    public int WidthTiles;

    /// <summary>Map height in tiles (256 for standard RTS map).</summary>
    public int HeightTiles;

    /// <summary>Size of each tile in pixels (32px per tile).</summary>
    public int TileSize;

    /// <summary>Size of each chunk in tiles for streaming (64 tiles).</summary>
    public int ChunkSize;

    // === DualGrid Rendering Settings ===

    /// <summary>Width of each tile in the atlas texture (16px).</summary>
    public int AtlasTileWidth;

    /// <summary>Height of each tile in the atlas texture (16px).</summary>
    public int AtlasTileHeight;

    /// <summary>Number of columns in the tile atlas (5).</summary>
    public int AtlasColumns;

    /// <summary>Radius of chunks to load around camera (2).</summary>
    public int ChunkLoadRadius;

    // === UI Settings ===

    /// <summary>Height of the bottom UI bar in pixels (120px).</summary>
    public int BottomBarHeight;

    // === Threat Grid Settings ===

    /// <summary>Multiplier for noise spillover to threat grid (0.1 = 10% of noise becomes threat).</summary>
    public Fixed64 NoiseSpilloverMultiplier;

    // === Noise Attraction Settings ===

    /// <summary>Pixels of attraction radius per noise unit (0.5 = noise 1000 attracts at 500px).</summary>
    public Fixed64 NoiseAttractionRadiusPerUnit;

    /// <summary>Minimum attraction radius in pixels (128px).</summary>
    public Fixed64 NoiseAttractionMinRadius;
}
