using Core;
using FlowField;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Provides world data for the flow field system.
/// Checks terrain passability and building occupancy to determine blocked tiles.
/// </summary>
public sealed class TileWorldProvider : IWorldProvider
{
    private readonly TerrainDataService _terrainData;

    public int TileSize => 32; // RTS spec: 32px per tile

    public TileWorldProvider(TerrainDataService terrainData)
    {
        _terrainData = terrainData;
    }

    public bool IsBlocked(Fixed64 worldX, Fixed64 worldY)
    {
        // Convert world coords to tile coords
        int tileX = (worldX / Fixed64.FromInt(TileSize)).ToInt();
        int tileY = (worldY / Fixed64.FromInt(TileSize)).ToInt();

        // Check terrain passability (water, mountains are impassable) - O(1)
        if (_terrainData.IsGenerated && !_terrainData.IsPassable(tileX, tileY))
        {
            return true;
        }

        // Check building occupancy - O(1) lookup via BuildingOccupancyGrid
        return _terrainData.IsTileBlockedByBuilding(tileX, tileY);
    }

    public bool IsBlockedByTerrain(Fixed64 worldX, Fixed64 worldY)
    {
        // Convert world coords to tile coords
        int tileX = (worldX / Fixed64.FromInt(TileSize)).ToInt();
        int tileY = (worldY / Fixed64.FromInt(TileSize)).ToInt();

        // Only check terrain passability - ignore buildings
        return _terrainData.IsGenerated && !_terrainData.IsPassable(tileX, tileY);
    }
}
