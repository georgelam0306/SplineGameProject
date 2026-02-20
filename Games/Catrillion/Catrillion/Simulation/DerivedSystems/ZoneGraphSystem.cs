using Core;
using FlowField;
using SimTable;

namespace Catrillion.Simulation.DerivedSystems;

/// <summary>
/// Wrapper around ZoneGraph to implement IDerivedSimSystem and IZoneGraph.
/// ZoneGraph builds sectors lazily on demand, so Rebuild() is a no-op.
/// Invalidate() clears all cached sectors so they rebuild with current world data.
/// </summary>
public sealed class ZoneGraphSystem : IDerivedSimSystem, IZoneGraph
{
    private readonly ZoneGraph _zoneGraph;

    public ZoneGraphSystem(ZoneGraph zoneGraph)
    {
        _zoneGraph = zoneGraph;
    }

    /// <summary>
    /// Access the underlying ZoneGraph for direct queries.
    /// </summary>
    public ZoneGraph Graph => _zoneGraph;

    #region IDerivedSimSystem

    /// <summary>
    /// Mark all cached state as dirty. Called after rollback/snapshot restore.
    /// Clears all built sectors so they rebuild with current world data.
    /// </summary>
    public void Invalidate()
    {
        _zoneGraph.InvalidateAllSectors();
    }

    /// <summary>
    /// Rebuild cached state from authoritative data if dirty.
    /// ZoneGraph uses lazy building, so this is a no-op - sectors are built on demand.
    /// </summary>
    public void Rebuild()
    {
        // No-op: ZoneGraph builds sectors lazily when queried
    }

    #endregion

    #region IZoneGraph - delegate to underlying ZoneGraph which implements IZoneGraph

    public void EnsureSectorBuilt(int sectorX, int sectorY) => _zoneGraph.EnsureSectorBuilt(sectorX, sectorY);

    public int? GetZoneIdAtTile(int tileX, int tileY) => _zoneGraph.GetZoneIdAtTile(tileX, tileY);

    public bool TryGetZone(int zoneId, out ZoneInfo zone) => _zoneGraph.TryGetZone(zoneId, out zone);

    public Fixed64 GetWallDistance(int tileX, int tileY) => _zoneGraph.GetWallDistance(tileX, tileY);

    public void InvalidateTile(int tileX, int tileY) => _zoneGraph.InvalidateTile(tileX, tileY);

    public void InvalidateAll() => _zoneGraph.InvalidateAll();

    public bool FindZonePath(int startZoneId, int endZoneId, List<int> outPath) =>
        ((IZoneGraph)_zoneGraph).FindZonePath(startZoneId, endZoneId, outPath);

    public bool TryGetPortal(int portalId, out PortalInfo portal) => _zoneGraph.TryGetPortal(portalId, out portal);

    public int GetZoneCount() => _zoneGraph.GetZoneCount();

    public int GetPortalCount() => _zoneGraph.GetPortalCount();

    public void GetPortalsForZone(int zoneId, List<int> portalIds) =>
        ((IZoneGraph)_zoneGraph).GetPortalsForZone(zoneId, portalIds);

    #endregion
}
