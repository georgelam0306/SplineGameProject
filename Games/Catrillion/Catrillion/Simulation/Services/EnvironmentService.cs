using Catrillion.GameData.Schemas;
using Catrillion.Rendering.DualGrid.Utilities;
using Catrillion.Simulation.Components;
using Core;
using SimTable;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Service for calculating environment bonuses for buildings.
/// Handles resource node queries and terrain tile queries for environment-dependent production.
/// </summary>
public sealed class EnvironmentService
{
    private readonly TerrainDataService _terrainData;

    // Special value for RequiredResourceNodeType meaning "any ore" (Stone, Iron, or Gold)
    private const int AnyOreRequirement = -2;

    public EnvironmentService(TerrainDataService terrainData)
    {
        _terrainData = terrainData;
    }

    /// <summary>
    /// Counts resource nodes of a specific type within a radius of a tile position.
    /// </summary>
    public int CountNearbyResourceNodes(
        ResourceNodeRowTable resourceNodes,
        int centerTileX,
        int centerTileY,
        int radiusTiles,
        ResourceTypeId targetType)
    {
        int count = 0;
        int radiusSq = radiusTiles * radiusTiles;

        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            if (node.TypeId != targetType) continue;

            int dx = node.TileX - centerTileX;
            int dy = node.TileY - centerTileY;

            // Check if within circular radius
            if (dx * dx + dy * dy <= radiusSq)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Counts ore nodes (Stone, Iron, or Gold) within a radius and returns the most common type.
    /// Used for adaptive quarries.
    /// </summary>
    public (int count, ResourceTypeId detectedType) CountNearbyOreNodes(
        ResourceNodeRowTable resourceNodes,
        int centerTileX,
        int centerTileY,
        int radiusTiles)
    {
        int stoneCount = 0;
        int ironCount = 0;
        int goldCount = 0;
        int radiusSq = radiusTiles * radiusTiles;

        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            int dx = node.TileX - centerTileX;
            int dy = node.TileY - centerTileY;

            // Check if within circular radius
            if (dx * dx + dy * dy > radiusSq) continue;

            switch (node.TypeId)
            {
                case ResourceTypeId.Stone:
                    stoneCount++;
                    break;
                case ResourceTypeId.Iron:
                    ironCount++;
                    break;
                case ResourceTypeId.Gold:
                    goldCount++;
                    break;
            }
        }

        int totalCount = stoneCount + ironCount + goldCount;
        if (totalCount == 0)
            return (0, ResourceTypeId.Gold); // Default to Gold if no ores found

        // Return TOTAL count of all ores, but detect the most abundant type for output
        // This way Quarry benefits from ALL nearby ore nodes
        ResourceTypeId detectedType;
        if (stoneCount >= ironCount && stoneCount >= goldCount)
            detectedType = ResourceTypeId.Stone;
        else if (ironCount >= goldCount)
            detectedType = ResourceTypeId.Iron;
        else
            detectedType = ResourceTypeId.Gold;

        return (totalCount, detectedType);
    }

    /// <summary>
    /// Calculates environment bonus for a building and applies it to the building row.
    /// Should be called when a building is spawned.
    /// </summary>
    public void CalculateEnvironmentBonus(
        BuildingRowTable buildings,
        SimHandle handle,
        ResourceNodeRowTable resourceNodes)
    {
        var building = buildings.GetRow(handle);

        int tileX = building.TileX;
        int tileY = building.TileY;
        int width = building.Width;
        int height = building.Height;

        // Default values
        building.EnvironmentNodeCount = 0;
        building.DetectedResourceType = -1;

        // Handle resource node requirements
        if (building.RequiredResourceNodeType >= 0)
        {
            var targetType = (ResourceTypeId)building.RequiredResourceNodeType;

            if (building.RequiresOnTopOfNode)
            {
                // Oil refinery: check for node at building location
                int count = CountResourceNodesOnTiles(
                    resourceNodes,
                    tileX, tileY, width, height,
                    targetType);
                building.EnvironmentNodeCount = (byte)Math.Min(count, building.MaxNodeBonus > 0 ? building.MaxNodeBonus : 255);
            }
            else
            {
                // IronMine etc: count nodes adjacent to building
                int count = CountAdjacentResourceNodes(resourceNodes, tileX, tileY, width, height, targetType);
                building.EnvironmentNodeCount = (byte)Math.Min(count, building.MaxNodeBonus > 0 ? building.MaxNodeBonus : 255);
            }
        }
        // Handle adaptive quarry (any ore) - uses adjacent nodes
        else if (building.RequiredResourceNodeType == AnyOreRequirement)
        {
            var (count, detectedType) = CountAdjacentOreNodes(
                resourceNodes,
                tileX, tileY, width, height);
            building.EnvironmentNodeCount = (byte)Math.Min(count, building.MaxNodeBonus > 0 ? building.MaxNodeBonus : 255);
            building.DetectedResourceType = (sbyte)detectedType;
        }
        // Handle terrain requirements - Sawmill uses adjacent, others use radius
        else if (building.RequiredTerrainType >= 0)
        {
            var targetTerrain = (TerrainType)building.RequiredTerrainType;

            // Sawmill: count adjacent Dirt tiles (trees)
            if (building.TypeId == BuildingTypeId.Sawmill)
            {
                int count = CountAdjacentTerrain(tileX, tileY, width, height, targetTerrain);
                building.EnvironmentNodeCount = (byte)Math.Min(count, building.MaxNodeBonus > 0 ? building.MaxNodeBonus : 255);
            }
            else
            {
                // Farm, FishingHut, HuntingCottage: use radius
                int centerTileX = tileX + width / 2;
                int centerTileY = tileY + height / 2;
                int count = _terrainData.CountTerrainInRadius(
                    centerTileX, centerTileY,
                    building.EnvironmentQueryRadius,
                    targetTerrain);
                building.EnvironmentNodeCount = (byte)Math.Min(count, building.MaxNodeBonus > 0 ? building.MaxNodeBonus : 255);
            }
        }
    }

    /// <summary>
    /// Counts resource nodes of a specific type adjacent to building boundaries.
    /// </summary>
    private int CountAdjacentResourceNodes(
        ResourceNodeRowTable resourceNodes,
        int tileX, int tileY, int width, int height,
        ResourceTypeId targetType)
    {
        int count = 0;

        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            if (node.TypeId != targetType) continue;

            if (IsAdjacentToBuilding(node.TileX, node.TileY, tileX, tileY, width, height))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Counts resource nodes directly on top of a building's footprint tiles.
    /// Used for oil refineries that must be placed on oil deposits.
    /// </summary>
    private int CountResourceNodesOnTiles(
        ResourceNodeRowTable resourceNodes,
        int tileX, int tileY,
        int width, int height,
        ResourceTypeId targetType)
    {
        int count = 0;

        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            if (node.TypeId != targetType) continue;

            // Check if node is within building footprint
            if (node.TileX >= tileX && node.TileX < tileX + width &&
                node.TileY >= tileY && node.TileY < tileY + height)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts terrain tiles adjacent to the building's boundaries.
    /// Used for Sawmill (counts Dirt tiles with trees adjacent to the building).
    /// </summary>
    public int CountAdjacentTerrain(int tileX, int tileY, int width, int height, TerrainType targetTerrain)
    {
        int count = 0;

        // Top row (y = tileY - 1)
        for (int x = tileX - 1; x <= tileX + width; x++)
        {
            if (_terrainData.GetTerrainAt(x, tileY - 1) == targetTerrain)
                count++;
        }

        // Bottom row (y = tileY + height)
        for (int x = tileX - 1; x <= tileX + width; x++)
        {
            if (_terrainData.GetTerrainAt(x, tileY + height) == targetTerrain)
                count++;
        }

        // Left column (x = tileX - 1), excluding corners already counted
        for (int y = tileY; y < tileY + height; y++)
        {
            if (_terrainData.GetTerrainAt(tileX - 1, y) == targetTerrain)
                count++;
        }

        // Right column (x = tileX + width), excluding corners already counted
        for (int y = tileY; y < tileY + height; y++)
        {
            if (_terrainData.GetTerrainAt(tileX + width, y) == targetTerrain)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Counts ore nodes (Stone, Iron, Gold) adjacent to building boundaries.
    /// Used for Quarry - counts nodes directly touching the building.
    /// </summary>
    public (int count, ResourceTypeId detectedType) CountAdjacentOreNodes(
        ResourceNodeRowTable resourceNodes,
        int tileX, int tileY, int width, int height)
    {
        int stoneCount = 0;
        int ironCount = 0;
        int goldCount = 0;

        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);

            // Check if node is adjacent to building (not inside, not far away)
            if (!IsAdjacentToBuilding(node.TileX, node.TileY, tileX, tileY, width, height))
                continue;

            switch (node.TypeId)
            {
                case ResourceTypeId.Stone:
                    stoneCount++;
                    break;
                case ResourceTypeId.Iron:
                    ironCount++;
                    break;
                case ResourceTypeId.Gold:
                    goldCount++;
                    break;
            }
        }

        int totalCount = stoneCount + ironCount + goldCount;
        if (totalCount == 0)
            return (0, ResourceTypeId.Gold);

        ResourceTypeId detectedType;
        if (stoneCount >= ironCount && stoneCount >= goldCount)
            detectedType = ResourceTypeId.Stone;
        else if (ironCount >= goldCount)
            detectedType = ResourceTypeId.Iron;
        else
            detectedType = ResourceTypeId.Gold;

        return (totalCount, detectedType);
    }

    /// <summary>
    /// Checks if a tile is adjacent to (directly touching) a building's boundaries.
    /// </summary>
    private static bool IsAdjacentToBuilding(int nodeTileX, int nodeTileY, int buildingX, int buildingY, int width, int height)
    {
        // Node must be within 1 tile of the building boundary
        // Check if it's in the ring around the building

        // Inside the building - not adjacent
        if (nodeTileX >= buildingX && nodeTileX < buildingX + width &&
            nodeTileY >= buildingY && nodeTileY < buildingY + height)
            return false;

        // Too far away - not adjacent
        if (nodeTileX < buildingX - 1 || nodeTileX > buildingX + width ||
            nodeTileY < buildingY - 1 || nodeTileY > buildingY + height)
            return false;

        // In the adjacent ring
        return true;
    }

    /// <summary>
    /// Validates that a building can be placed based on its environment requirements.
    /// Returns true if placement is valid.
    /// </summary>
    public bool ValidatePlacement(
        int tileX, int tileY,
        int width, int height,
        BuildingTypeId typeId,
        in BuildingTypeData buildingType,
        ResourceNodeRowTable resourceNodes)
    {
        int centerTileX = tileX + width / 2;
        int centerTileY = tileY + height / 2;
        int queryRadius = buildingType.EnvironmentQueryRadius;

        // Handle resource node requirements
        if (buildingType.RequiredResourceNodeType >= 0)
        {
            var targetType = (ResourceTypeId)buildingType.RequiredResourceNodeType;

            if (buildingType.RequiresOnTopOfNode)
            {
                // Oil refinery: must have at least one node on building footprint
                int count = CountResourceNodesOnTiles(
                    resourceNodes,
                    tileX, tileY, width, height,
                    targetType);
                return count > 0;
            }
            else
            {
                // IronMine etc: must have at least one adjacent node
                int count = CountAdjacentResourceNodes(
                    resourceNodes,
                    tileX, tileY, width, height,
                    targetType);
                return count > 0;
            }
        }
        // Handle adaptive quarry (any ore) - uses adjacent nodes
        else if (buildingType.RequiredResourceNodeType == AnyOreRequirement)
        {
            var (count, _) = CountAdjacentOreNodes(
                resourceNodes,
                tileX, tileY, width, height);
            return count > 0;
        }
        // Handle terrain requirements
        else if (buildingType.RequiredTerrainType >= 0)
        {
            var targetTerrain = (TerrainType)buildingType.RequiredTerrainType;

            // Sawmill: uses adjacent terrain tiles
            if (typeId == BuildingTypeId.Sawmill)
            {
                int count = CountAdjacentTerrain(tileX, tileY, width, height, targetTerrain);
                return count > 0;
            }
            else
            {
                // Farm, FishingHut, HuntingCottage: use radius
                int count = _terrainData.CountTerrainInRadius(
                    centerTileX, centerTileY,
                    queryRadius,
                    targetTerrain);
                return count > 0;
            }
        }

        // No environment requirements
        return true;
    }
}
