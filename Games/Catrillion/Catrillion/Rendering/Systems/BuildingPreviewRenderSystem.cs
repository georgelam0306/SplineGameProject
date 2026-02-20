using System.Collections.Generic;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.DualGrid.Utilities;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Core;
using GameDocDatabase;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders a ghost/preview building at the cursor when in build mode.
/// Shows green for valid placement, red for invalid.
/// Shows environment query radius and projected production for environment-dependent buildings.
/// Also renders constraint visuals (power radius, connection lines).
/// </summary>
public sealed class BuildingPreviewRenderSystem : BaseSystem
{
    private readonly GameplayStore _gameplayStore;
    private readonly SimWorld _simWorld;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly TerrainDataService _terrainData;
    private readonly BuildingConstraintValidator _constraintValidator;
    private readonly Dictionary<BuildingTypeId, Texture2D> _textures = new();
    private readonly int _tileSize;

    // Special value for RequiredResourceNodeType meaning "any ore" (Stone, Iron, or Gold)
    private const int AnyOreRequirement = -2;

    // Last validation result for tooltip access
    private ConstraintValidationResult _lastValidationResult;
    private bool _lastBoundsValid;
    private bool _lastTerrainValid;
    private bool _lastOverlapValid;

    public BuildingPreviewRenderSystem(
        GameplayStore gameplayStore,
        SimWorld simWorld,
        GameDataManager<GameDocDb> gameData,
        TerrainDataService terrainData,
        BuildingConstraintValidator constraintValidator)
    {
        _gameplayStore = gameplayStore;
        _simWorld = simWorld;
        _gameData = gameData;
        _terrainData = terrainData;
        _constraintValidator = constraintValidator;

        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    /// <summary>
    /// Gets the last constraint validation result for tooltip display.
    /// </summary>
    public ConstraintValidationResult LastValidationResult => _lastValidationResult;
    public bool LastBoundsValid => _lastBoundsValid;
    public bool LastOverlapValid => _lastOverlapValid;

    protected override void OnUpdateGroup()
    {
        // Only render when in build mode with a preview position
        if (!_gameplayStore.IsInBuildMode.CurrentValue) return;

        var previewPos = _gameplayStore.PlacementPreview.CurrentValue;
        if (!previewPos.HasValue) return;

        var buildingType = _gameplayStore.BuildModeType.CurrentValue;
        if (!buildingType.HasValue) return;

        int tileX = previewPos.Value.tileX;
        int tileY = previewPos.Value.tileY;

        // Get building type data for dimensions
        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)buildingType.Value);
        int width = typeData.Width;
        int height = typeData.Height;

        // Convert tile to world position
        float worldX = tileX * _tileSize;
        float worldY = tileY * _tileSize;
        float pixelWidth = width * _tileSize;
        float pixelHeight = height * _tileSize;

        // Calculate center of building for radius drawing
        float centerX = worldX + pixelWidth / 2;
        float centerY = worldY + pixelHeight / 2;
        int centerTileX = tileX + width / 2;
        int centerTileY = tileY + height / 2;

        // Check if placement is valid
        bool isValid = IsValidPlacement(buildingType.Value, tileX, tileY, width, height);

        // Check environment requirements and calculate projected production
        int nodeCount = 0;
        int projectedProduction = 0;
        string resourceName = "";
        bool hasEnvironmentRequirement = typeData.EnvironmentQueryRadius > 0 &&
            (typeData.RequiredResourceNodeType >= 0 ||
             typeData.RequiredResourceNodeType == AnyOreRequirement ||
             typeData.RequiredTerrainType >= 0);

        if (hasEnvironmentRequirement)
        {
            (nodeCount, projectedProduction, resourceName) = CalculateEnvironmentProduction(
                buildingType.Value, in typeData, centerTileX, centerTileY, tileX, tileY, width, height);

            // Environment requirement not met = invalid placement
            if (nodeCount == 0 && !typeData.RequiresOnTopOfNode)
            {
                isValid = false;
            }
            else if (typeData.RequiresOnTopOfNode && nodeCount == 0)
            {
                isValid = false;
            }
        }

        // Semi-transparent tint based on validity
        Color tint = isValid
            ? new Color(100, 255, 100, 150)  // Green semi-transparent
            : new Color(255, 100, 100, 150); // Red semi-transparent

        // Draw environment query area (adjacent tiles for Sawmill/Quarry, radius for others)
        if (hasEnvironmentRequirement)
        {
            Color areaColor = nodeCount > 0
                ? new Color(100, 200, 255, 80)   // Blue semi-transparent when valid
                : new Color(255, 100, 100, 80);  // Red semi-transparent when no nodes
            Color borderColor = new Color(100, 200, 255, 200);

            // Sawmill and Quarry use adjacent tiles, others use radius
            bool usesAdjacentTiles = buildingType.Value == BuildingTypeId.Sawmill ||
                                     buildingType.Value == BuildingTypeId.Quarry ||
                                     (typeData.RequiredResourceNodeType >= 0 && !typeData.RequiresOnTopOfNode);

            if (usesAdjacentTiles)
            {
                // Draw adjacent tile ring around building
                DrawAdjacentTileRing(tileX, tileY, width, height, areaColor, borderColor);
            }
            else if (typeData.EnvironmentQueryRadius > 0)
            {
                // Draw radius circle for Farm, FishingHut, HuntingCottage
                float radiusPixels = typeData.EnvironmentQueryRadius * _tileSize;
                Raylib.DrawCircle((int)centerX, (int)centerY, radiusPixels, areaColor);
                Raylib.DrawCircleLines((int)centerX, (int)centerY, radiusPixels, borderColor);
            }
        }

        // Get or load texture
        var texture = GetTexture(buildingType.Value);

        if (texture.Id != 0)
        {
            // Draw sprite with tint
            var destRect = new Rectangle(worldX, worldY, pixelWidth, pixelHeight);
            Raylib.DrawTexturePro(
                texture,
                new Rectangle(0, 0, texture.Width, texture.Height),
                destRect,
                System.Numerics.Vector2.Zero,
                0f,
                tint
            );
        }
        else
        {
            // Fallback: colored rectangle
            Raylib.DrawRectangle((int)worldX, (int)worldY, (int)pixelWidth, (int)pixelHeight, tint);
        }

        // Draw outline
        Color outlineColor = isValid ? Color.Green : Color.Red;
        Raylib.DrawRectangleLines((int)worldX, (int)worldY, (int)pixelWidth, (int)pixelHeight, outlineColor);

        // Draw failure message when placement is invalid
        if (!isValid)
        {
            string failureMessage = GetFailureMessage();
            int fontSize = 14;
            int textWidth = Raylib.MeasureText(failureMessage, fontSize);
            int textX = (int)(centerX - textWidth / 2);
            int textY = (int)(worldY + pixelHeight + 8);

            // Draw text background for readability
            Raylib.DrawRectangle(textX - 4, textY - 2, textWidth + 8, fontSize + 4,
                new Color(0, 0, 0, 200));

            // Draw failure text in red
            Raylib.DrawText(failureMessage, textX, textY, fontSize, Color.Red);
        }

        // Draw projected production text for environment buildings
        if (hasEnvironmentRequirement)
        {
            string productionText = nodeCount > 0
                ? $"+{projectedProduction} {resourceName}/s ({nodeCount} nodes)"
                : $"No {resourceName} in range!";

            int fontSize = 16;
            int textWidth = Raylib.MeasureText(productionText, fontSize);
            int textX = (int)(centerX - textWidth / 2);
            int textY = (int)(worldY - 25);

            // Draw text background for readability
            Raylib.DrawRectangle(textX - 4, textY - 2, textWidth + 8, fontSize + 4,
                new Color(0, 0, 0, 180));

            // Draw text
            Color textColor = nodeCount > 0 ? Color.Lime : Color.Red;
            Raylib.DrawText(productionText, textX, textY, fontSize, textColor);
        }
    }

    private bool IsValidPlacement(BuildingTypeId typeId, int tileX, int tileY, int width, int height)
    {
        ref readonly var mapConfig = ref _gameData.Db.MapConfigData.FindById(0);

        // Check map bounds
        _lastBoundsValid = !(tileX < 0 || tileY < 0 ||
            tileX + width > mapConfig.WidthTiles ||
            tileY + height > mapConfig.HeightTiles);

        if (!_lastBoundsValid)
        {
            _lastValidationResult = ConstraintValidationResult.Failure(ConstraintType.None);
            return false;
        }

        // Check terrain passability and resource nodes for all tiles in building footprint
        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)typeId);
        var resourceNodes = _simWorld.ResourceNodeRows;
        _lastTerrainValid = true;
        for (int tx = tileX; tx < tileX + width; tx++)
        {
            for (int ty = tileY; ty < tileY + height; ty++)
            {
                if (!_terrainData.IsPassable(tx, ty))
                {
                    _lastTerrainValid = false;
                    _lastValidationResult = ConstraintValidationResult.Failure(ConstraintType.None);
                    return false;
                }
                // Check for resource node blocking (unless building requires placement on node)
                if (!typeData.RequiresOnTopOfNode && _terrainData.IsTileBlockedByResourceNode(tx, ty, resourceNodes))
                {
                    _lastTerrainValid = false;
                    _lastValidationResult = ConstraintValidationResult.Failure(ConstraintType.None);
                    return false;
                }
            }
        }

        // Check for building overlap
        _lastOverlapValid = true;
        var buildings = _simWorld.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var existing)) continue;
            if (!existing.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Check AABB overlap
            if (RectanglesOverlap(
                tileX, tileY, width, height,
                existing.TileX, existing.TileY, existing.Width, existing.Height))
            {
                _lastOverlapValid = false;
                _lastValidationResult = ConstraintValidationResult.Failure(ConstraintType.None);
                return false;
            }
        }

        // Check constraint system
        _lastValidationResult = _constraintValidator.Validate(_simWorld, typeId, tileX, tileY);
        return _lastValidationResult.IsValid;
    }

    private static bool RectanglesOverlap(
        int x1, int y1, int w1, int h1,
        int x2, int y2, int w2, int h2)
    {
        return x1 < x2 + w2 && x1 + w1 > x2 &&
               y1 < y2 + h2 && y1 + h1 > y2;
    }

    /// <summary>
    /// Calculates projected production for environment-dependent buildings.
    /// Returns (nodeCount, projectedProduction, resourceName).
    /// </summary>
    private (int nodeCount, int projectedProduction, string resourceName) CalculateEnvironmentProduction(
        BuildingTypeId buildingTypeId,
        in BuildingTypeData typeData,
        int centerTileX, int centerTileY,
        int tileX, int tileY, int width, int height)
    {
        int nodeCount = 0;
        string resourceName = "";

        // Handle terrain requirements
        if (typeData.RequiredTerrainType >= 0)
        {
            var targetTerrain = (TerrainType)typeData.RequiredTerrainType;

            // Sawmill: uses adjacent terrain tiles
            if (buildingTypeId == BuildingTypeId.Sawmill)
            {
                nodeCount = CountAdjacentTerrain(tileX, tileY, width, height, targetTerrain);
                resourceName = "Wood";
            }
            else
            {
                // Farm, FishingHut, HuntingCottage: use radius
                nodeCount = _terrainData.CountTerrainInRadius(centerTileX, centerTileY, typeData.EnvironmentQueryRadius, targetTerrain);
                resourceName = "Food";
            }
        }
        // Handle resource node requirements
        else if (typeData.RequiredResourceNodeType >= 0)
        {
            var targetType = (ResourceTypeId)typeData.RequiredResourceNodeType;
            resourceName = GetResourceName(targetType);

            if (typeData.RequiresOnTopOfNode)
            {
                // Oil refinery: check for node at building location
                nodeCount = CountResourceNodesOnTiles(tileX, tileY, width, height, targetType);
            }
            else
            {
                // IronMine etc: count adjacent nodes
                nodeCount = CountAdjacentResourceNodes(tileX, tileY, width, height, targetType);
            }
        }
        // Handle adaptive quarry (any ore) - uses adjacent nodes
        else if (typeData.RequiredResourceNodeType == AnyOreRequirement)
        {
            var (count, detectedType) = CountAdjacentOreNodes(tileX, tileY, width, height);
            nodeCount = count;
            resourceName = count > 0 ? GetResourceName(detectedType) : "Ore";
        }

        // Calculate projected production (capped by MaxNodeBonus if set)
        int effectiveNodeCount = typeData.MaxNodeBonus > 0
            ? System.Math.Min(nodeCount, typeData.MaxNodeBonus)
            : nodeCount;
        int projectedProduction = typeData.BaseRatePerNode * effectiveNodeCount;

        return (nodeCount, projectedProduction, resourceName);
    }

    private static string GetResourceName(ResourceTypeId typeId) => typeId switch
    {
        ResourceTypeId.Gold => "Gold",
        ResourceTypeId.Wood => "Wood",
        ResourceTypeId.Stone => "Stone",
        ResourceTypeId.Iron => "Iron",
        ResourceTypeId.Oil => "Oil",
        _ => "Resource"
    };

    private int CountNearbyResourceNodes(int centerTileX, int centerTileY, int radiusTiles, ResourceTypeId targetType)
    {
        int count = 0;
        int radiusSq = radiusTiles * radiusTiles;
        var resourceNodes = _simWorld.ResourceNodeRows;

        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            if (node.TypeId != targetType) continue;

            int dx = node.TileX - centerTileX;
            int dy = node.TileY - centerTileY;

            if (dx * dx + dy * dy <= radiusSq)
                count++;
        }

        return count;
    }

    private (int count, ResourceTypeId detectedType) CountNearbyOreNodes(int centerTileX, int centerTileY, int radiusTiles)
    {
        int stoneCount = 0;
        int ironCount = 0;
        int goldCount = 0;
        int radiusSq = radiusTiles * radiusTiles;
        var resourceNodes = _simWorld.ResourceNodeRows;

        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            int dx = node.TileX - centerTileX;
            int dy = node.TileY - centerTileY;

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
            return (0, ResourceTypeId.Gold);

        // Return TOTAL count of all ores, detect most abundant type for output
        ResourceTypeId detectedType;
        if (stoneCount >= ironCount && stoneCount >= goldCount)
            detectedType = ResourceTypeId.Stone;
        else if (ironCount >= goldCount)
            detectedType = ResourceTypeId.Iron;
        else
            detectedType = ResourceTypeId.Gold;

        return (totalCount, detectedType);
    }

    private int CountResourceNodesOnTiles(int tileX, int tileY, int width, int height, ResourceTypeId targetType)
    {
        int count = 0;
        var resourceNodes = _simWorld.ResourceNodeRows;

        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            if (node.TypeId != targetType) continue;

            if (node.TileX >= tileX && node.TileX < tileX + width &&
                node.TileY >= tileY && node.TileY < tileY + height)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Draws the ring of adjacent tiles around a building for visual preview.
    /// </summary>
    private void DrawAdjacentTileRing(int tileX, int tileY, int width, int height, Color fillColor, Color borderColor)
    {
        // Top row (y = tileY - 1)
        for (int x = tileX - 1; x <= tileX + width; x++)
        {
            DrawTile(x, tileY - 1, fillColor, borderColor);
        }

        // Bottom row (y = tileY + height)
        for (int x = tileX - 1; x <= tileX + width; x++)
        {
            DrawTile(x, tileY + height, fillColor, borderColor);
        }

        // Left column, excluding corners
        for (int y = tileY; y < tileY + height; y++)
        {
            DrawTile(tileX - 1, y, fillColor, borderColor);
        }

        // Right column, excluding corners
        for (int y = tileY; y < tileY + height; y++)
        {
            DrawTile(tileX + width, y, fillColor, borderColor);
        }
    }

    private void DrawTile(int tx, int ty, Color fillColor, Color borderColor)
    {
        int px = tx * _tileSize;
        int py = ty * _tileSize;
        Raylib.DrawRectangle(px, py, _tileSize, _tileSize, fillColor);
        Raylib.DrawRectangleLines(px, py, _tileSize, _tileSize, borderColor);
    }

    /// <summary>
    /// Counts terrain tiles adjacent to the building's boundaries.
    /// Used for Sawmill (counts Dirt tiles with trees adjacent to the building).
    /// </summary>
    private int CountAdjacentTerrain(int tileX, int tileY, int width, int height, TerrainType targetTerrain)
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
    /// Counts resource nodes of a specific type adjacent to building boundaries.
    /// </summary>
    private int CountAdjacentResourceNodes(int tileX, int tileY, int width, int height, ResourceTypeId targetType)
    {
        int count = 0;
        var resourceNodes = _simWorld.ResourceNodeRows;

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
    /// Counts ore nodes (Stone, Iron, Gold) adjacent to building boundaries.
    /// Used for Quarry - counts nodes directly touching the building.
    /// </summary>
    private (int count, ResourceTypeId detectedType) CountAdjacentOreNodes(int tileX, int tileY, int width, int height)
    {
        int stoneCount = 0;
        int ironCount = 0;
        int goldCount = 0;
        var resourceNodes = _simWorld.ResourceNodeRows;

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

    private Texture2D GetTexture(BuildingTypeId typeId)
    {
        if (_textures.TryGetValue(typeId, out var texture))
        {
            return texture;
        }

        // Get sprite filename from data-driven BuildingTypeData
        ref readonly var buildingData = ref _gameData.Db.BuildingTypeData.FindById((int)typeId);
        string spriteFile = buildingData.SpriteFile; // StringHandle -> string via implicit conversion

        if (!string.IsNullOrEmpty(spriteFile))
        {
            string path = $"Assets/Buildings/{spriteFile}";
            texture = Raylib.LoadTexture(path);
            if (texture.Id != 0)
            {
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
            }
        }

        _textures[typeId] = texture;
        return texture;
    }

    /// <summary>
    /// Gets a human-readable constraint failure message for UI display.
    /// </summary>
    public string GetFailureMessage()
    {
        if (!_lastBoundsValid) return "Out of bounds";
        if (!_lastOverlapValid) return "Overlaps existing building";
        if (!_lastValidationResult.IsValid)
        {
            return _lastValidationResult.FailedType switch
            {
                ConstraintType.RequiresPowerConnection => "Requires power connection",
                ConstraintType.RequiresNearbyBuilding => "Required building not nearby",
                ConstraintType.ExcludesNearbyBuilding => "Too close to restricted building",
                ConstraintType.RequiresNearbyResource => "Required resource not nearby",
                ConstraintType.RequiresClearArea => "Area not clear",
                ConstraintType.RequiresNearbyBuildingFlag => "Required building type not nearby",
                ConstraintType.ExcludesNearbyBuildingFlag => "Too close to restricted building type",
                ConstraintType.RequiresAccessTile => "No access point available",
                _ => "Invalid placement"
            };
        }
        return string.Empty;
    }
}
