using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.DualGrid.Utilities;
using Catrillion.Simulation.Components;
using Core;
using GameDocDatabase;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Result of constraint validation for building placement.
/// </summary>
public readonly struct ConstraintValidationResult
{
    public readonly bool IsValid;
    public readonly ConstraintType FailedType;
    public readonly GameDataId FailureMessageId;

    public ConstraintValidationResult(bool isValid, ConstraintType failedType = ConstraintType.None, GameDataId failureMessageId = default)
    {
        IsValid = isValid;
        FailedType = failedType;
        FailureMessageId = failureMessageId;
    }

    public static ConstraintValidationResult Success => new(true);

    public static ConstraintValidationResult Failure(ConstraintType type, GameDataId message = default)
        => new(false, type, message);
}

/// <summary>
/// Validates building placement constraints.
/// Checks all constraints defined in BuildingConstraintData for a building type.
/// </summary>
public sealed class BuildingConstraintValidator
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly PowerNetworkService _powerNetwork;
    private readonly TerrainDataService _terrainData;
    private readonly EnvironmentService _environmentService;
    private readonly int _tileSize;

    public BuildingConstraintValidator(
        GameDataManager<GameDocDb> gameData,
        PowerNetworkService powerNetwork,
        TerrainDataService terrainData,
        EnvironmentService environmentService)
    {
        _gameData = gameData;
        _powerNetwork = powerNetwork;
        _terrainData = terrainData;
        _environmentService = environmentService;
        _tileSize = gameData.Db.MapConfigData.FindById(0).TileSize;
    }

    // Universal constraint settings
    private const int EnemyExclusionRadius = 128; // Pixels - no building within ~4 tiles of enemies

    /// <summary>
    /// Validates all constraints for placing a building at the given location.
    /// </summary>
    public ConstraintValidationResult Validate(
        SimWorld world,
        BuildingTypeId typeId,
        int tileX, int tileY)
    {
        var db = _gameData.Db;
        ref readonly var typeData = ref db.BuildingTypeData.FindById((int)typeId);
        int width = typeData.Width;
        int height = typeData.Height;

        // === Universal constraints (apply to all buildings) ===

        // Cannot build if enemies are nearby
        var enemyResult = CheckNoEnemiesNearby(world, tileX, tileY, width, height);
        if (!enemyResult.IsValid)
        {
            return enemyResult;
        }

        // Cannot build on top of friendly units
        var unitResult = CheckNoUnitsOnTop(world, tileX, tileY, width, height);
        if (!unitResult.IsValid)
        {
            return unitResult;
        }

        // All buildings must be connected to power grid (except CommandCenter and power generators)
        bool isCommandCenter = typeId == BuildingTypeId.CommandCenter;
        bool isPowerGenerator = typeData.PowerConsumption < 0;
        if (!isCommandCenter && !isPowerGenerator)
        {
            var powerResult = CheckUniversalPowerConnection(world, tileX, tileY, width, height);
            if (!powerResult.IsValid)
            {
                return powerResult;
            }
        }

        // === Per-building-type constraints ===

        // Check all constraints defined for this building type
        var constraints = db.BuildingConstraintData;
        for (int i = 0; i < constraints.Count; i++)
        {
            ref readonly var constraint = ref constraints.GetAtIndex(i);
            if (constraint.BuildingTypeId != (int)typeId) continue;

            var result = CheckConstraint(world, in constraint, in typeData, tileX, tileY, width, height);
            if (!result.IsValid)
            {
                return result;
            }
        }

        // Check if placing this building would block access to any existing building
        var blockResult = CheckWouldBlockExistingAccess(world, tileX, tileY, width, height);
        if (!blockResult.IsValid)
        {
            return blockResult;
        }

        return ConstraintValidationResult.Success;
    }

    private ConstraintValidationResult CheckConstraint(
        SimWorld world,
        in BuildingConstraintData constraint,
        in BuildingTypeData typeData,
        int tileX, int tileY, int width, int height)
    {
        return constraint.Type switch
        {
            ConstraintType.RequiresPowerConnection =>
                CheckPowerConnection(world, tileX, tileY, width, height, in constraint),

            ConstraintType.RequiresNearbyBuilding =>
                CheckNearbyBuilding(world, tileX, tileY, width, height, in constraint, required: true),

            ConstraintType.ExcludesNearbyBuilding =>
                CheckNearbyBuilding(world, tileX, tileY, width, height, in constraint, required: false),

            ConstraintType.RequiresNearbyResource =>
                CheckNearbyResource(world, tileX, tileY, width, height, in constraint),

            ConstraintType.RequiresClearArea =>
                CheckClearArea(world, tileX, tileY, width, height, in constraint),

            ConstraintType.RequiresNearbyBuildingFlag =>
                CheckNearbyBuildingFlag(world, tileX, tileY, width, height, in constraint, required: true),

            ConstraintType.ExcludesNearbyBuildingFlag =>
                CheckNearbyBuildingFlag(world, tileX, tileY, width, height, in constraint, required: false),

            ConstraintType.RequiresAccessTile =>
                CheckAccessTile(world, tileX, tileY, width, height, in constraint),

            ConstraintType.RequiresResourceNodeOnTop =>
                CheckResourceNodeOnTop(world, tileX, tileY, width, height, in constraint),

            ConstraintType.RequiresAdjacentResourceNode =>
                CheckAdjacentResourceNode(world, tileX, tileY, width, height, in constraint),

            ConstraintType.RequiresAdjacentOre =>
                CheckAdjacentOre(world, tileX, tileY, width, height, in constraint),

            ConstraintType.RequiresAdjacentTerrain =>
                CheckAdjacentTerrain(tileX, tileY, width, height, in constraint),

            ConstraintType.RequiresTerrainInRadius =>
                CheckTerrainInRadius(tileX, tileY, width, height, in constraint),

            ConstraintType.ExcludesAdjacentToBuildingType =>
                CheckExcludesAdjacentToBuildingType(world, tileX, tileY, width, height, in constraint),

            ConstraintType.ExcludesNearbyEnemies =>
                CheckExcludesNearbyEnemies(world, tileX, tileY, width, height, in constraint),

            ConstraintType.ExcludesUnitsOnTop =>
                CheckExcludesUnitsOnTop(world, tileX, tileY, width, height, in constraint),

            _ => ConstraintValidationResult.Success
        };
    }

    private ConstraintValidationResult CheckPowerConnection(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        if (_powerNetwork.WouldBePowered(tileX, tileY, width, height))
        {
            return ConstraintValidationResult.Success;
        }
        return ConstraintValidationResult.Failure(ConstraintType.RequiresPowerConnection, constraint.FailureMessageId);
    }

    /// <summary>
    /// Universal power grid check - all buildings must be connected to power.
    /// </summary>
    private ConstraintValidationResult CheckUniversalPowerConnection(
        SimWorld world, int tileX, int tileY, int width, int height)
    {
        if (_powerNetwork.WouldBePowered(tileX, tileY, width, height))
        {
            return ConstraintValidationResult.Success;
        }
        return ConstraintValidationResult.Failure(ConstraintType.RequiresPowerConnection);
    }

    private ConstraintValidationResult CheckNearbyBuilding(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint, bool required)
    {
        var position = GetBuildingCenter(tileX, tileY, width, height);
        var radius = constraint.Radius;
        var buildings = world.BuildingRows;
        int count = 0;

        foreach (int slot in buildings.QueryRadius(position, radius))
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if ((int)building.TypeId == constraint.TargetId)
            {
                count++;
            }
        }

        if (required)
        {
            int minCount = constraint.MinCount > 0 ? constraint.MinCount : 1;
            if (count < minCount)
            {
                return ConstraintValidationResult.Failure(ConstraintType.RequiresNearbyBuilding, constraint.FailureMessageId);
            }
        }
        else
        {
            // Exclusion: count must be 0 (or less than max if specified)
            int maxCount = constraint.MaxCount >= 0 ? constraint.MaxCount : 0;
            if (count > maxCount)
            {
                return ConstraintValidationResult.Failure(ConstraintType.ExcludesNearbyBuilding, constraint.FailureMessageId);
            }
        }

        return ConstraintValidationResult.Success;
    }

    private ConstraintValidationResult CheckNearbyResource(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var position = GetBuildingCenter(tileX, tileY, width, height);
        var radiusSq = constraint.Radius * constraint.Radius;
        var resources = world.ResourceNodeRows;
        int count = 0;

        // ResourceNodeRow uses tile coordinates, not world position - iterate manually
        for (int slot = 0; slot < resources.Count; slot++)
        {
            if (!resources.TryGetRow(slot, out var node)) continue;
            if ((int)node.TypeId != constraint.TargetId) continue;

            // Convert tile to world position for distance check
            int nodeWorldX = node.TileX * _tileSize + _tileSize / 2;
            int nodeWorldY = node.TileY * _tileSize + _tileSize / 2;
            var nodePos = new Fixed64Vec2(Fixed64.FromInt(nodeWorldX), Fixed64.FromInt(nodeWorldY));

            var distSq = Fixed64Vec2.DistanceSquared(position, nodePos);
            if (distSq <= radiusSq)
            {
                count++;
            }
        }

        int minCount = constraint.MinCount > 0 ? constraint.MinCount : 1;
        if (count < minCount)
        {
            return ConstraintValidationResult.Failure(ConstraintType.RequiresNearbyResource, constraint.FailureMessageId);
        }

        return ConstraintValidationResult.Success;
    }

    private ConstraintValidationResult CheckClearArea(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var position = GetBuildingCenter(tileX, tileY, width, height);
        var radius = constraint.Radius;
        var buildings = world.BuildingRows;

        foreach (int slot in buildings.QueryRadius(position, radius))
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Found a building in the clear area
            return ConstraintValidationResult.Failure(ConstraintType.RequiresClearArea, constraint.FailureMessageId);
        }

        return ConstraintValidationResult.Success;
    }

    private ConstraintValidationResult CheckNearbyBuildingFlag(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint, bool required)
    {
        var position = GetBuildingCenter(tileX, tileY, width, height);
        var radius = constraint.Radius;
        var buildings = world.BuildingRows;
        var db = _gameData.Db;
        int count = 0;

        foreach (int slot in buildings.QueryRadius(position, radius))
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            ref readonly var buildingType = ref db.BuildingTypeData.FindById((int)building.TypeId);
            if ((buildingType.ConstraintFlags & constraint.TargetFlags) != 0)
            {
                count++;
            }
        }

        if (required)
        {
            int minCount = constraint.MinCount > 0 ? constraint.MinCount : 1;
            if (count < minCount)
            {
                return ConstraintValidationResult.Failure(ConstraintType.RequiresNearbyBuildingFlag, constraint.FailureMessageId);
            }
        }
        else
        {
            int maxCount = constraint.MaxCount >= 0 ? constraint.MaxCount : 0;
            if (count > maxCount)
            {
                return ConstraintValidationResult.Failure(ConstraintType.ExcludesNearbyBuildingFlag, constraint.FailureMessageId);
            }
        }

        return ConstraintValidationResult.Success;
    }

    private ConstraintValidationResult CheckAccessTile(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var resourceNodes = world.ResourceNodeRows;

        // Check each cardinal direction for at least one clear tile
        // North edge
        for (int x = tileX; x < tileX + width; x++)
        {
            if (IsTileClear(resourceNodes, x, tileY - 1))
                return ConstraintValidationResult.Success;
        }

        // South edge
        for (int x = tileX; x < tileX + width; x++)
        {
            if (IsTileClear(resourceNodes, x, tileY + height))
                return ConstraintValidationResult.Success;
        }

        // West edge
        for (int y = tileY; y < tileY + height; y++)
        {
            if (IsTileClear(resourceNodes, tileX - 1, y))
                return ConstraintValidationResult.Success;
        }

        // East edge
        for (int y = tileY; y < tileY + height; y++)
        {
            if (IsTileClear(resourceNodes, tileX + width, y))
                return ConstraintValidationResult.Success;
        }

        // No clear access tile found
        return ConstraintValidationResult.Failure(ConstraintType.RequiresAccessTile, constraint.FailureMessageId);
    }

    /// <summary>
    /// Checks if placing a building at the given location would block access to any existing building
    /// that requires access tiles.
    /// </summary>
    private ConstraintValidationResult CheckWouldBlockExistingAccess(
        SimWorld world, int newTileX, int newTileY, int newWidth, int newHeight)
    {
        var buildings = world.BuildingRows;
        var resourceNodes = world.ResourceNodeRows;
        var constraints = _gameData.Db.BuildingConstraintData;

        // Check each existing building (both active and under construction need access protection)
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var existing)) continue;
            // Protect both active and under-construction buildings that require access
            if (!existing.Flags.HasFlag(BuildingFlags.IsActive) &&
                !existing.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;

            // Check if this building type has RequiresAccessTile constraint
            bool requiresAccess = false;
            for (int i = 0; i < constraints.Count; i++)
            {
                ref readonly var constraint = ref constraints.GetAtIndex(i);
                if (constraint.BuildingTypeId == (int)existing.TypeId &&
                    constraint.Type == ConstraintType.RequiresAccessTile)
                {
                    requiresAccess = true;
                    break;
                }
            }

            if (!requiresAccess) continue;

            // Check if placing the new building would block all access to this existing building
            if (WouldBlockAllAccess(resourceNodes,
                existing.TileX, existing.TileY, existing.Width, existing.Height,
                newTileX, newTileY, newWidth, newHeight))
            {
                return ConstraintValidationResult.Failure(ConstraintType.RequiresAccessTile);
            }
        }

        return ConstraintValidationResult.Success;
    }

    /// <summary>
    /// Checks if placing a new building would block all access tiles for an existing building.
    /// Only returns true if: (1) the existing building currently has access, AND (2) the new building would block it.
    /// </summary>
    private bool WouldBlockAllAccess(
        ResourceNodeRowTable resourceNodes,
        int existingTileX, int existingTileY, int existingWidth, int existingHeight,
        int newTileX, int newTileY, int newWidth, int newHeight)
    {
        int ex = existingTileX;
        int ey = existingTileY;
        int ew = existingWidth;
        int eh = existingHeight;

        // First, check if the existing building currently has any access (without the new building)
        // If it already has no access, we shouldn't block placement of the new building
        bool hasCurrentAccess = false;

        // North edge
        for (int x = ex; x < ex + ew && !hasCurrentAccess; x++)
        {
            if (IsTileClear(resourceNodes, x, ey - 1))
                hasCurrentAccess = true;
        }
        // South edge
        for (int x = ex; x < ex + ew && !hasCurrentAccess; x++)
        {
            if (IsTileClear(resourceNodes, x, ey + eh))
                hasCurrentAccess = true;
        }
        // West edge
        for (int y = ey; y < ey + eh && !hasCurrentAccess; y++)
        {
            if (IsTileClear(resourceNodes, ex - 1, y))
                hasCurrentAccess = true;
        }
        // East edge
        for (int y = ey; y < ey + eh && !hasCurrentAccess; y++)
        {
            if (IsTileClear(resourceNodes, ex + ew, y))
                hasCurrentAccess = true;
        }

        // If the existing building already has no access, don't block the new building placement
        if (!hasCurrentAccess)
            return false;

        // Now check if placing the new building would block ALL remaining access
        // North edge
        for (int x = ex; x < ex + ew; x++)
        {
            if (IsTileClearWithProposedBuilding(resourceNodes, x, ey - 1, newTileX, newTileY, newWidth, newHeight))
                return false; // At least one access tile would remain
        }

        // South edge
        for (int x = ex; x < ex + ew; x++)
        {
            if (IsTileClearWithProposedBuilding(resourceNodes, x, ey + eh, newTileX, newTileY, newWidth, newHeight))
                return false;
        }

        // West edge
        for (int y = ey; y < ey + eh; y++)
        {
            if (IsTileClearWithProposedBuilding(resourceNodes, ex - 1, y, newTileX, newTileY, newWidth, newHeight))
                return false;
        }

        // East edge
        for (int y = ey; y < ey + eh; y++)
        {
            if (IsTileClearWithProposedBuilding(resourceNodes, ex + ew, y, newTileX, newTileY, newWidth, newHeight))
                return false;
        }

        // The new building would block all remaining access tiles
        return true;
    }

    /// <summary>
    /// Checks if a tile would be clear after placing the proposed building.
    /// Uses BuildingOccupancyGrid for O(1) lookup.
    /// </summary>
    private bool IsTileClearWithProposedBuilding(
        ResourceNodeRowTable resourceNodes, int tileX, int tileY,
        int newBuildingX, int newBuildingY, int newWidth, int newHeight)
    {
        // Check if tile is blocked by anything (terrain, existing building, or resource node)
        if (_terrainData.IsTileBlocked(tileX, tileY, resourceNodes))
            return false;

        // Check if the proposed new building would occupy this tile
        if (tileX >= newBuildingX && tileX < newBuildingX + newWidth &&
            tileY >= newBuildingY && tileY < newBuildingY + newHeight)
        {
            return false;
        }

        return true;
    }

    private bool IsTileClear(ResourceNodeRowTable resourceNodes, int tileX, int tileY)
    {
        // Check if tile is blocked by anything (terrain, building, or resource node)
        return !_terrainData.IsTileBlocked(tileX, tileY, resourceNodes);
    }

    private Fixed64Vec2 GetBuildingCenter(int tileX, int tileY, int width, int height)
    {
        int centerX = tileX * _tileSize + (width * _tileSize) / 2;
        int centerY = tileY * _tileSize + (height * _tileSize) / 2;
        return new Fixed64Vec2(Fixed64.FromInt(centerX), Fixed64.FromInt(centerY));
    }

    private ConstraintValidationResult CheckResourceNodeOnTop(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var targetType = (ResourceTypeId)constraint.TargetId;
        var resourceNodes = world.ResourceNodeRows;

        // Check if any matching resource node is within the building footprint
        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            if (node.TypeId != targetType) continue;

            if (node.TileX >= tileX && node.TileX < tileX + width &&
                node.TileY >= tileY && node.TileY < tileY + height)
            {
                return ConstraintValidationResult.Success;
            }
        }

        return ConstraintValidationResult.Failure(ConstraintType.RequiresResourceNodeOnTop, constraint.FailureMessageId);
    }

    private ConstraintValidationResult CheckAdjacentResourceNode(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var targetType = (ResourceTypeId)constraint.TargetId;
        var resourceNodes = world.ResourceNodeRows;

        // Check if any matching resource node is adjacent to building boundary
        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);
            if (node.TypeId != targetType) continue;

            if (IsAdjacentToBuilding(node.TileX, node.TileY, tileX, tileY, width, height))
            {
                return ConstraintValidationResult.Success;
            }
        }

        return ConstraintValidationResult.Failure(ConstraintType.RequiresAdjacentResourceNode, constraint.FailureMessageId);
    }

    private ConstraintValidationResult CheckAdjacentOre(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var resourceNodes = world.ResourceNodeRows;

        // Check if any ore node (Stone, Iron, Gold) is adjacent to building boundary
        for (int slot = 0; slot < resourceNodes.Count; slot++)
        {
            var node = resourceNodes.GetRowBySlot(slot);

            // Check if it's an ore type
            if (node.TypeId != ResourceTypeId.Stone &&
                node.TypeId != ResourceTypeId.Iron &&
                node.TypeId != ResourceTypeId.Gold)
                continue;

            if (IsAdjacentToBuilding(node.TileX, node.TileY, tileX, tileY, width, height))
            {
                return ConstraintValidationResult.Success;
            }
        }

        return ConstraintValidationResult.Failure(ConstraintType.RequiresAdjacentOre, constraint.FailureMessageId);
    }

    private ConstraintValidationResult CheckAdjacentTerrain(
        int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var targetTerrain = (TerrainType)constraint.TargetId;
        int count = _environmentService.CountAdjacentTerrain(tileX, tileY, width, height, targetTerrain);

        int minCount = constraint.MinCount > 0 ? constraint.MinCount : 1;
        if (count >= minCount)
        {
            return ConstraintValidationResult.Success;
        }

        return ConstraintValidationResult.Failure(ConstraintType.RequiresAdjacentTerrain, constraint.FailureMessageId);
    }

    private ConstraintValidationResult CheckTerrainInRadius(
        int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var targetTerrain = (TerrainType)constraint.TargetId;
        int centerTileX = tileX + width / 2;
        int centerTileY = tileY + height / 2;
        int radius = constraint.Radius.ToInt();

        int count = _terrainData.CountTerrainInRadius(centerTileX, centerTileY, radius, targetTerrain);

        int minCount = constraint.MinCount > 0 ? constraint.MinCount : 1;
        if (count >= minCount)
        {
            return ConstraintValidationResult.Success;
        }

        return ConstraintValidationResult.Failure(ConstraintType.RequiresTerrainInRadius, constraint.FailureMessageId);
    }

    private static bool IsAdjacentToBuilding(int nodeTileX, int nodeTileY, int buildingX, int buildingY, int width, int height)
    {
        // Node must be within 1 tile of the building boundary (in the ring around it)

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

    private ConstraintValidationResult CheckExcludesAdjacentToBuildingType(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var targetTypeId = (BuildingTypeId)constraint.TargetId;
        var buildings = world.BuildingRows;

        // Check if any building of the target type is adjacent to the proposed placement
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var existing)) continue;
            if (!existing.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (existing.TypeId != targetTypeId) continue;

            // Check if the proposed building footprint would be adjacent to this building
            if (AreBuildingsAdjacent(
                tileX, tileY, width, height,
                existing.TileX, existing.TileY, existing.Width, existing.Height))
            {
                return ConstraintValidationResult.Failure(ConstraintType.ExcludesAdjacentToBuildingType, constraint.FailureMessageId);
            }
        }

        return ConstraintValidationResult.Success;
    }

    private static bool AreBuildingsAdjacent(
        int x1, int y1, int w1, int h1,
        int x2, int y2, int w2, int h2)
    {
        // Two buildings are adjacent if they are within 1 tile of each other but not overlapping

        // Check if they overlap (not adjacent)
        bool overlapping = x1 < x2 + w2 && x1 + w1 > x2 &&
                          y1 < y2 + h2 && y1 + h1 > y2;
        if (overlapping) return false;

        // Check if they are within 1 tile gap (adjacent)
        // Expand building 1 by 1 tile in all directions and check for overlap with building 2
        bool adjacent = (x1 - 1) < x2 + w2 && (x1 + w1 + 1) > x2 &&
                       (y1 - 1) < y2 + h2 && (y1 + h1 + 1) > y2;

        return adjacent;
    }

    // === Universal constraint checks (no constraint data required) ===

    private ConstraintValidationResult CheckNoEnemiesNearby(
        SimWorld world, int tileX, int tileY, int width, int height)
    {
        var position = GetBuildingCenter(tileX, tileY, width, height);
        var radius = Fixed64.FromInt(EnemyExclusionRadius);
        var zombies = world.ZombieRows;

        foreach (int slot in zombies.QueryRadius(position, radius))
        {
            if (!zombies.TryGetRow(slot, out var zombie)) continue;
            if (zombie.Flags.HasFlag(MortalFlags.IsDead)) continue;

            // Found a living enemy within radius
            return ConstraintValidationResult.Failure(ConstraintType.ExcludesNearbyEnemies);
        }

        return ConstraintValidationResult.Success;
    }

    private ConstraintValidationResult CheckNoUnitsOnTop(
        SimWorld world, int tileX, int tileY, int width, int height)
    {
        // Calculate building footprint in world coordinates
        var minX = Fixed64.FromInt(tileX * _tileSize);
        var maxX = Fixed64.FromInt((tileX + width) * _tileSize);
        var minY = Fixed64.FromInt(tileY * _tileSize);
        var maxY = Fixed64.FromInt((tileY + height) * _tileSize);

        var units = world.CombatUnitRows;

        foreach (int slot in units.QueryBox(minX, maxX, minY, maxY))
        {
            if (!units.TryGetRow(slot, out var unit)) continue;
            if (unit.Flags.HasFlag(MortalFlags.IsDead)) continue;
            if (unit.GarrisonedInHandle.IsValid) continue; // Ignore garrisoned units

            // Found a living, non-garrisoned unit in the footprint
            return ConstraintValidationResult.Failure(ConstraintType.ExcludesUnitsOnTop);
        }

        return ConstraintValidationResult.Success;
    }

    // === Per-building constraint checks (require constraint data) ===

    private ConstraintValidationResult CheckExcludesNearbyEnemies(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        var position = GetBuildingCenter(tileX, tileY, width, height);
        var radius = constraint.Radius;
        var zombies = world.ZombieRows;

        foreach (int slot in zombies.QueryRadius(position, radius))
        {
            if (!zombies.TryGetRow(slot, out var zombie)) continue;
            if (zombie.Flags.HasFlag(MortalFlags.IsDead)) continue;

            // Found a living enemy within radius
            return ConstraintValidationResult.Failure(ConstraintType.ExcludesNearbyEnemies, constraint.FailureMessageId);
        }

        return ConstraintValidationResult.Success;
    }

    private ConstraintValidationResult CheckExcludesUnitsOnTop(
        SimWorld world, int tileX, int tileY, int width, int height,
        in BuildingConstraintData constraint)
    {
        // Calculate building footprint in world coordinates
        var minX = Fixed64.FromInt(tileX * _tileSize);
        var maxX = Fixed64.FromInt((tileX + width) * _tileSize);
        var minY = Fixed64.FromInt(tileY * _tileSize);
        var maxY = Fixed64.FromInt((tileY + height) * _tileSize);

        var units = world.CombatUnitRows;

        foreach (int slot in units.QueryBox(minX, maxX, minY, maxY))
        {
            if (!units.TryGetRow(slot, out var unit)) continue;
            if (unit.Flags.HasFlag(MortalFlags.IsDead)) continue;
            if (unit.GarrisonedInHandle.IsValid) continue; // Ignore garrisoned units

            // Found a living, non-garrisoned unit in the footprint
            return ConstraintValidationResult.Failure(ConstraintType.ExcludesUnitsOnTop, constraint.FailureMessageId);
        }

        return ConstraintValidationResult.Success;
    }
}
