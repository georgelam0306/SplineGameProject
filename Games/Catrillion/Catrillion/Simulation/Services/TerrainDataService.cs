using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.DualGrid.Utilities;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.DerivedSystems;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Holds the procedurally generated terrain data for the map.
/// Generated once at game initialization and shared between systems.
/// Building occupancy is tracked via BuildingOccupancyGrid derived system.
/// </summary>
public sealed class TerrainDataService
{
    private readonly ProceduralTerrainGenerator _generator;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly BuildingOccupancyGrid _buildingOccupancy;
    private readonly int _widthTiles;
    private readonly int _heightTiles;

    private TerrainType[]? _terrain;

    public TerrainDataService(GameDataManager<GameDocDb> gameData, BuildingOccupancyGrid buildingOccupancy)
    {
        _gameData = gameData;
        _buildingOccupancy = buildingOccupancy;
        _generator = new ProceduralTerrainGenerator(gameData);

        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _widthTiles = mapConfig.WidthTiles;
        _heightTiles = mapConfig.HeightTiles;
    }

    /// <summary>
    /// Width of the terrain map in tiles.
    /// </summary>
    public int WidthTiles => _widthTiles;

    /// <summary>
    /// Height of the terrain map in tiles.
    /// </summary>
    public int HeightTiles => _heightTiles;

    /// <summary>
    /// Whether terrain has been generated.
    /// </summary>
    public bool IsGenerated => _terrain != null;

    /// <summary>
    /// Generates the terrain for the entire map.
    /// Should be called once during game initialization.
    /// </summary>
    public void Generate(int seed)
    {
        _terrain = _generator.GenerateTerrain(_widthTiles, _heightTiles, seed);
    }

    /// <summary>
    /// Gets the terrain type at the given tile coordinates.
    /// Returns Grass if terrain hasn't been generated or coordinates are out of bounds.
    /// </summary>
    public TerrainType GetTerrainAt(int tileX, int tileY)
    {
        if (_terrain == null)
            return TerrainType.Grass;

        if (tileX < 0 || tileX >= _widthTiles || tileY < 0 || tileY >= _heightTiles)
            return TerrainType.None;

        return _terrain[tileY * _widthTiles + tileX];
    }

    /// <summary>
    /// Checks if a terrain type is passable (zombies/units can walk).
    /// Uses TerrainBiomeConfigData for passability rules.
    /// </summary>
    public bool IsPassable(int tileX, int tileY)
    {
        var terrain = GetTerrainAt(tileX, tileY);
        ref readonly var biomeConfig = ref _gameData.Db.TerrainBiomeConfigData.FindById((int)terrain);
        return biomeConfig.IsPassable;
    }

    /// <summary>
    /// Counts tiles of a specific terrain type within a radius of the center tile.
    /// Uses circular distance check (not square).
    /// </summary>
    public int CountTerrainInRadius(int centerTileX, int centerTileY, int radiusTiles, TerrainType targetType)
    {
        int count = 0;
        int radiusSq = radiusTiles * radiusTiles;

        for (int dy = -radiusTiles; dy <= radiusTiles; dy++)
        {
            for (int dx = -radiusTiles; dx <= radiusTiles; dx++)
            {
                // Check if within circular radius
                if (dx * dx + dy * dy > radiusSq) continue;

                int tx = centerTileX + dx;
                int ty = centerTileY + dy;

                if (GetTerrainAt(tx, ty) == targetType)
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Checks if there is at least one tile of the specified terrain type adjacent to the center tile.
    /// Adjacent means within 1 tile (8-directional).
    /// </summary>
    public bool HasAdjacentTerrain(int centerTileX, int centerTileY, TerrainType targetType)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue; // Skip center

                if (GetTerrainAt(centerTileX + dx, centerTileY + dy) == targetType)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Marks specific tiles as blocked or unblocked by a building.
    /// Use this for incremental updates when buildings are placed/destroyed.
    /// </summary>
    public void MarkBuildingTiles(int tileX, int tileY, int width, int height, bool blocked)
    {
        _buildingOccupancy.MarkTiles(tileX, tileY, width, height, blocked);
    }

    /// <summary>
    /// Checks if a tile is blocked by anything (terrain, building, or resource node). O(1) lookup for terrain/buildings.
    /// </summary>
    public bool IsTileBlocked(int tileX, int tileY, ResourceNodeRowTable? resourceNodes = null)
    {
        // Check terrain passability
        if (!IsPassable(tileX, tileY))
            return true;

        // Check building occupancy via derived system
        if (_buildingOccupancy.IsTileBlocked(tileX, tileY))
            return true;

        // Check resource node occupancy (if provided)
        if (resourceNodes != null && IsTileBlockedByResourceNode(tileX, tileY, resourceNodes))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a tile has a resource node on it.
    /// </summary>
    public bool IsTileBlockedByResourceNode(int tileX, int tileY, ResourceNodeRowTable resourceNodes)
    {
        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            if (node.TileX == tileX && node.TileY == tileY)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a tile is blocked by a building. O(1) lookup via BuildingOccupancyGrid.
    /// </summary>
    public bool IsTileBlockedByBuilding(int tileX, int tileY)
    {
        return _buildingOccupancy.IsTileBlocked(tileX, tileY);
    }
}
