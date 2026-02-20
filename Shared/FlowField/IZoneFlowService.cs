using System;
using Core;

namespace FlowField;

/// <summary>
/// Indicates which cache a flow direction came from (for debug visualization).
/// </summary>
public enum FlowCacheType
{
    None,
    TargetSet,    // Player formation movement commands
    SingleDest,   // Single destination movement
    MultiTarget   // Enemy pathfinding (global seeds)
}

/// <summary>
/// Interface for flow field service - provides movement directions toward targets.
/// </summary>
public interface IZoneFlowService
{
    /// <summary>
    /// Sets attack seeds (multi-target mode for enemy pathfinding).
    /// </summary>
    void SetSeeds(List<(int tileX, int tileY, Fixed64 cost)> seeds);

    /// <summary>
    /// Clears all seeds.
    /// </summary>
    void ClearSeeds();

    /// <summary>
    /// Gets flow direction toward seeds at a world position.
    /// </summary>
    Fixed64Vec2 GetFlowDirection(Fixed64Vec2 worldPos);

    /// <summary>
    /// Gets flow direction toward a specific destination tile.
    /// </summary>
    Fixed64Vec2 GetFlowDirectionForDestination(Fixed64Vec2 worldPos, int destTileX, int destTileY, bool ignoreBuildings = false);

    /// <summary>
    /// Gets cached flow direction toward a specific destination tile.
    /// Returns Zero if no cached flow exists (doesn't compute new flows).
    /// </summary>
    Fixed64Vec2 GetFlowDirectionForDestinationCached(Fixed64Vec2 worldPos, int destTileX, int destTileY, bool ignoreBuildings = false);

    /// <summary>
    /// Gets flow direction toward a building by finding the nearest passable perimeter tile.
    /// Buildings occupy blocked tiles, so we path to the closest tile around their footprint.
    /// </summary>
    Fixed64Vec2 GetFlowDirectionForBuildingDestination(
        Fixed64Vec2 worldPos,
        int buildingTileX, int buildingTileY,
        int buildingWidth, int buildingHeight,
        bool ignoreBuildings = false);

    /// <summary>
    /// Gets flow direction toward the nearest of multiple target tiles.
    /// Used for formation movement where units flow toward any of the formation slots.
    /// </summary>
    Fixed64Vec2 GetFlowDirectionForTargets(
        Fixed64Vec2 worldPos,
        ReadOnlySpan<(int tileX, int tileY)> targets);

    /// <summary>
    /// Checks if a position is near any seed (for arrival detection).
    /// </summary>
    bool IsNearSeed(Fixed64Vec2 worldPos, Fixed64 arrivalDistance);

    /// <summary>
    /// Marks a tile as dirty (requires flow invalidation).
    /// </summary>
    void MarkTileDirty(int tileX, int tileY);

    /// <summary>
    /// Marks flows as dirty (need recomputation).
    /// </summary>
    void MarkFlowsDirty();

    /// <summary>
    /// Clears the dirty flag after recomputation.
    /// </summary>
    void ClearDirtyFlag();

    /// <summary>
    /// Flushes pending invalidations to update flows.
    /// </summary>
    void FlushPendingInvalidations();

    /// <summary>
    /// Invalidates flows for a sector.
    /// </summary>
    void InvalidateSector(int sectorX, int sectorY);

    /// <summary>
    /// Invalidates all cached flows.
    /// </summary>
    void InvalidateAllFlows();

    /// <summary>
    /// Gets the number of cached zone flows.
    /// </summary>
    int GetCachedZoneFlowCount();

    /// <summary>
    /// Gets cached flow direction at a tile from any cached flow field.
    /// Used for debug visualization. Returns Zero if no cached flow exists.
    /// </summary>
    Fixed64Vec2 GetAnyCachedFlowDirection(int tileX, int tileY);

    /// <summary>
    /// Gets cached flow direction with cache type information.
    /// Used for debug visualization to color-code arrows by source.
    /// </summary>
    (Fixed64Vec2 direction, FlowCacheType cacheType) GetAnyCachedFlowDirectionWithType(int tileX, int tileY);

    /// <summary>
    /// Gets the underlying zone graph for direct zone queries.
    /// </summary>
    IZoneGraph GetZoneGraph();
}
