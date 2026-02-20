using Core;

namespace FlowField;

/// <summary>
/// Interface for zone graph operations - hierarchical pathfinding structure.
/// </summary>
public interface IZoneGraph
{
    /// <summary>
    /// Ensures sector is built (zones, portals computed).
    /// </summary>
    void EnsureSectorBuilt(int sectorX, int sectorY);

    /// <summary>
    /// Gets the zone ID at a specific tile, or null if unpassable.
    /// </summary>
    int? GetZoneIdAtTile(int tileX, int tileY);

    /// <summary>
    /// Tries to get zone info by its ID.
    /// </summary>
    bool TryGetZone(int zoneId, out ZoneInfo zone);

    /// <summary>
    /// Gets wall distance at a tile (for flow field wall avoidance).
    /// </summary>
    Fixed64 GetWallDistance(int tileX, int tileY);

    /// <summary>
    /// Invalidates a single tile (e.g., building placed/destroyed).
    /// </summary>
    void InvalidateTile(int tileX, int tileY);

    /// <summary>
    /// Invalidates all sectors (full rebuild).
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Finds a path between zones using A*.
    /// </summary>
    bool FindZonePath(int startZoneId, int endZoneId, List<int> outPath);

    /// <summary>
    /// Gets portal info for seeding flows between zones.
    /// </summary>
    bool TryGetPortal(int portalId, out PortalInfo portal);

    /// <summary>
    /// Gets the number of zones in the graph.
    /// </summary>
    int GetZoneCount();

    /// <summary>
    /// Gets the number of portals in the graph.
    /// </summary>
    int GetPortalCount();

    /// <summary>
    /// Gets portals connected to a zone.
    /// </summary>
    void GetPortalsForZone(int zoneId, List<int> portalIds);
}

/// <summary>
/// Lightweight struct for zone information.
/// </summary>
public readonly struct ZoneInfo
{
    public readonly int ZoneId;
    public readonly int SectorX;
    public readonly int SectorY;
    public readonly int CenterTileX;
    public readonly int CenterTileY;
    public readonly int TileCount;

    public ZoneInfo(int zoneId, int sectorX, int sectorY, int centerTileX, int centerTileY, int tileCount)
    {
        ZoneId = zoneId;
        SectorX = sectorX;
        SectorY = sectorY;
        CenterTileX = centerTileX;
        CenterTileY = centerTileY;
        TileCount = tileCount;
    }
}

/// <summary>
/// Lightweight struct for portal information.
/// </summary>
public readonly struct PortalInfo
{
    public readonly int PortalId;
    public readonly int FromZoneId;
    public readonly int ToZoneId;
    public readonly int StartTileX;
    public readonly int StartTileY;
    public readonly int EndTileX;
    public readonly int EndTileY;
    public readonly Fixed64 Cost;

    public PortalInfo(int portalId, int fromZoneId, int toZoneId, int startTileX, int startTileY, int endTileX, int endTileY, Fixed64 cost)
    {
        PortalId = portalId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
        StartTileX = startTileX;
        StartTileY = startTileY;
        EndTileX = endTileX;
        EndTileY = endTileY;
        Cost = cost;
    }
}
