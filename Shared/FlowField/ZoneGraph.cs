using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Core;

namespace FlowField;

public class ZoneGraph : IZoneGraph
{
    private readonly IWorldProvider _worldProvider;

    private readonly ConcurrentDictionary<(int sectorX, int sectorY), ZoneSector> _sectors;
    private readonly Dictionary<int, SectorZone> _zonesById;
    private readonly Dictionary<int, ZonePortal> _portalsById;
    private readonly Dictionary<int, List<int>> _zonePortals;

    private readonly PriorityQueue<int, long> _astarOpenSet;  // Uses Fixed64.Raw for determinism
    private readonly Dictionary<int, int?> _astarCameFrom;
    private readonly Dictionary<int, Fixed64> _astarGScore;
    private readonly HashSet<int> _astarClosedSet;
    private readonly HashSet<(int, int)> _sectorsWithNeighborsBuilt;

    private readonly Queue<(int localX, int localY)> _floodFillQueue;
    private readonly List<int> _pathBuffer;
    private readonly List<int> _lastZonePath;
    private readonly Dictionary<int, List<int>> _recentPathsByStartZone;

    private readonly Fixed64[] _edtScratchGrid;
    private readonly Fixed64[] _edtScratchRow;
    private readonly Fixed64[] _edtOriginalValues;
    private readonly int[] _edtParabolaPositions;
    private readonly Fixed64[] _edtParabolaIntersections;

    private int _nextZoneId;
    private int _nextPortalId;

    private readonly object _sectorBuildLock = new object();

    public const int SectorSize = 32;
    private static readonly Fixed64 EdtInfinity = Fixed64.FromInt(10000000);

    public ZoneGraph(IWorldProvider worldProvider)
    {
        _worldProvider = worldProvider;

        _sectors = new ConcurrentDictionary<(int, int), ZoneSector>();
        _zonesById = new Dictionary<int, SectorZone>();
        _portalsById = new Dictionary<int, ZonePortal>();
        _zonePortals = new Dictionary<int, List<int>>();

        _astarOpenSet = new PriorityQueue<int, long>(64);
        _astarCameFrom = new Dictionary<int, int?>(64);
        _astarGScore = new Dictionary<int, Fixed64>(64);
        _astarClosedSet = new HashSet<int>(64);
        _sectorsWithNeighborsBuilt = new HashSet<(int, int)>(64);

        _floodFillQueue = new Queue<(int, int)>(SectorSize * SectorSize);
        _pathBuffer = new List<int>(32);
        _lastZonePath = new List<int>(32);
        _recentPathsByStartZone = new Dictionary<int, List<int>>(16);

        _edtScratchGrid = new Fixed64[SectorSize * SectorSize];
        _edtScratchRow = new Fixed64[SectorSize];
        _edtOriginalValues = new Fixed64[SectorSize];
        _edtParabolaPositions = new int[SectorSize + 1];
        _edtParabolaIntersections = new Fixed64[SectorSize + 1];

        _nextZoneId = 1;
        _nextPortalId = 1;
    }

    public void BuildSector(int sectorX, int sectorY)
    {
        lock (_sectorBuildLock)
        {
            EnsureSectorBuilt(sectorX, sectorY);
        }
    }

    private void FloodFillZoneUsingSDF(int[] tileZoneIndices, Fixed64[] wallDistances, int startLocalX, int startLocalY, int zoneIndex)
    {
        _floodFillQueue.Clear();
        _floodFillQueue.Enqueue((startLocalX, startLocalY));

        while (_floodFillQueue.Count > 0)
        {
            var (localX, localY) = _floodFillQueue.Dequeue();

            if (localX < 0 || localX >= SectorSize || localY < 0 || localY >= SectorSize)
            {
                continue;
            }

            int cellIndex = localX + localY * SectorSize;
            if (tileZoneIndices[cellIndex] >= 0)
            {
                continue;
            }

            if (wallDistances[cellIndex] == Fixed64.Zero)
            {
                continue;
            }

            tileZoneIndices[cellIndex] = zoneIndex;

            _floodFillQueue.Enqueue((localX - 1, localY));
            _floodFillQueue.Enqueue((localX + 1, localY));
            _floodFillQueue.Enqueue((localX, localY - 1));
            _floodFillQueue.Enqueue((localX, localY + 1));
        }
    }

    private void DetectPortalsForSector(int sectorX, int sectorY, ZoneSector sector, int tileSize)
    {
        DetectVerticalEdgePortals(sectorX, sectorY, sector, sector.MinTileX, tileSize, isLeftEdge: true);
        DetectVerticalEdgePortals(sectorX, sectorY, sector, sector.MaxTileX - 1, tileSize, isLeftEdge: false);
        DetectHorizontalEdgePortals(sectorX, sectorY, sector, sector.MinTileY, tileSize, isTopEdge: true);
        DetectHorizontalEdgePortals(sectorX, sectorY, sector, sector.MaxTileY - 1, tileSize, isTopEdge: false);
    }

    private void DetectVerticalEdgePortals(int sectorX, int sectorY, ZoneSector sector, int edgeTileX, int tileSize, bool isLeftEdge)
    {
        int neighborOffsetX = isLeftEdge ? -1 : 1;
        int neighborSectorX = sectorX + neighborOffsetX;

        if (!_sectors.TryGetValue((neighborSectorX, sectorY), out var neighborSector))
        {
            return;
        }

        bool spanOpen = false;
        int spanStartY = 0;
        int spanZoneIndex = -1;
        int spanNeighborZoneIndex = -1;

        for (int tileY = sector.MinTileY; tileY < sector.MaxTileY; tileY++)
        {
            int localX = edgeTileX - sector.MinTileX;
            int localY = tileY - sector.MinTileY;
            int cellIndex = localX + localY * SectorSize;

            int zoneIndex = sector.TileZoneIndices[cellIndex];
            if (zoneIndex < 0)
            {
                if (spanOpen)
                {
                    CreateVerticalPortal(sector, neighborSector, edgeTileX, spanStartY, tileY - 1, spanZoneIndex, spanNeighborZoneIndex, neighborSectorX, sectorY);
                    spanOpen = false;
                }
                continue;
            }

            int neighborTileX = edgeTileX + neighborOffsetX;
            int neighborLocalX = neighborTileX - neighborSector.MinTileX;
            int neighborLocalY = localY;

            if (neighborLocalX < 0 || neighborLocalX >= SectorSize)
            {
                if (spanOpen)
                {
                    CreateVerticalPortal(sector, neighborSector, edgeTileX, spanStartY, tileY - 1, spanZoneIndex, spanNeighborZoneIndex, neighborSectorX, sectorY);
                    spanOpen = false;
                }
                continue;
            }

            int neighborCellIndex = neighborLocalX + neighborLocalY * SectorSize;
            int neighborZoneIndex = neighborSector.TileZoneIndices[neighborCellIndex];

            if (neighborZoneIndex < 0)
            {
                if (spanOpen)
                {
                    CreateVerticalPortal(sector, neighborSector, edgeTileX, spanStartY, tileY - 1, spanZoneIndex, spanNeighborZoneIndex, neighborSectorX, sectorY);
                    spanOpen = false;
                }
                continue;
            }

            if (!spanOpen)
            {
                spanOpen = true;
                spanStartY = tileY;
                spanZoneIndex = zoneIndex;
                spanNeighborZoneIndex = neighborZoneIndex;
            }
            else if (zoneIndex != spanZoneIndex || neighborZoneIndex != spanNeighborZoneIndex)
            {
                CreateVerticalPortal(sector, neighborSector, edgeTileX, spanStartY, tileY - 1, spanZoneIndex, spanNeighborZoneIndex, neighborSectorX, sectorY);
                spanStartY = tileY;
                spanZoneIndex = zoneIndex;
                spanNeighborZoneIndex = neighborZoneIndex;
            }
        }

        if (spanOpen)
        {
            CreateVerticalPortal(sector, neighborSector, edgeTileX, spanStartY, sector.MaxTileY - 1, spanZoneIndex, spanNeighborZoneIndex, neighborSectorX, sectorY);
        }
    }

    private void CreateVerticalPortal(ZoneSector fromSector, ZoneSector toSector, int tileX, int startTileY, int endTileY, int fromZoneIndex, int toZoneIndex, int neighborSectorX, int sectorY)
    {
        if (fromZoneIndex < 0 || toZoneIndex < 0)
        {
            return;
        }

        if (fromZoneIndex >= fromSector.ZoneIds.Count || toZoneIndex >= toSector.ZoneIds.Count)
        {
            return;
        }

        int fromZoneId = fromSector.ZoneIds[fromZoneIndex];
        int toZoneId = toSector.ZoneIds[toZoneIndex];

        if (PortalAlreadyExists(fromZoneId, toZoneId, tileX, startTileY, tileX, endTileY))
        {
            return;
        }

        int portalId = _nextPortalId++;
        var portal = new ZonePortal
        {
            PortalId = portalId,
            StartTileX = tileX,
            StartTileY = startTileY,
            EndTileX = tileX,
            EndTileY = endTileY,
            FromZoneId = fromZoneId,
            ToZoneId = toZoneId
        };

        _portalsById[portalId] = portal;

        if (!_zonePortals.ContainsKey(fromZoneId))
        {
            _zonePortals[fromZoneId] = new List<int>();
        }
        _zonePortals[fromZoneId].Add(portalId);

        if (!_zonePortals.ContainsKey(toZoneId))
        {
            _zonePortals[toZoneId] = new List<int>();
        }
        _zonePortals[toZoneId].Add(portalId);
    }

    private void DetectHorizontalEdgePortals(int sectorX, int sectorY, ZoneSector sector, int edgeTileY, int tileSize, bool isTopEdge)
    {
        int neighborOffsetY = isTopEdge ? -1 : 1;
        int neighborSectorY = sectorY + neighborOffsetY;

        if (!_sectors.TryGetValue((sectorX, neighborSectorY), out var neighborSector))
        {
            return;
        }

        bool spanOpen = false;
        int spanStartX = 0;
        int spanZoneIndex = -1;
        int spanNeighborZoneIndex = -1;

        for (int tileX = sector.MinTileX; tileX < sector.MaxTileX; tileX++)
        {
            int localX = tileX - sector.MinTileX;
            int localY = edgeTileY - sector.MinTileY;
            int cellIndex = localX + localY * SectorSize;

            int zoneIndex = sector.TileZoneIndices[cellIndex];
            if (zoneIndex < 0)
            {
                if (spanOpen)
                {
                    CreateHorizontalPortal(sector, neighborSector, edgeTileY, spanStartX, tileX - 1, spanZoneIndex, spanNeighborZoneIndex, sectorX, neighborSectorY);
                    spanOpen = false;
                }
                continue;
            }

            int neighborTileY = edgeTileY + neighborOffsetY;
            int neighborLocalX = localX;
            int neighborLocalY = neighborTileY - neighborSector.MinTileY;

            if (neighborLocalY < 0 || neighborLocalY >= SectorSize)
            {
                if (spanOpen)
                {
                    CreateHorizontalPortal(sector, neighborSector, edgeTileY, spanStartX, tileX - 1, spanZoneIndex, spanNeighborZoneIndex, sectorX, neighborSectorY);
                    spanOpen = false;
                }
                continue;
            }

            int neighborCellIndex = neighborLocalX + neighborLocalY * SectorSize;
            int neighborZoneIndex = neighborSector.TileZoneIndices[neighborCellIndex];

            if (neighborZoneIndex < 0)
            {
                if (spanOpen)
                {
                    CreateHorizontalPortal(sector, neighborSector, edgeTileY, spanStartX, tileX - 1, spanZoneIndex, spanNeighborZoneIndex, sectorX, neighborSectorY);
                    spanOpen = false;
                }
                continue;
            }

            if (!spanOpen)
            {
                spanOpen = true;
                spanStartX = tileX;
                spanZoneIndex = zoneIndex;
                spanNeighborZoneIndex = neighborZoneIndex;
            }
            else if (zoneIndex != spanZoneIndex || neighborZoneIndex != spanNeighborZoneIndex)
            {
                CreateHorizontalPortal(sector, neighborSector, edgeTileY, spanStartX, tileX - 1, spanZoneIndex, spanNeighborZoneIndex, sectorX, neighborSectorY);
                spanStartX = tileX;
                spanZoneIndex = zoneIndex;
                spanNeighborZoneIndex = neighborZoneIndex;
            }
        }

        if (spanOpen)
        {
            CreateHorizontalPortal(sector, neighborSector, edgeTileY, spanStartX, sector.MaxTileX - 1, spanZoneIndex, spanNeighborZoneIndex, sectorX, neighborSectorY);
        }
    }

    private void CreateHorizontalPortal(ZoneSector fromSector, ZoneSector toSector, int tileY, int startTileX, int endTileX, int fromZoneIndex, int toZoneIndex, int sectorX, int neighborSectorY)
    {
        if (fromZoneIndex < 0 || toZoneIndex < 0)
        {
            return;
        }

        if (fromZoneIndex >= fromSector.ZoneIds.Count || toZoneIndex >= toSector.ZoneIds.Count)
        {
            return;
        }

        int fromZoneId = fromSector.ZoneIds[fromZoneIndex];
        int toZoneId = toSector.ZoneIds[toZoneIndex];

        if (PortalAlreadyExists(fromZoneId, toZoneId, startTileX, tileY, endTileX, tileY))
        {
            return;
        }

        int portalId = _nextPortalId++;
        var portal = new ZonePortal
        {
            PortalId = portalId,
            StartTileX = startTileX,
            StartTileY = tileY,
            EndTileX = endTileX,
            EndTileY = tileY,
            FromZoneId = fromZoneId,
            ToZoneId = toZoneId
        };

        _portalsById[portalId] = portal;

        if (!_zonePortals.ContainsKey(fromZoneId))
        {
            _zonePortals[fromZoneId] = new List<int>();
        }
        _zonePortals[fromZoneId].Add(portalId);

        if (!_zonePortals.ContainsKey(toZoneId))
        {
            _zonePortals[toZoneId] = new List<int>();
        }
        _zonePortals[toZoneId].Add(portalId);
    }

    private bool PortalAlreadyExists(int fromZoneId, int toZoneId, int startTileX, int startTileY, int endTileX, int endTileY)
    {
        if (!_zonePortals.TryGetValue(fromZoneId, out var portalIds))
        {
            return false;
        }

        for (int portalIndex = 0; portalIndex < portalIds.Count; portalIndex++)
        {
            int portalId = portalIds[portalIndex];
            if (!_portalsById.TryGetValue(portalId, out var existing))
            {
                continue;
            }

            bool matchesForward = existing.FromZoneId == fromZoneId && existing.ToZoneId == toZoneId;
            bool matchesReverse = existing.FromZoneId == toZoneId && existing.ToZoneId == fromZoneId;

            if ((matchesForward || matchesReverse) &&
                existing.StartTileX == startTileX &&
                existing.StartTileY == startTileY &&
                existing.EndTileX == endTileX &&
                existing.EndTileY == endTileY)
            {
                return true;
            }
        }

        return false;
    }

    public void InvalidateSector(int sectorX, int sectorY)
    {
        lock (_sectorBuildLock)
        {
            InvalidateSectorInternal(sectorX, sectorY);
            InvalidateSectorInternal(sectorX - 1, sectorY);
            InvalidateSectorInternal(sectorX + 1, sectorY);
            InvalidateSectorInternal(sectorX, sectorY - 1);
            InvalidateSectorInternal(sectorX, sectorY + 1);

            EnsureSectorBuilt(sectorX, sectorY);
        }
    }

    /// <summary>
    /// Clears all built sectors, forcing them to rebuild with current world data.
    /// Call this after terrain generation or major world changes.
    /// </summary>
    public void InvalidateAllSectors()
    {
        lock (_sectorBuildLock)
        {
            // Clear all portals and zone data
            _portalsById.Clear();
            _zonePortals.Clear();
            _sectors.Clear();
            _zonesById.Clear();
            _sectorsWithNeighborsBuilt.Clear();  // Clear neighbor build tracking
            _nextZoneId = 1;  // Reset to 1 to match constructor initialization
            _nextPortalId = 1;  // Reset to 1 to match constructor initialization
            LastPathfindBuiltSectors = false;  // Reset pathfind flag
        }
    }

    /// <summary>
    /// IZoneGraph interface method - invalidates all sectors.
    /// </summary>
    public void InvalidateAll() => InvalidateAllSectors();

    private void InvalidateSectorInternal(int sectorX, int sectorY)
    {
        if (!_sectors.TryGetValue((sectorX, sectorY), out var sector))
        {
            return;
        }

        if (sector.ZoneIds == null)
        {
            _sectors.TryRemove((sectorX, sectorY), out _);
            return;
        }

        for (int zoneIdIndex = 0; zoneIdIndex < sector.ZoneIds.Count; zoneIdIndex++)
        {
            int zoneId = sector.ZoneIds[zoneIdIndex];

            if (_zonePortals.TryGetValue(zoneId, out var portalIds))
            {
                for (int portalIndex = 0; portalIndex < portalIds.Count; portalIndex++)
                {
                    int portalId = portalIds[portalIndex];
                    if (_portalsById.TryGetValue(portalId, out var portal))
                    {
                        int otherZoneId = portal.FromZoneId == zoneId ? portal.ToZoneId : portal.FromZoneId;
                        if (_zonePortals.TryGetValue(otherZoneId, out var otherPortalIds))
                        {
                            otherPortalIds.Remove(portalId);
                        }
                        _portalsById.Remove(portalId);
                    }
                }
                _zonePortals.Remove(zoneId);
            }

            _zonesById.Remove(zoneId);
        }

        _sectors.TryRemove((sectorX, sectorY), out _);
    }

    public void EnsureSectorBuilt(int sectorX, int sectorY)
    {
        if (_sectors.ContainsKey((sectorX, sectorY)))
        {
            return;
        }

        // Build zones for this sector and all 4 neighbors first (without portals)
        BuildSectorZonesOnly(sectorX, sectorY);
        BuildSectorZonesOnly(sectorX - 1, sectorY);
        BuildSectorZonesOnly(sectorX + 1, sectorY);
        BuildSectorZonesOnly(sectorX, sectorY - 1);
        BuildSectorZonesOnly(sectorX, sectorY + 1);

        // Detect portals for ALL 5 sectors so they can connect to their neighbors
        int tileSize = _worldProvider.TileSize;
        DetectPortalsForSectorIfExists(sectorX, sectorY, tileSize);
        DetectPortalsForSectorIfExists(sectorX - 1, sectorY, tileSize);
        DetectPortalsForSectorIfExists(sectorX + 1, sectorY, tileSize);
        DetectPortalsForSectorIfExists(sectorX, sectorY - 1, tileSize);
        DetectPortalsForSectorIfExists(sectorX, sectorY + 1, tileSize);
    }

    private void DetectPortalsForSectorIfExists(int sectorX, int sectorY, int tileSize)
    {
        if (_sectors.TryGetValue((sectorX, sectorY), out var sector))
        {
            DetectPortalsForSector(sectorX, sectorY, sector, tileSize);
        }
    }

    private void BuildSectorZonesOnly(int sectorX, int sectorY)
    {
        if (_sectors.ContainsKey((sectorX, sectorY)))
        {
            return;
        }

        int minTileX = sectorX * SectorSize;
        int minTileY = sectorY * SectorSize;
        int maxTileX = minTileX + SectorSize;
        int maxTileY = minTileY + SectorSize;
        int tileSize = _worldProvider.TileSize;

        Fixed64[] wallDistances = new Fixed64[SectorSize * SectorSize];
        ComputeWallDistanceFieldFromStructures(minTileX, minTileY, tileSize, wallDistances);

        int[] tileZoneIndices = new int[SectorSize * SectorSize];
        for (int cellIndex = 0; cellIndex < tileZoneIndices.Length; cellIndex++)
        {
            tileZoneIndices[cellIndex] = -1;
        }

        var zoneIds = new List<int>();
        int zoneIndex = 0;

        for (int localY = 0; localY < SectorSize; localY++)
        {
            for (int localX = 0; localX < SectorSize; localX++)
            {
                int cellIndex = localX + localY * SectorSize;
                if (tileZoneIndices[cellIndex] >= 0)
                {
                    continue;
                }

                if (wallDistances[cellIndex] == Fixed64.Zero)
                {
                    continue;
                }

                int newZoneId = _nextZoneId++;
                var newZone = new SectorZone
                {
                    ZoneId = newZoneId,
                    SectorX = sectorX,
                    SectorY = sectorY,
                    ZoneIndex = zoneIndex
                };

                _zonesById[newZoneId] = newZone;
                _zonePortals[newZoneId] = new List<int>();
                zoneIds.Add(newZoneId);

                FloodFillZoneUsingSDF(tileZoneIndices, wallDistances, localX, localY, zoneIndex);
                zoneIndex++;
            }
        }

        var sector = new ZoneSector
        {
            SectorX = sectorX,
            SectorY = sectorY,
            MinTileX = minTileX,
            MinTileY = minTileY,
            MaxTileX = maxTileX,
            MaxTileY = maxTileY,
            ZoneIds = zoneIds,
            TileZoneIndices = tileZoneIndices,
            WallDistances = wallDistances
        };

        _sectors[(sectorX, sectorY)] = sector;
    }

    public (int sectorX, int sectorY) TileToSector(int tileX, int tileY)
    {
        // Deterministic floor division (rounds toward negative infinity)
        int sectorX = tileX >= 0 ? tileX / SectorSize : (tileX - SectorSize + 1) / SectorSize;
        int sectorY = tileY >= 0 ? tileY / SectorSize : (tileY - SectorSize + 1) / SectorSize;
        return (sectorX, sectorY);
    }

    public void InvalidateTile(int tileX, int tileY)
    {
        var (sectorX, sectorY) = TileToSector(tileX, tileY);
        InvalidateSector(sectorX, sectorY);
    }

    public int? GetZoneIdAtTile(int tileX, int tileY)
    {
        var (sectorX, sectorY) = TileToSector(tileX, tileY);

        if (!_sectors.TryGetValue((sectorX, sectorY), out var sector))
        {
            lock (_sectorBuildLock)
            {
                EnsureSectorBuilt(sectorX, sectorY);
            }

            if (!_sectors.TryGetValue((sectorX, sectorY), out sector))
            {
                return null;
            }
        }

        int localX = tileX - sector.MinTileX;
        int localY = tileY - sector.MinTileY;

        if (localX < 0 || localX >= SectorSize || localY < 0 || localY >= SectorSize)
        {
            return null;
        }

        int cellIndex = localX + localY * SectorSize;
        int zoneIndex = sector.TileZoneIndices[cellIndex];

        if (zoneIndex < 0 || zoneIndex >= sector.ZoneIds.Count)
        {
            return null;
        }

        return sector.ZoneIds[zoneIndex];
    }

    public SectorZone? GetZoneById(int zoneId)
    {
        if (_zonesById.TryGetValue(zoneId, out var zone))
        {
            return zone;
        }
        return null;
    }

    /// <summary>
    /// IZoneGraph interface method - tries to get zone info by ID.
    /// </summary>
    public bool TryGetZone(int zoneId, out ZoneInfo zone)
    {
        if (!_zonesById.TryGetValue(zoneId, out var sectorZone))
        {
            zone = default;
            return false;
        }

        var center = GetZoneCenter(zoneId);
        if (!center.HasValue)
        {
            zone = default;
            return false;
        }

        // Count tiles in zone
        int tileCount = 0;
        if (_sectors.TryGetValue((sectorZone.SectorX, sectorZone.SectorY), out var sector))
        {
            for (int i = 0; i < SectorSize * SectorSize; i++)
            {
                if (sector.TileZoneIndices[i] == sectorZone.ZoneIndex)
                {
                    tileCount++;
                }
            }
        }

        zone = new ZoneInfo(
            zoneId,
            sectorZone.SectorX,
            sectorZone.SectorY,
            center.Value.centerTileX,
            center.Value.centerTileY,
            tileCount);
        return true;
    }

    public ZoneSector? GetSector(int sectorX, int sectorY)
    {
        if (_sectors.TryGetValue((sectorX, sectorY), out var sector))
        {
            return sector;
        }
        return null;
    }

    public Fixed64 GetWallDistance(int tileX, int tileY)
    {
        var (sectorX, sectorY) = TileToSector(tileX, tileY);

        if (!_sectors.TryGetValue((sectorX, sectorY), out var sector))
        {
            return Fixed64.Zero;
        }

        if (sector.WallDistances == null)
        {
            return Fixed64.Zero;
        }

        int localX = tileX - sector.MinTileX;
        int localY = tileY - sector.MinTileY;

        if (localX < 0 || localX >= SectorSize || localY < 0 || localY >= SectorSize)
        {
            return Fixed64.Zero;
        }

        int cellIndex = localX + localY * SectorSize;
        return sector.WallDistances[cellIndex];
    }

    public ZonePortal? GetPortalById(int portalId)
    {
        if (_portalsById.TryGetValue(portalId, out var portal))
        {
            return portal;
        }
        return null;
    }

    /// <summary>
    /// IZoneGraph interface method - tries to get portal info by ID.
    /// </summary>
    public bool TryGetPortal(int portalId, out PortalInfo portal)
    {
        if (!_portalsById.TryGetValue(portalId, out var zonePortal))
        {
            portal = default;
            return false;
        }

        portal = new PortalInfo(
            zonePortal.PortalId,
            zonePortal.FromZoneId,
            zonePortal.ToZoneId,
            zonePortal.StartTileX,
            zonePortal.StartTileY,
            zonePortal.EndTileX,
            zonePortal.EndTileY,
            Fixed64.OneValue);  // Default traversal cost
        return true;
    }

    public List<int>? GetPortalsForZone(int zoneId)
    {
        if (_zonePortals.TryGetValue(zoneId, out var portalIds))
        {
            return portalIds;
        }
        return null;
    }

    /// <summary>
    /// IZoneGraph interface method - gets portals connected to a zone.
    /// </summary>
    void IZoneGraph.GetPortalsForZone(int zoneId, List<int> portalIds)
    {
        portalIds.Clear();
        if (_zonePortals.TryGetValue(zoneId, out var ids))
        {
            portalIds.AddRange(ids);
        }
    }

    public bool LastPathfindBuiltSectors { get; private set; }

    /// <summary>
    /// IZoneGraph interface method - finds a path between zones using A*.
    /// </summary>
    bool IZoneGraph.FindZonePath(int startZoneId, int endZoneId, List<int> outPath)
    {
        outPath.Clear();
        var result = FindZonePath(startZoneId, endZoneId);
        if (result == null)
        {
            return false;
        }
        outPath.AddRange(result);
        return true;
    }

    /// <summary>
    /// Finds a path of zones from start to destination, using zone center as destination estimate.
    /// </summary>
    public List<int>? FindZonePath(int startZoneId, int destZoneId)
    {
        // Try to get zone center; if unavailable, fall back to sector center estimate
        var destCenter = GetZoneCenter(destZoneId);
        if (destCenter.HasValue)
        {
            return FindZonePath(startZoneId, destZoneId, destCenter.Value.centerTileX, destCenter.Value.centerTileY);
        }

        // Fallback: use sector center if zone center unavailable (e.g., sector not fully built)
        if (_zonesById.TryGetValue(destZoneId, out var destZone))
        {
            int sectorCenterX = destZone.SectorX * SectorSize + SectorSize / 2;
            int sectorCenterY = destZone.SectorY * SectorSize + SectorSize / 2;
            return FindZonePath(startZoneId, destZoneId, sectorCenterX, sectorCenterY);
        }

        return null;
    }

    /// <summary>
    /// Finds a path of zones from start to destination with portal-aware costing.
    /// Uses actual destination tile for accurate portal distance estimation.
    /// </summary>
    public List<int>? FindZonePath(int startZoneId, int destZoneId, int destTileX, int destTileY)
    {
        LastPathfindBuiltSectors = false;

        if (startZoneId == destZoneId)
        {
            _pathBuffer.Clear();
            _pathBuffer.Add(startZoneId);
            return _pathBuffer;
        }

        if (!_zonesById.TryGetValue(startZoneId, out _) || !_zonesById.TryGetValue(destZoneId, out var destZone))
        {
            return null;
        }

        _astarOpenSet.Clear();
        _astarCameFrom.Clear();
        _astarGScore.Clear();
        _astarClosedSet.Clear();
        _sectorsWithNeighborsBuilt.Clear();

        _astarGScore[startZoneId] = Fixed64.Zero;
        Fixed64 heuristic = EstimateZoneDistance(startZoneId, destZoneId);
        _astarOpenSet.Enqueue(startZoneId, heuristic.Raw);
        _astarCameFrom[startZoneId] = null;

        const int maxIterations = 10000;
        int iterations = 0;

        while (_astarOpenSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            int currentZoneId = _astarOpenSet.Dequeue();

            if (_astarClosedSet.Contains(currentZoneId))
            {
                continue;
            }
            _astarClosedSet.Add(currentZoneId);

            if (currentZoneId == destZoneId)
            {
                LastPathfindBuiltSectors = _sectorsWithNeighborsBuilt.Count > 0;
                var path = ReconstructPath(currentZoneId);
                _lastZonePath.Clear();
                _lastZonePath.AddRange(path);
                StorePathByStartZone(startZoneId, path);
                return path;
            }

            if (!_zonesById.TryGetValue(currentZoneId, out var currentZone))
            {
                continue;
            }
            BuildNeighborSectorsOnDemand(currentZone.SectorX, currentZone.SectorY);

            if (!_zonePortals.TryGetValue(currentZoneId, out var portalIds) || portalIds.Count == 0)
            {
                continue;
            }

            for (int portalIndex = 0; portalIndex < portalIds.Count; portalIndex++)
            {
                int portalId = portalIds[portalIndex];
                if (!_portalsById.TryGetValue(portalId, out var portal))
                {
                    continue;
                }

                int neighborZoneId = portal.FromZoneId == currentZoneId ? portal.ToZoneId : portal.FromZoneId;

                if (_astarClosedSet.Contains(neighborZoneId))
                {
                    continue;
                }

                // Use portal-aware cost instead of fixed cost=1
                Fixed64 portalCost = EstimatePortalTraversalCost(currentZoneId, portal, destTileX, destTileY);
                Fixed64 tentativeG = _astarGScore.GetValueOrDefault(currentZoneId, Fixed64.MaxValue) + portalCost;

                if (tentativeG < _astarGScore.GetValueOrDefault(neighborZoneId, Fixed64.MaxValue))
                {
                    _astarCameFrom[neighborZoneId] = currentZoneId;
                    _astarGScore[neighborZoneId] = tentativeG;
                    Fixed64 h = EstimateZoneDistance(neighborZoneId, destZoneId);
                    _astarOpenSet.Enqueue(neighborZoneId, (tentativeG + h).Raw);
                }
            }
        }

        LastPathfindBuiltSectors = _sectorsWithNeighborsBuilt.Count > 0;
        return null;
    }

    public ZonePortal? FindPortalBetweenZones(int fromZoneId, int toZoneId)
    {
        if (!_zonePortals.TryGetValue(fromZoneId, out var portalIds))
        {
            return null;
        }

        for (int portalIndex = 0; portalIndex < portalIds.Count; portalIndex++)
        {
            int portalId = portalIds[portalIndex];
            if (!_portalsById.TryGetValue(portalId, out var portal))
            {
                continue;
            }

            if ((portal.FromZoneId == fromZoneId && portal.ToZoneId == toZoneId) ||
                (portal.FromZoneId == toZoneId && portal.ToZoneId == fromZoneId))
            {
                return portal;
            }
        }

        return null;
    }

    private readonly List<ZonePortal> _portalsBetweenZonesBuffer = new(8);

    /// <summary>
    /// Finds ALL portals between two zones (for when there are multiple gaps/passages).
    /// </summary>
    public List<ZonePortal> FindAllPortalsBetweenZones(int fromZoneId, int toZoneId)
    {
        _portalsBetweenZonesBuffer.Clear();

        if (!_zonePortals.TryGetValue(fromZoneId, out var portalIds))
        {
            return _portalsBetweenZonesBuffer;
        }

        for (int portalIndex = 0; portalIndex < portalIds.Count; portalIndex++)
        {
            int portalId = portalIds[portalIndex];
            if (!_portalsById.TryGetValue(portalId, out var portal))
            {
                continue;
            }

            if ((portal.FromZoneId == fromZoneId && portal.ToZoneId == toZoneId) ||
                (portal.FromZoneId == toZoneId && portal.ToZoneId == fromZoneId))
            {
                _portalsBetweenZonesBuffer.Add(portal);
            }
        }

        return _portalsBetweenZonesBuffer;
    }

    private Fixed64 EstimateZoneDistance(int fromZoneId, int toZoneId)
    {
        if (!_zonesById.TryGetValue(fromZoneId, out var fromZone) ||
            !_zonesById.TryGetValue(toZoneId, out var toZone))
        {
            return Fixed64.MaxValue;
        }

        // Manhattan distance using integer math (sector coordinates are always integers)
        int dx = Math.Abs(fromZone.SectorX - toZone.SectorX);
        int dy = Math.Abs(fromZone.SectorY - toZone.SectorY);
        return Fixed64.FromInt(dx + dy);
    }

    private List<int> ReconstructPath(int currentZoneId)
    {
        _pathBuffer.Clear();
        int? node = currentZoneId;
        int safetyLimit = 1000;
        int iterations = 0;

        while (node.HasValue && iterations < safetyLimit)
        {
            iterations++;
            _pathBuffer.Add(node.Value);
            node = _astarCameFrom.GetValueOrDefault(node.Value);
        }

        _pathBuffer.Reverse();
        return _pathBuffer;
    }

    private bool IsTileBlocked(int tileX, int tileY, int tileSize)
    {
        // Compute world position at tile center using Fixed64
        Fixed64 halfTileSize = Fixed64.FromInt(tileSize) / Fixed64.FromInt(2);
        Fixed64 worldX = Fixed64.FromInt(tileX * tileSize) + halfTileSize;
        Fixed64 worldY = Fixed64.FromInt(tileY * tileSize) + halfTileSize;
        return _worldProvider.IsBlocked(worldX, worldY);
    }

    private void ComputeWallDistanceFieldFromStructures(int minTileX, int minTileY, int tileSize, Fixed64[] wallDistances)
    {
        // Initialize grid: blocked cells = 0, open cells = infinity
        for (int localY = 0; localY < SectorSize; localY++)
        {
            for (int localX = 0; localX < SectorSize; localX++)
            {
                int cellIndex = localX + localY * SectorSize;
                int tileX = minTileX + localX;
                int tileY = minTileY + localY;

                bool blocked = IsTileBlocked(tileX, tileY, tileSize);
                _edtScratchGrid[cellIndex] = blocked ? Fixed64.Zero : EdtInfinity;
            }
        }

        // Transform rows (horizontal pass)
        for (int rowIndex = 0; rowIndex < SectorSize; rowIndex++)
        {
            int rowStart = rowIndex * SectorSize;
            for (int columnIndex = 0; columnIndex < SectorSize; columnIndex++)
            {
                _edtScratchRow[columnIndex] = _edtScratchGrid[rowStart + columnIndex];
            }

            TransformRow1D(_edtScratchRow, SectorSize);

            for (int columnIndex = 0; columnIndex < SectorSize; columnIndex++)
            {
                _edtScratchGrid[rowStart + columnIndex] = _edtScratchRow[columnIndex];
            }
        }

        // Transform columns (vertical pass)
        for (int columnIndex = 0; columnIndex < SectorSize; columnIndex++)
        {
            for (int rowIndex = 0; rowIndex < SectorSize; rowIndex++)
            {
                _edtScratchRow[rowIndex] = _edtScratchGrid[rowIndex * SectorSize + columnIndex];
            }

            TransformRow1D(_edtScratchRow, SectorSize);

            for (int rowIndex = 0; rowIndex < SectorSize; rowIndex++)
            {
                _edtScratchGrid[rowIndex * SectorSize + columnIndex] = _edtScratchRow[rowIndex];
            }
        }

        // Convert squared distances to actual distances using Fixed64.Sqrt
        for (int cellIndex = 0; cellIndex < SectorSize * SectorSize; cellIndex++)
        {
            Fixed64 squaredDistance = _edtScratchGrid[cellIndex];
            wallDistances[cellIndex] = Fixed64.Sqrt(squaredDistance);
        }
    }

    private void TransformRow1D(Fixed64[] values, int length)
    {
        // Copy input values
        for (int copyIndex = 0; copyIndex < length; copyIndex++)
        {
            _edtOriginalValues[copyIndex] = values[copyIndex];
        }

        int parabolaCount = 0;
        _edtParabolaPositions[0] = 0;
        _edtParabolaIntersections[0] = Fixed64.MinValue;  // Negative infinity
        _edtParabolaIntersections[1] = Fixed64.MaxValue;  // Positive infinity

        for (int currentPosition = 1; currentPosition < length; currentPosition++)
        {
            Fixed64 currentValue = _edtOriginalValues[currentPosition];

            while (parabolaCount >= 0)
            {
                int lastPosition = _edtParabolaPositions[parabolaCount];
                Fixed64 lastValue = _edtOriginalValues[lastPosition];

                Fixed64 intersection = ComputeParabolaIntersection(lastPosition, lastValue, currentPosition, currentValue);

                if (intersection <= _edtParabolaIntersections[parabolaCount])
                {
                    parabolaCount--;
                }
                else
                {
                    break;
                }
            }

            parabolaCount++;
            _edtParabolaPositions[parabolaCount] = currentPosition;
            _edtParabolaIntersections[parabolaCount] = ComputeParabolaIntersection(
                _edtParabolaPositions[parabolaCount - 1],
                _edtOriginalValues[_edtParabolaPositions[parabolaCount - 1]],
                currentPosition,
                currentValue);
            _edtParabolaIntersections[parabolaCount + 1] = Fixed64.MaxValue;
        }

        int activeParabola = 0;
        for (int outputPosition = 0; outputPosition < length; outputPosition++)
        {
            Fixed64 outputPosFixed = Fixed64.FromInt(outputPosition);
            while (_edtParabolaIntersections[activeParabola + 1] < outputPosFixed)
            {
                activeParabola++;
            }

            int sourcePosition = _edtParabolaPositions[activeParabola];
            Fixed64 delta = Fixed64.FromInt(outputPosition - sourcePosition);
            values[outputPosition] = delta * delta + _edtOriginalValues[sourcePosition];
        }
    }

    private static Fixed64 ComputeParabolaIntersection(int position1, Fixed64 value1, int position2, Fixed64 value2)
    {
        // Formula: ((v2 + p2²) - (v1 + p1²)) / (2 * (p2 - p1))
        Fixed64 p1 = Fixed64.FromInt(position1);
        Fixed64 p2 = Fixed64.FromInt(position2);
        Fixed64 numerator = (value2 + p2 * p2) - (value1 + p1 * p1);
        Fixed64 denominator = Fixed64.FromInt(2 * (position2 - position1));
        return numerator / denominator;
    }

    public int GetZoneCount()
    {
        return _zonesById.Count;
    }

    public int GetPortalCount()
    {
        return _portalsById.Count;
    }

    public IEnumerable<ZonePortal> AllPortals => _portalsById.Values;

    public int GetSectorCount()
    {
        return _sectors.Count;
    }

    public IReadOnlyList<int> GetLastZonePath()
    {
        return _lastZonePath;
    }

    public IReadOnlyDictionary<int, List<int>> GetRecentPathsByStartZone()
    {
        return _recentPathsByStartZone;
    }

    public void ClearRecentPaths()
    {
        _recentPathsByStartZone.Clear();
    }

    private void StorePathByStartZone(int startZoneId, List<int> path)
    {
        if (!_recentPathsByStartZone.TryGetValue(startZoneId, out var storedPath))
        {
            storedPath = new List<int>(path.Count);
            _recentPathsByStartZone[startZoneId] = storedPath;
        }
        storedPath.Clear();
        storedPath.AddRange(path);
    }

    public (int centerTileX, int centerTileY)? GetZoneCenter(int zoneId)
    {
        if (!_zonesById.TryGetValue(zoneId, out var zone))
        {
            return null;
        }

        if (!_sectors.TryGetValue((zone.SectorX, zone.SectorY), out var sector))
        {
            return null;
        }

        int sumX = 0;
        int sumY = 0;
        int tileCount = 0;

        for (int localY = 0; localY < SectorSize; localY++)
        {
            for (int localX = 0; localX < SectorSize; localX++)
            {
                int cellIndex = localX + localY * SectorSize;
                if (sector.TileZoneIndices[cellIndex] == zone.ZoneIndex)
                {
                    sumX += sector.MinTileX + localX;
                    sumY += sector.MinTileY + localY;
                    tileCount++;
                }
            }
        }

        if (tileCount == 0)
        {
            return null;
        }

        return (sumX / tileCount, sumY / tileCount);
    }

    /// <summary>
    /// Estimates the cost to traverse from a zone through a portal.
    /// Prefers portals that are closer to the zone center (reachability) and
    /// adds a small bonus for portals closer to the destination.
    /// </summary>
    private Fixed64 EstimatePortalTraversalCost(
        int fromZoneId,
        ZonePortal portal,
        int destTileX,
        int destTileY)
    {
        int portalCenterX = portal.CenterTileX;
        int portalCenterY = portal.CenterTileY;

        // Get zone center, fall back to portal center if unavailable
        var fromCenter = GetZoneCenter(fromZoneId);
        int fromX = fromCenter?.centerTileX ?? portalCenterX;
        int fromY = fromCenter?.centerTileY ?? portalCenterY;

        // Manhattan distance from zone center to portal (primary cost)
        int toPortal = Math.Abs(fromX - portalCenterX)
                     + Math.Abs(fromY - portalCenterY);

        // Small bonus for portals closer to destination (tie-breaker, scaled to avoid dominating)
        // This helps prefer the "right" portal when multiple portals are equidistant from zone center
        int portalToDestDiffX = Math.Abs(portalCenterX - destTileX);
        int portalToDestDiffY = Math.Abs(portalCenterY - destTileY);
        int destProximityBonus = (portalToDestDiffX + portalToDestDiffY) / SectorSize;

        // Cost = distance to reach portal + 1 for crossing + small dest proximity bonus
        return Fixed64.FromInt(toPortal + 1 + destProximityBonus);
    }

    private void BuildNeighborSectorsOnDemand(int sectorX, int sectorY)
    {
        if (_sectorsWithNeighborsBuilt.Contains((sectorX, sectorY)))
        {
            return;
        }
        _sectorsWithNeighborsBuilt.Add((sectorX, sectorY));

        lock (_sectorBuildLock)
        {
            int tileSize = _worldProvider.TileSize;

            BuildSectorZonesOnly(sectorX - 1, sectorY);
            BuildSectorZonesOnly(sectorX + 1, sectorY);
            BuildSectorZonesOnly(sectorX, sectorY - 1);
            BuildSectorZonesOnly(sectorX, sectorY + 1);

            if (_sectors.TryGetValue((sectorX, sectorY), out var sector))
            {
                DetectPortalsForSector(sectorX, sectorY, sector, tileSize);
            }

            DetectPortalsForSectorIfExists(sectorX - 1, sectorY, tileSize);
            DetectPortalsForSectorIfExists(sectorX + 1, sectorY, tileSize);
            DetectPortalsForSectorIfExists(sectorX, sectorY - 1, tileSize);
            DetectPortalsForSectorIfExists(sectorX, sectorY + 1, tileSize);
        }
    }
}

