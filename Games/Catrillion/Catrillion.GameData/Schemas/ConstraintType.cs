namespace Catrillion.GameData.Schemas;

/// <summary>
/// Types of placement constraints that can be applied to buildings.
/// </summary>
public enum ConstraintType : byte
{
    None = 0,

    /// <summary>
    /// Building must connect to power network (via nearby powered buildings).
    /// </summary>
    RequiresPowerConnection = 1,

    /// <summary>
    /// Must have a specific building type within radius.
    /// TargetId = BuildingTypeId to require.
    /// </summary>
    RequiresNearbyBuilding = 2,

    /// <summary>
    /// Cannot have a specific building type within radius.
    /// TargetId = BuildingTypeId to exclude.
    /// </summary>
    ExcludesNearbyBuilding = 3,

    /// <summary>
    /// Must have a resource node of specific type within radius.
    /// TargetId = ResourceTypeId to require.
    /// </summary>
    RequiresNearbyResource = 4,

    /// <summary>
    /// No buildings allowed within radius (for expansion buildings).
    /// </summary>
    RequiresClearArea = 5,

    /// <summary>
    /// Must have a building with matching constraint flags within radius.
    /// TargetFlags = flags to match (any matching flag satisfies).
    /// </summary>
    RequiresNearbyBuildingFlag = 6,

    /// <summary>
    /// Cannot have a building with matching constraint flags within radius.
    /// TargetFlags = flags to exclude.
    /// </summary>
    ExcludesNearbyBuildingFlag = 7,

    /// <summary>
    /// At least one adjacent tile (cardinal direction) must be clear for entry/exit.
    /// Checks building edges for passable, unblocked tiles.
    /// </summary>
    RequiresAccessTile = 8,

    /// <summary>
    /// Building must be placed on top of a resource node.
    /// TargetId = ResourceTypeId to require (e.g., Oil for OilRefinery).
    /// </summary>
    RequiresResourceNodeOnTop = 9,

    /// <summary>
    /// Must have a resource node of specific type adjacent to building boundary.
    /// TargetId = ResourceTypeId to require.
    /// </summary>
    RequiresAdjacentResourceNode = 10,

    /// <summary>
    /// Must have any ore node (Stone, Iron, or Gold) adjacent to building boundary.
    /// Used for Quarry which adapts to ore type.
    /// </summary>
    RequiresAdjacentOre = 11,

    /// <summary>
    /// Must have terrain of specific type adjacent to building boundary.
    /// TargetId = TerrainType to require (e.g., Dirt for Sawmill).
    /// </summary>
    RequiresAdjacentTerrain = 12,

    /// <summary>
    /// Must have terrain of specific type within radius.
    /// TargetId = TerrainType, Radius = search radius in tiles.
    /// Used for Farm (Grass), FishingHut (Water), HuntingCottage (Dirt).
    /// </summary>
    RequiresTerrainInRadius = 13,

    /// <summary>
    /// Cannot be placed adjacent to a specific building type.
    /// TargetId = BuildingTypeId to exclude adjacency with.
    /// Used to prevent buildings from being placed directly next to CommandCenter.
    /// </summary>
    ExcludesAdjacentToBuildingType = 14,

    /// <summary>
    /// Cannot be placed if enemies (zombies) are within radius.
    /// Radius = search radius in pixels.
    /// Used to prevent building in contested areas.
    /// </summary>
    ExcludesNearbyEnemies = 15,

    /// <summary>
    /// Cannot be placed if friendly combat units are in the building footprint.
    /// Prevents accidentally trapping units under buildings.
    /// </summary>
    ExcludesUnitsOnTop = 16,
}
