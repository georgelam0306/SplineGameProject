using Catrillion.Simulation.Components;
using SimTable;

namespace Catrillion.Simulation.DerivedSystems;

/// <summary>
/// Derived state for building tile occupancy. NOT serialized - rebuilt from BuildingRows on rollback.
/// Stores packed bits indicating which map tiles are blocked by buildings.
/// For a 256x256 map = 65536 tiles = 1024 ulongs (8KB).
/// </summary>
public sealed class BuildingOccupancyGrid : IDerivedSimSystem
{
    private const int MapWidth = 256;
    private const int MapHeight = 256;
    private const int TotalBits = MapWidth * MapHeight;
    private const int UlongCount = TotalBits / 64;

    private readonly SimWorld _world;
    private readonly ulong[] _occupancyBits;
    private bool _dirty = true;

    public BuildingOccupancyGrid(SimWorld world)
    {
        _world = world;
        _occupancyBits = new ulong[UlongCount];
    }

    /// <summary>
    /// Mark all cached state as dirty. Called after rollback/snapshot restore.
    /// </summary>
    public void Invalidate()
    {
        _dirty = true;
    }

    /// <summary>
    /// Rebuild occupancy grid from authoritative BuildingRow data if dirty.
    /// </summary>
    public void Rebuild()
    {
        if (!_dirty) return;
        _dirty = false;

        // Clear the grid
        Array.Clear(_occupancyBits, 0, UlongCount);

        // Iterate all active or under-construction buildings and mark their tiles as occupied
        var buildings = _world.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            // Include both active and under-construction buildings (they both block tiles)
            if (!building.Flags.HasFlag(BuildingFlags.IsActive) &&
                !building.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;

            MarkTiles(building.TileX, building.TileY, building.Width, building.Height, blocked: true);
        }
    }

    /// <summary>
    /// Checks if a tile is blocked by a building. O(1) lookup.
    /// </summary>
    public bool IsTileBlocked(int tileX, int tileY)
    {
        if (tileX < 0 || tileX >= MapWidth || tileY < 0 || tileY >= MapHeight)
            return false;

        int bitIndex = tileY * MapWidth + tileX;
        int ulongIndex = bitIndex / 64;
        int bitOffset = bitIndex % 64;

        return (_occupancyBits[ulongIndex] & (1UL << bitOffset)) != 0;
    }

    /// <summary>
    /// Marks tiles as blocked or unblocked. Used for incremental updates during gameplay.
    /// Note: These changes will be lost on rollback since the grid is rebuilt from BuildingRows.
    /// </summary>
    public void MarkTiles(int tileX, int tileY, int width, int height, bool blocked)
    {
        for (int ty = tileY; ty < tileY + height; ty++)
        {
            for (int tx = tileX; tx < tileX + width; tx++)
            {
                if (tx >= 0 && tx < MapWidth && ty >= 0 && ty < MapHeight)
                {
                    int bitIndex = ty * MapWidth + tx;
                    int ulongIndex = bitIndex / 64;
                    int bitOffset = bitIndex % 64;

                    if (blocked)
                    {
                        _occupancyBits[ulongIndex] |= (1UL << bitOffset);
                    }
                    else
                    {
                        _occupancyBits[ulongIndex] &= ~(1UL << bitOffset);
                    }
                }
            }
        }
    }
}
