using System;
using Core;
using FlowField;
using SimTable;

namespace Catrillion.Simulation.DerivedSystems;

/// <summary>
/// Wrapper around ZoneFlowService to implement IDerivedSimSystem and IZoneFlowService.
/// ZoneFlowService computes flow fields lazily on demand, so Rebuild() is a no-op.
/// Invalidate() clears all cached flow fields so they recompute with current zone graph data.
/// </summary>
public sealed class ZoneFlowSystem : IDerivedSimSystem, IZoneFlowService
{
    private readonly ZoneFlowService _flowService;
    private readonly ZoneGraphSystem _zoneGraphSystem;

    public ZoneFlowSystem(ZoneFlowService flowService, ZoneGraphSystem zoneGraphSystem)
    {
        _flowService = flowService;
        _zoneGraphSystem = zoneGraphSystem;
    }

    /// <summary>
    /// Access the underlying ZoneFlowService for direct queries.
    /// </summary>
    public ZoneFlowService FlowService => _flowService;

    #region IDerivedSimSystem

    /// <summary>
    /// Mark all cached state as dirty. Called after rollback/snapshot restore.
    /// Clears all cached flow fields so they recompute with current zone data.
    /// </summary>
    public void Invalidate()
    {
        _flowService.InvalidateAllFlows();
    }

    /// <summary>
    /// Rebuild cached state from authoritative data if dirty.
    /// ZoneFlowService computes flow fields lazily, so this is a no-op.
    /// </summary>
    public void Rebuild()
    {
        // No-op: ZoneFlowService computes flows lazily when queried
    }

    #endregion

    #region IZoneFlowService - delegate to underlying ZoneFlowService which implements IZoneFlowService

    public void SetSeeds(List<(int tileX, int tileY, Fixed64 cost)> seeds) => _flowService.SetSeeds(seeds);

    public void ClearSeeds() => _flowService.ClearSeeds();

    public Fixed64Vec2 GetFlowDirection(Fixed64Vec2 worldPos) => _flowService.GetFlowDirection(worldPos);

    public Fixed64Vec2 GetFlowDirectionForDestination(Fixed64Vec2 worldPos, int destTileX, int destTileY, bool ignoreBuildings = false) =>
        _flowService.GetFlowDirectionForDestination(worldPos, destTileX, destTileY, ignoreBuildings);

    public Fixed64Vec2 GetFlowDirectionForDestinationCached(Fixed64Vec2 worldPos, int destTileX, int destTileY, bool ignoreBuildings = false) =>
        _flowService.GetFlowDirectionForDestinationCached(worldPos, destTileX, destTileY, ignoreBuildings);

    public Fixed64Vec2 GetFlowDirectionForBuildingDestination(
        Fixed64Vec2 worldPos,
        int buildingTileX, int buildingTileY,
        int buildingWidth, int buildingHeight,
        bool ignoreBuildings = false) =>
        _flowService.GetFlowDirectionForBuildingDestination(worldPos, buildingTileX, buildingTileY, buildingWidth, buildingHeight, ignoreBuildings);

    public Fixed64Vec2 GetFlowDirectionForTargets(
        Fixed64Vec2 worldPos,
        ReadOnlySpan<(int tileX, int tileY)> targets) =>
        _flowService.GetFlowDirectionForTargets(worldPos, targets);

    public bool IsNearSeed(Fixed64Vec2 worldPos, Fixed64 arrivalDistance) => _flowService.IsNearSeed(worldPos, arrivalDistance);

    public void MarkTileDirty(int tileX, int tileY) => _flowService.MarkTileDirty(tileX, tileY);

    public void MarkFlowsDirty() => _flowService.MarkFlowsDirty();

    public void ClearDirtyFlag() => _flowService.ClearDirtyFlag();

    public void FlushPendingInvalidations() => _flowService.FlushPendingInvalidations();

    public void InvalidateSector(int sectorX, int sectorY) => _flowService.InvalidateSector(sectorX, sectorY);

    public void InvalidateAllFlows() => _flowService.InvalidateAllFlows();

    public int GetCachedZoneFlowCount() => _flowService.GetCachedZoneFlowCount();

    public Fixed64Vec2 GetAnyCachedFlowDirection(int tileX, int tileY) => _flowService.GetAnyCachedFlowDirection(tileX, tileY);

    public (Fixed64Vec2 direction, FlowCacheType cacheType) GetAnyCachedFlowDirectionWithType(int tileX, int tileY) =>
        _flowService.GetAnyCachedFlowDirectionWithType(tileX, tileY);

    public IZoneGraph GetZoneGraph() => _zoneGraphSystem;

    #endregion
}
