namespace Catrillion.Config;

public static class GameConfig
{
    public const int WindowWidth = 800;
    public const int WindowHeight = 600;

    public static class Map
    {
        public const int WidthTiles = 256;   // RTS spec: 256x256 tiles
        public const int HeightTiles = 256;
        public const int TileSize = 32;      // RTS spec: 32px per tile
        public const int ChunkSize = 128;   // 2×2 chunks for 256×256 map (4096px per chunk)
    }

    public static class NoiseGrid
    {
        public const int Dimensions = 32;           // 32x32 grid
        public const int CellSizePixels = 256;      // 8x8 tiles per cell
    }

    public static class ThreatGrid
    {
        public const int Dimensions = 64;           // 64x64 grid
        public const int CellSizePixels = 128;      // 4x4 tiles per cell
    }

    public static class PowerGrid
    {
        public const int Dimensions = 256;          // 256x256 grid (1:1 with tiles)
        public const int CellSizePixels = 32;       // 1 tile per cell
    }

    public static class DualGrid
    {
        public const int AtlasTileWidth = 16;
        public const int AtlasTileHeight = 16;
        public const int AtlasColumns = 5;
        public const int ChunkLoadRadius = 2;
    }

    /// <summary>
    /// Debug flags for testing and development.
    /// Set to true to enable various debug features.
    /// </summary>
    public static class Debug
    {
        /// <summary>When true, fog of war is completely disabled (everything visible).</summary>
        public static bool DisableFogOfWar = false;

        /// <summary>When true, placed map enemies are not spawned at game start.</summary>
        public static bool DisablePlacedEnemies = false;
    }
}
