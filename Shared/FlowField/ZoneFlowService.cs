using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Core;
using Pooled.Runtime;

namespace FlowField;

/// <summary>
/// Slot in an array-based doubly-linked list for LRU tracking.
/// Uses indices instead of references to avoid allocations.
/// </summary>
internal struct LruSlot
{
    public int Prev;  // -1 for none
    public int Next;  // -1 for none
}

public class ZoneFlowService : IZoneFlowService
{
    private readonly ZoneGraph _zoneGraph;
    private readonly IWorldProvider _worldProvider;
    private readonly IPoolRegistry _poolRegistry;

    private readonly ConcurrentDictionary<int, ZoneFlowDataHandle> _multiTargetFlowCache;
    private readonly LruSlot[] _multiTargetLruSlots;
    private readonly int[] _multiTargetSlotToZone;  // slot -> zoneId (for eviction)
    private readonly Dictionary<int, int> _multiTargetZoneToSlot;  // zoneId -> slot index
    private int _multiTargetLruHead;  // -1 for empty
    private int _multiTargetLruTail;  // -1 for empty
    private int _multiTargetLruFreeHead;  // head of free slot list

    private readonly ConcurrentDictionary<(int zoneId, int destTileX, int destTileY, bool ignoreBuildings), ZoneFlowDataHandle> _singleDestFlowCache;
    private readonly LruSlot[] _singleDestLruSlots;
    private readonly Dictionary<(int, int, int, bool), int> _singleDestKeyToSlot;  // key -> slot index
    private readonly (int, int, int, bool)[] _singleDestSlotToKey;  // slot -> key (for eviction)
    private int _singleDestLruHead;  // -1 for empty
    private int _singleDestLruTail;  // -1 for empty
    private int _singleDestLruFreeHead;  // head of free slot list

    // Target-set cache: keyed by (zoneId, targetsHash) for multi-target flow fields
    private readonly ConcurrentDictionary<(int zoneId, int targetsHash), ZoneFlowDataHandle> _targetSetFlowCache;
    private readonly LruSlot[] _targetSetLruSlots;
    private readonly Dictionary<(int, int), int> _targetSetKeyToSlot;  // key -> slot index
    private readonly (int, int)[] _targetSetSlotToKey;  // slot -> key (for eviction)
    private int _targetSetLruHead;  // -1 for empty
    private int _targetSetLruTail;  // -1 for empty
    private int _targetSetLruFreeHead;  // head of free slot list

    private readonly ThreadLocal<PriorityQueue<(int tileX, int tileY), long>> _dijkstraQueue;
    private readonly ThreadLocal<HashSet<(int, int)>> _visited;
    private readonly ThreadLocal<HashSet<(int, int)>> _blocked;
    private readonly List<int> _multiTargetKeysToRemoveBuffer;
    private readonly List<(int, int, int, bool)> _singleDestKeysToRemoveBuffer;
    private readonly List<(int, int)> _targetSetKeysToRemoveBuffer;

    private readonly ConcurrentStack<FlowCell[]> _flowCellPool;
    private readonly ConcurrentStack<Fixed64[]> _distancePool;
    private readonly object _writeLock;
    private readonly StringBuilder _debugSb;

    private List<(int tileX, int tileY, Fixed64 cost)>? _currentSeeds;
    private HashSet<(int, int)> _currentSeedTiles;
    private int _currentSeedsHash;

    private const int MaxCachedZoneFlows = 256;
    private const int MaxCachedTargetSetFlows = 128;
    private static readonly Fixed64 CardinalCost = Fixed64.OneValue;
    private static readonly Fixed64 DiagonalCost = Fixed64.FromFloat(1.414f);
    private static readonly Fixed64 WallCostFactor = Fixed64.FromFloat(0.5f);
    private static readonly Fixed64 MaxDistance = Fixed64.FromFloat(float.MaxValue / 2f);
    private static readonly Fixed64 MinMagnitude = Fixed64.FromFloat(0.001f);
    private static readonly List<ZonePortal> _emptyPortalList = new(0);

    private volatile bool _flowsDirty;
    private readonly HashSet<(int sectorX, int sectorY)> _pendingInvalidations = new();
    private readonly List<(int sectorX, int sectorY)> _pendingInvalidationsSorted = new();

    public bool FlowsDirty => _flowsDirty;

    public void ClearDirtyFlag()
    {
        _flowsDirty = false;
    }

    public void MarkFlowsDirty()
    {
        _flowsDirty = true;
    }

    /// <summary>
    /// Marks a tile as dirty for deferred invalidation.
    /// Call FlushPendingInvalidations() to apply all pending invalidations.
    /// </summary>
    public void MarkTileDirty(int tileX, int tileY)
    {
        var (sectorX, sectorY) = _zoneGraph.TileToSector(tileX, tileY);
        _pendingInvalidations.Add((sectorX, sectorY));
        _flowsDirty = true;
    }

    /// <summary>
    /// Flushes all pending sector invalidations.
    /// Call this once per tick before systems that use flow fields.
    /// </summary>
    public void FlushPendingInvalidations()
    {
        if (_pendingInvalidations.Count == 0) return;

        // Sort pending invalidations for deterministic processing order.
        // This ensures zone IDs are assigned consistently across rollbacks.
        _pendingInvalidationsSorted.Clear();
        _pendingInvalidationsSorted.AddRange(_pendingInvalidations);
        _pendingInvalidationsSorted.Sort();

        for (int i = 0; i < _pendingInvalidationsSorted.Count; i++)
        {
            var (sx, sy) = _pendingInvalidationsSorted[i];
            _zoneGraph.InvalidateSector(sx, sy);
        }

        // Clear ALL flow caches globally, not just sector-local.
        // Flow fields are computed via zone graph pathfinding and can span many sectors.
        // A building in sector A can affect paths from sector B to sector C, even if
        // B and C are far from A. Only invalidating A's neighbors leaves stale cache entries.
        ClearAllFlows();
        ClearRecentPaths();  // Zone paths through affected sectors are also stale

        _pendingInvalidations.Clear();
    }

    public ZoneFlowService(
        ZoneGraph zoneGraph,
        IWorldProvider worldProvider,
        IPoolRegistry poolRegistry)
    {
        _zoneGraph = zoneGraph;
        _worldProvider = worldProvider;
        _poolRegistry = poolRegistry;

        _multiTargetFlowCache = new ConcurrentDictionary<int, ZoneFlowDataHandle>();
        _multiTargetLruSlots = new LruSlot[MaxCachedZoneFlows];
        _multiTargetSlotToZone = new int[MaxCachedZoneFlows];
        _multiTargetZoneToSlot = new Dictionary<int, int>(MaxCachedZoneFlows);
        _multiTargetLruHead = -1;
        _multiTargetLruTail = -1;
        InitializeMultiTargetFreeList();

        _singleDestFlowCache = new ConcurrentDictionary<(int, int, int, bool), ZoneFlowDataHandle>();
        _singleDestLruSlots = new LruSlot[MaxCachedZoneFlows];
        _singleDestSlotToKey = new (int, int, int, bool)[MaxCachedZoneFlows];
        _singleDestKeyToSlot = new Dictionary<(int, int, int, bool), int>(MaxCachedZoneFlows);
        _singleDestLruHead = -1;
        _singleDestLruTail = -1;
        InitializeSingleDestFreeList();

        _targetSetFlowCache = new ConcurrentDictionary<(int, int), ZoneFlowDataHandle>();
        _targetSetLruSlots = new LruSlot[MaxCachedTargetSetFlows];
        _targetSetSlotToKey = new (int, int)[MaxCachedTargetSetFlows];
        _targetSetKeyToSlot = new Dictionary<(int, int), int>(MaxCachedTargetSetFlows);
        _targetSetLruHead = -1;
        _targetSetLruTail = -1;
        InitializeTargetSetFreeList();

        _dijkstraQueue = new ThreadLocal<PriorityQueue<(int, int), long>>(() => new PriorityQueue<(int, int), long>(2048));
        _visited = new ThreadLocal<HashSet<(int, int)>>(() => new HashSet<(int, int)>(2048));
        _blocked = new ThreadLocal<HashSet<(int, int)>>(() => new HashSet<(int, int)>(2048));
        _multiTargetKeysToRemoveBuffer = new List<int>(32);
        _singleDestKeysToRemoveBuffer = new List<(int, int, int, bool)>(32);
        _targetSetKeysToRemoveBuffer = new List<(int, int)>(32);

        _flowCellPool = new ConcurrentStack<FlowCell[]>();
        _distancePool = new ConcurrentStack<Fixed64[]>();
        _writeLock = new object();
        _debugSb = new StringBuilder(256);

        _currentSeedTiles = new HashSet<(int, int)>(256);
    }

    private void InitializeMultiTargetFreeList()
    {
        // Link all slots into a free list via Next pointer
        for (int slotIndex = 0; slotIndex < MaxCachedZoneFlows - 1; slotIndex++)
        {
            _multiTargetLruSlots[slotIndex].Next = slotIndex + 1;
            _multiTargetLruSlots[slotIndex].Prev = -1;
            _multiTargetSlotToZone[slotIndex] = -1;
        }
        _multiTargetLruSlots[MaxCachedZoneFlows - 1].Next = -1;
        _multiTargetLruSlots[MaxCachedZoneFlows - 1].Prev = -1;
        _multiTargetSlotToZone[MaxCachedZoneFlows - 1] = -1;
        _multiTargetLruFreeHead = 0;
    }

    private void InitializeSingleDestFreeList()
    {
        // Link all slots into a free list via Next pointer
        for (int slotIndex = 0; slotIndex < MaxCachedZoneFlows - 1; slotIndex++)
        {
            _singleDestLruSlots[slotIndex].Next = slotIndex + 1;
            _singleDestLruSlots[slotIndex].Prev = -1;
            _singleDestSlotToKey[slotIndex] = (-1, -1, -1, false);
        }
        _singleDestLruSlots[MaxCachedZoneFlows - 1].Next = -1;
        _singleDestLruSlots[MaxCachedZoneFlows - 1].Prev = -1;
        _singleDestSlotToKey[MaxCachedZoneFlows - 1] = (-1, -1, -1, false);
        _singleDestLruFreeHead = 0;
    }

    private void InitializeTargetSetFreeList()
    {
        // Link all slots into a free list via Next pointer
        for (int slotIndex = 0; slotIndex < MaxCachedTargetSetFlows - 1; slotIndex++)
        {
            _targetSetLruSlots[slotIndex].Next = slotIndex + 1;
            _targetSetLruSlots[slotIndex].Prev = -1;
            _targetSetSlotToKey[slotIndex] = (-1, -1);
        }
        _targetSetLruSlots[MaxCachedTargetSetFlows - 1].Next = -1;
        _targetSetLruSlots[MaxCachedTargetSetFlows - 1].Prev = -1;
        _targetSetSlotToKey[MaxCachedTargetSetFlows - 1] = (-1, -1);
        _targetSetLruFreeHead = 0;
    }

    public void SetSeeds(List<(int tileX, int tileY, Fixed64 cost)> seeds)
    {
        int newHash = ComputeSeedsHash(seeds);
        if (newHash != _currentSeedsHash)
        {
            _currentSeeds = seeds;
            _currentSeedsHash = newHash;
            _currentSeedTiles.Clear();
            if (seeds != null)
            {
                for (int seedIndex = 0; seedIndex < seeds.Count; seedIndex++)
                {
                    var (tileX, tileY, _) = seeds[seedIndex];
                    _currentSeedTiles.Add((tileX, tileY));
                }
            }
            ClearMultiTargetFlows();
        }
    }

    public void ClearSeeds()
    {
        if (_currentSeeds != null)
        {
            _currentSeeds = null;
            _currentSeedsHash = 0;
            _currentSeedTiles.Clear();
            ClearMultiTargetFlows();
        }
    }

    /// <summary>
    /// Gets the current number of seed tiles.
    /// </summary>
    public int SeedCount => _currentSeeds?.Count ?? 0;

    public void InvalidateSeedTile(int tileX, int tileY)
    {
        if (_currentSeedTiles.Remove((tileX, tileY)))
        {
            ClearMultiTargetFlows();

            if (_currentSeeds != null)
            {
                for (int seedIndex = _currentSeeds.Count - 1; seedIndex >= 0; seedIndex--)
                {
                    var seed = _currentSeeds[seedIndex];
                    if (seed.tileX == tileX && seed.tileY == tileY)
                    {
                        _currentSeeds.RemoveAt(seedIndex);
                    }
                }
            }
        }
    }

    private int ComputeSeedsHash(List<(int tileX, int tileY, Fixed64 cost)> seeds)
    {
        if (seeds == null || seeds.Count == 0)
        {
            return 0;
        }

        int hash = 17;
        for (int seedIndex = 0; seedIndex < seeds.Count; seedIndex++)
        {
            var (tileX, tileY, cost) = seeds[seedIndex];
            hash = hash * 31 + tileX;
            hash = hash * 31 + tileY;
            hash = hash * 31 + cost.Raw.GetHashCode();
        }
        return hash;
    }

    public Fixed64Vec2 GetFlowDirection(Fixed64Vec2 worldPos)
    {
        if (_currentSeeds == null || _currentSeeds.Count == 0)
        {
            return Fixed64Vec2.Zero;
        }

        Fixed64 tileSizeFixed = Fixed64.FromInt(_worldProvider.TileSize);
        int currentTileX = (worldPos.X / tileSizeFixed).ToInt();
        int currentTileY = (worldPos.Y / tileSizeFixed).ToInt();

        int? currentZoneId = _zoneGraph.GetZoneIdAtTile(currentTileX, currentTileY);
        if (!currentZoneId.HasValue)
        {
            return FindDirectionToNearestSeed(currentTileX, currentTileY);
        }

        ZoneFlowDataHandle? flowHandle;

        if (_multiTargetFlowCache.TryGetValue(currentZoneId.Value, out var existingHandle))
        {
            var existingView = ZoneFlowData.Api.FromHandle(_poolRegistry, existingHandle);
            if (existingView.IsAlive && existingView.IsComplete)
            {
                flowHandle = existingHandle;
            }
            else
            {
                lock (_writeLock)
                {
                    flowHandle = GetOrCreateMultiTargetZoneFlow(currentZoneId.Value);
                }
            }
        }
        else
        {
            lock (_writeLock)
            {
                bool hadCachedFlow = _multiTargetFlowCache.ContainsKey(currentZoneId.Value);
                flowHandle = GetOrCreateMultiTargetZoneFlow(currentZoneId.Value);

                if (hadCachedFlow && _zoneGraph.LastPathfindBuiltSectors)
                {
                    InvalidateAllMultiTargetCaches();
                    flowHandle = GetOrCreateMultiTargetZoneFlow(currentZoneId.Value);
                }
            }
        }

        if (!flowHandle.HasValue)
        {
            return FindDirectionToNearestSeed(currentTileX, currentTileY);
        }

        var dir = GetZoneFlowDirection(flowHandle.Value, currentTileX, currentTileY);
        if (dir == Fixed64Vec2.Zero)
        {
            return FindDirectionToNearestSeed(currentTileX, currentTileY);
        }

        return dir;
    }

    public Fixed64Vec2 GetFlowDirectionCached(Fixed64Vec2 worldPos)
    {
        if (_currentSeeds == null || _currentSeeds.Count == 0)
        {
            return Fixed64Vec2.Zero;
        }

        Fixed64 tileSizeFixed = Fixed64.FromInt(_worldProvider.TileSize);
        int currentTileX = (worldPos.X / tileSizeFixed).ToInt();
        int currentTileY = (worldPos.Y / tileSizeFixed).ToInt();

        int? currentZoneId = _zoneGraph.GetZoneIdAtTile(currentTileX, currentTileY);
        if (!currentZoneId.HasValue)
        {
            return Fixed64Vec2.Zero;
        }

        if (!_multiTargetFlowCache.TryGetValue(currentZoneId.Value, out var handle))
        {
            return Fixed64Vec2.Zero;
        }

        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive || !view.IsComplete)
        {
            return Fixed64Vec2.Zero;
        }

        return GetZoneFlowDirection(handle, currentTileX, currentTileY);
    }

    public Fixed64Vec2 GetFlowDirectionForDestination(Fixed64Vec2 worldPos, int destTileX, int destTileY, bool ignoreBuildings = false)
    {
        Fixed64 tileSizeFixed = Fixed64.FromInt(_worldProvider.TileSize);
        int currentTileX = (worldPos.X / tileSizeFixed).ToInt();
        int currentTileY = (worldPos.Y / tileSizeFixed).ToInt();

        int? currentZoneId = _zoneGraph.GetZoneIdAtTile(currentTileX, currentTileY);

        if (!currentZoneId.HasValue)
        {
            // Unit is on unzoned tile (impassable or enclosed area) - no valid path
            return Fixed64Vec2.Zero;
        }

        var key = (currentZoneId.Value, destTileX, destTileY, ignoreBuildings);
        ZoneFlowDataHandle? flowHandle;

        if (_singleDestFlowCache.TryGetValue(key, out var existingHandle))
        {
            var existingView = ZoneFlowData.Api.FromHandle(_poolRegistry, existingHandle);
            if (existingView.IsAlive && existingView.IsComplete)
            {
                flowHandle = existingHandle;
            }
            else
            {
                lock (_writeLock)
                {
                    flowHandle = GetOrCreateSingleDestZoneFlow(currentZoneId.Value, destTileX, destTileY, ignoreBuildings);
                }
            }
        }
        else
        {
            lock (_writeLock)
            {
                bool hadCachedFlow = _singleDestFlowCache.ContainsKey(key);
                flowHandle = GetOrCreateSingleDestZoneFlow(currentZoneId.Value, destTileX, destTileY, ignoreBuildings);

                if (hadCachedFlow && _zoneGraph.LastPathfindBuiltSectors)
                {
                    InvalidateSingleDestCachesForDestination(destTileX, destTileY);
                    flowHandle = GetOrCreateSingleDestZoneFlow(currentZoneId.Value, destTileX, destTileY, ignoreBuildings);
                }
            }
        }

        if (!flowHandle.HasValue)
        {
            // No flow data available - no valid path
            return Fixed64Vec2.Zero;
        }

        // Return whatever direction the flow field gives (zero = no path or already at destination)
        return GetZoneFlowDirection(flowHandle.Value, currentTileX, currentTileY);
    }

    public Fixed64Vec2 GetFlowDirectionForDestinationCached(Fixed64Vec2 worldPos, int destTileX, int destTileY, bool ignoreBuildings = false)
    {
        Fixed64 tileSizeFixed = Fixed64.FromInt(_worldProvider.TileSize);
        int currentTileX = (worldPos.X / tileSizeFixed).ToInt();
        int currentTileY = (worldPos.Y / tileSizeFixed).ToInt();

        int? currentZoneId = _zoneGraph.GetZoneIdAtTile(currentTileX, currentTileY);
        if (!currentZoneId.HasValue)
        {
            return Fixed64Vec2.Zero;
        }

        var key = (currentZoneId.Value, destTileX, destTileY, ignoreBuildings);
        if (!_singleDestFlowCache.TryGetValue(key, out var handle))
        {
            return Fixed64Vec2.Zero;
        }

        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive || !view.IsComplete)
        {
            return Fixed64Vec2.Zero;
        }

        return GetZoneFlowDirection(handle, currentTileX, currentTileY);
    }

    /// <summary>
    /// Gets flow direction toward a building by finding the nearest passable perimeter tile.
    /// Buildings occupy blocked tiles, so we path to the closest tile around their footprint.
    /// </summary>
    public Fixed64Vec2 GetFlowDirectionForBuildingDestination(
        Fixed64Vec2 worldPos,
        int buildingTileX, int buildingTileY,
        int buildingWidth, int buildingHeight,
        bool ignoreBuildings = false)
    {
        Fixed64 tileSizeFixed = Fixed64.FromInt(_worldProvider.TileSize);
        int currentTileX = (worldPos.X / tileSizeFixed).ToInt();
        int currentTileY = (worldPos.Y / tileSizeFixed).ToInt();

        // Find the nearest passable tile on the building perimeter
        int bestTileX = -1;
        int bestTileY = -1;
        int bestDistSq = int.MaxValue;

        // Check all perimeter tiles around the building
        // Top edge (y = buildingTileY - 1)
        for (int x = buildingTileX; x < buildingTileX + buildingWidth; x++)
        {
            int y = buildingTileY - 1;
            if (_zoneGraph.GetZoneIdAtTile(x, y).HasValue)
            {
                int dx = x - currentTileX;
                int dy = y - currentTileY;
                int distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTileX = x;
                    bestTileY = y;
                }
            }
        }

        // Bottom edge (y = buildingTileY + buildingHeight)
        for (int x = buildingTileX; x < buildingTileX + buildingWidth; x++)
        {
            int y = buildingTileY + buildingHeight;
            if (_zoneGraph.GetZoneIdAtTile(x, y).HasValue)
            {
                int dx = x - currentTileX;
                int dy = y - currentTileY;
                int distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTileX = x;
                    bestTileY = y;
                }
            }
        }

        // Left edge (x = buildingTileX - 1)
        for (int y = buildingTileY; y < buildingTileY + buildingHeight; y++)
        {
            int x = buildingTileX - 1;
            if (_zoneGraph.GetZoneIdAtTile(x, y).HasValue)
            {
                int dx = x - currentTileX;
                int dy = y - currentTileY;
                int distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTileX = x;
                    bestTileY = y;
                }
            }
        }

        // Right edge (x = buildingTileX + buildingWidth)
        for (int y = buildingTileY; y < buildingTileY + buildingHeight; y++)
        {
            int x = buildingTileX + buildingWidth;
            if (_zoneGraph.GetZoneIdAtTile(x, y).HasValue)
            {
                int dx = x - currentTileX;
                int dy = y - currentTileY;
                int distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTileX = x;
                    bestTileY = y;
                }
            }
        }

        // No passable perimeter tile found
        if (bestTileX < 0)
        {
            return Fixed64Vec2.Zero;
        }

        // Use the standard single-dest flow to the nearest perimeter tile
        return GetFlowDirectionForDestination(worldPos, bestTileX, bestTileY, ignoreBuildings);
    }

    /// <summary>
    /// Gets flow direction toward the nearest of multiple target tiles.
    /// Used for formation movement where units flow toward any of the formation slots.
    /// </summary>
    public Fixed64Vec2 GetFlowDirectionForTargets(
        Fixed64Vec2 worldPos,
        ReadOnlySpan<(int tileX, int tileY)> targets)
    {
        if (targets.Length == 0)
        {
            return Fixed64Vec2.Zero;
        }

        Fixed64 tileSizeFixed = Fixed64.FromInt(_worldProvider.TileSize);
        int currentTileX = (worldPos.X / tileSizeFixed).ToInt();
        int currentTileY = (worldPos.Y / tileSizeFixed).ToInt();

        int? currentZoneId = _zoneGraph.GetZoneIdAtTile(currentTileX, currentTileY);
        if (!currentZoneId.HasValue)
        {
            // Unit is on unzoned tile - fall back to direct direction
            return FindDirectionToNearestTarget(currentTileX, currentTileY, targets);
        }

        int targetsHash = ComputeTargetsHash(targets);
        var key = (currentZoneId.Value, targetsHash);
        ZoneFlowDataHandle? flowHandle;

        if (_targetSetFlowCache.TryGetValue(key, out var existingHandle))
        {
            var existingView = ZoneFlowData.Api.FromHandle(_poolRegistry, existingHandle);
            if (existingView.IsAlive && existingView.IsComplete)
            {
                flowHandle = existingHandle;
                TouchTargetSet(key);
            }
            else
            {
                lock (_writeLock)
                {
                    flowHandle = GetOrCreateTargetSetZoneFlow(currentZoneId.Value, targetsHash, targets);
                }
            }
        }
        else
        {
            lock (_writeLock)
            {
                flowHandle = GetOrCreateTargetSetZoneFlow(currentZoneId.Value, targetsHash, targets);
            }
        }

        if (!flowHandle.HasValue)
        {
            return FindDirectionToNearestTarget(currentTileX, currentTileY, targets);
        }

        var dir = GetZoneFlowDirection(flowHandle.Value, currentTileX, currentTileY);
        if (dir == Fixed64Vec2.Zero)
        {
            return FindDirectionToNearestTarget(currentTileX, currentTileY, targets);
        }

        return dir;
    }

    private static int ComputeTargetsHash(ReadOnlySpan<(int tileX, int tileY)> targets)
    {
        int hash = 17;
        for (int i = 0; i < targets.Length; i++)
        {
            hash = hash * 31 + targets[i].tileX;
            hash = hash * 31 + targets[i].tileY;
        }
        return hash;
    }

    private Fixed64Vec2 FindDirectionToNearestTarget(
        int fromTileX, int fromTileY,
        ReadOnlySpan<(int tileX, int tileY)> targets)
    {
        if (targets.Length == 0)
        {
            return Fixed64Vec2.Zero;
        }

        int nearestX = targets[0].tileX;
        int nearestY = targets[0].tileY;
        int nearestDistSq = (nearestX - fromTileX) * (nearestX - fromTileX) +
                           (nearestY - fromTileY) * (nearestY - fromTileY);

        for (int i = 1; i < targets.Length; i++)
        {
            int dx = targets[i].tileX - fromTileX;
            int dy = targets[i].tileY - fromTileY;
            int distSq = dx * dx + dy * dy;
            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearestX = targets[i].tileX;
                nearestY = targets[i].tileY;
            }
        }

        int dirX = nearestX - fromTileX;
        int dirY = nearestY - fromTileY;

        if (dirX == 0 && dirY == 0)
        {
            return Fixed64Vec2.Zero;
        }

        // Normalize
        Fixed64 fx = Fixed64.FromInt(dirX);
        Fixed64 fy = Fixed64.FromInt(dirY);
        Fixed64 lenSq = fx * fx + fy * fy;
        Fixed64 len = Fixed64.Sqrt(lenSq);

        if (len < MinMagnitude)
        {
            return Fixed64Vec2.Zero;
        }

        return new Fixed64Vec2(fx / len, fy / len);
    }

    private ZoneFlowDataHandle? GetOrCreateTargetSetZoneFlow(
        int zoneId,
        int targetsHash,
        ReadOnlySpan<(int tileX, int tileY)> targets)
    {
        var key = (zoneId, targetsHash);
        if (_targetSetFlowCache.TryGetValue(key, out var existingHandle))
        {
            var existingView = ZoneFlowData.Api.FromHandle(_poolRegistry, existingHandle);
            if (existingView.IsAlive && existingView.IsComplete)
            {
                TouchTargetSet(key);
                return existingHandle;
            }
        }

        var zone = _zoneGraph.GetZoneById(zoneId);
        if (!zone.HasValue)
        {
            return null;
        }

        return CreateTargetSetZoneFlow(zoneId, zone.Value.SectorX, zone.Value.SectorY, targetsHash, targets);
    }

    private ZoneFlowDataHandle? GetOrCreateTargetSetZoneFlowRecursive(
        int zoneId,
        int targetsHash,
        ReadOnlySpan<(int tileX, int tileY)> targets,
        int recursionDepth,
        HashSet<int>? visitedZones)
    {
        var key = (zoneId, targetsHash);
        if (_targetSetFlowCache.TryGetValue(key, out var existingHandle))
        {
            var existingView = ZoneFlowData.Api.FromHandle(_poolRegistry, existingHandle);
            if (existingView.IsAlive && existingView.IsComplete)
            {
                TouchTargetSet(key);
                return existingHandle;
            }
        }

        var zone = _zoneGraph.GetZoneById(zoneId);
        if (!zone.HasValue)
        {
            return null;
        }

        return CreateTargetSetZoneFlowRecursive(
            zoneId, zone.Value.SectorX, zone.Value.SectorY, targetsHash, targets,
            recursionDepth, visitedZones);
    }

    private ZoneFlowDataHandle CreateTargetSetZoneFlow(
        int zoneId,
        int sectorX,
        int sectorY,
        int targetsHash,
        ReadOnlySpan<(int tileX, int tileY)> targets)
    {
        // Evict if no free slots
        while (_targetSetLruFreeHead < 0)
        {
            EvictOldestTargetSet();
        }

        var flowCells = RentFlowCells();
        var distances = RentDistances();

        var initData = new ZoneFlowData
        {
            FlowCells = flowCells,
            Distances = distances,
            ZoneId = zoneId,
            SectorX = sectorX,
            SectorY = sectorY,
            IsComplete = false
        };

        var newView = ZoneFlowData.Api.Create(_poolRegistry, initData);
        var newHandle = newView.Handle;

        var key = (zoneId, targetsHash);
        _targetSetFlowCache[key] = newHandle;

        // Allocate a slot from free list
        int slot = _targetSetLruFreeHead;
        _targetSetLruFreeHead = _targetSetLruSlots[slot].Next;

        // Add to LRU list at head
        _targetSetSlotToKey[slot] = key;
        _targetSetKeyToSlot[key] = slot;
        _targetSetLruSlots[slot].Prev = -1;
        _targetSetLruSlots[slot].Next = _targetSetLruHead;
        if (_targetSetLruHead >= 0)
        {
            _targetSetLruSlots[_targetSetLruHead].Prev = slot;
        }
        _targetSetLruHead = slot;
        if (_targetSetLruTail < 0)
        {
            _targetSetLruTail = slot;
        }

        ComputeTargetSetZoneFlow(newHandle, zoneId, sectorX, sectorY, targets);

        return newHandle;
    }

    private ZoneFlowDataHandle CreateTargetSetZoneFlowRecursive(
        int zoneId,
        int sectorX,
        int sectorY,
        int targetsHash,
        ReadOnlySpan<(int tileX, int tileY)> targets,
        int recursionDepth,
        HashSet<int>? visitedZones)
    {
        // Evict if no free slots
        while (_targetSetLruFreeHead < 0)
        {
            EvictOldestTargetSet();
        }

        var flowCells = RentFlowCells();
        var distances = RentDistances();

        var initData = new ZoneFlowData
        {
            FlowCells = flowCells,
            Distances = distances,
            ZoneId = zoneId,
            SectorX = sectorX,
            SectorY = sectorY,
            IsComplete = false
        };

        var newView = ZoneFlowData.Api.Create(_poolRegistry, initData);
        var newHandle = newView.Handle;

        var key = (zoneId, targetsHash);
        _targetSetFlowCache[key] = newHandle;

        // Allocate a slot from free list
        int slot = _targetSetLruFreeHead;
        _targetSetLruFreeHead = _targetSetLruSlots[slot].Next;

        // Add to LRU list at head
        _targetSetSlotToKey[slot] = key;
        _targetSetKeyToSlot[key] = slot;
        _targetSetLruSlots[slot].Prev = -1;
        _targetSetLruSlots[slot].Next = _targetSetLruHead;
        if (_targetSetLruHead >= 0)
        {
            _targetSetLruSlots[_targetSetLruHead].Prev = slot;
        }
        _targetSetLruHead = slot;
        if (_targetSetLruTail < 0)
        {
            _targetSetLruTail = slot;
        }

        ComputeTargetSetZoneFlowRecursive(newHandle, zoneId, sectorX, sectorY, targets, recursionDepth, visitedZones);

        return newHandle;
    }

    private void ComputeTargetSetZoneFlow(
        ZoneFlowDataHandle handle,
        int zoneId,
        int sectorX,
        int sectorY,
        ReadOnlySpan<(int tileX, int tileY)> targets)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        int tileSize = _worldProvider.TileSize;
        int sectorMinTileX = sectorX * ZoneGraph.SectorSize;
        int sectorMinTileY = sectorY * ZoneGraph.SectorSize;

        _dijkstraQueue.Value!.Clear();
        _visited.Value!.Clear();
        _blocked.Value!.Clear();

        // Seed from all targets within this zone
        for (int i = 0; i < targets.Length; i++)
        {
            var (targetTileX, targetTileY) = targets[i];
            int? targetZoneId = _zoneGraph.GetZoneIdAtTile(targetTileX, targetTileY);

            if (targetZoneId == zoneId)
            {
                int localX = targetTileX - sectorMinTileX;
                int localY = targetTileY - sectorMinTileY;

                if (localX >= 0 && localX < ZoneGraph.SectorSize &&
                    localY >= 0 && localY < ZoneGraph.SectorSize)
                {
                    int cellIndex = localX + localY * ZoneGraph.SectorSize;
                    if (Fixed64.Zero < view.Distances[cellIndex])
                    {
                        view.Distances[cellIndex] = Fixed64.Zero;
                        _dijkstraQueue.Value!.Enqueue((targetTileX, targetTileY), 0);
                    }
                }
            }
        }

        // Seed from downstream zones (portals leading to zones with targets)
        SeedFromDownstreamZonesTargetSet(handle, zoneId, sectorMinTileX, sectorMinTileY, tileSize, targets);

        RunDijkstraAndComputeDirections(handle, zoneId, sectorMinTileX, sectorMinTileY, tileSize);
    }

    private void ComputeTargetSetZoneFlowRecursive(
        ZoneFlowDataHandle handle,
        int zoneId,
        int sectorX,
        int sectorY,
        ReadOnlySpan<(int tileX, int tileY)> targets,
        int recursionDepth,
        HashSet<int>? visitedZones)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        int tileSize = _worldProvider.TileSize;
        int sectorMinTileX = sectorX * ZoneGraph.SectorSize;
        int sectorMinTileY = sectorY * ZoneGraph.SectorSize;

        _dijkstraQueue.Value!.Clear();
        _visited.Value!.Clear();
        _blocked.Value!.Clear();

        // Seed from all targets within this zone
        for (int i = 0; i < targets.Length; i++)
        {
            var (targetTileX, targetTileY) = targets[i];
            int? targetZoneId = _zoneGraph.GetZoneIdAtTile(targetTileX, targetTileY);

            if (targetZoneId == zoneId)
            {
                int localX = targetTileX - sectorMinTileX;
                int localY = targetTileY - sectorMinTileY;

                if (localX >= 0 && localX < ZoneGraph.SectorSize &&
                    localY >= 0 && localY < ZoneGraph.SectorSize)
                {
                    int cellIndex = localX + localY * ZoneGraph.SectorSize;
                    if (Fixed64.Zero < view.Distances[cellIndex])
                    {
                        view.Distances[cellIndex] = Fixed64.Zero;
                        _dijkstraQueue.Value!.Enqueue((targetTileX, targetTileY), 0);
                    }
                }
            }
        }

        // Seed from downstream zones (portals leading to zones with targets)
        SeedFromDownstreamZonesTargetSet(handle, zoneId, sectorMinTileX, sectorMinTileY, tileSize, targets,
            recursionDepth, visitedZones);

        RunDijkstraAndComputeDirections(handle, zoneId, sectorMinTileX, sectorMinTileY, tileSize);
    }

    private const int MaxTargetSetRecursionDepth = 10;

    private void SeedFromDownstreamZonesTargetSet(
        ZoneFlowDataHandle handle,
        int zoneId,
        int sectorMinTileX,
        int sectorMinTileY,
        int tileSize,
        ReadOnlySpan<(int tileX, int tileY)> targets,
        int recursionDepth = 0,
        HashSet<int>? visitedZones = null)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        // Find which zones contain targets
        Span<int> targetZoneIds = stackalloc int[targets.Length];
        int targetZoneCount = 0;

        for (int i = 0; i < targets.Length; i++)
        {
            int? zId = _zoneGraph.GetZoneIdAtTile(targets[i].tileX, targets[i].tileY);
            if (zId.HasValue)
            {
                // Check if already in list
                bool found = false;
                for (int j = 0; j < targetZoneCount; j++)
                {
                    if (targetZoneIds[j] == zId.Value)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    targetZoneIds[targetZoneCount++] = zId.Value;
                }
            }
        }

        if (targetZoneCount == 0)
        {
            return;
        }

        // Find nearest target zone via zone path (like SeedFromDownstreamZonesMultiTarget)
        int? nearestTargetZoneId = null;
        int shortestPathLength = int.MaxValue;

        for (int t = 0; t < targetZoneCount; t++)
        {
            int targetZoneId = targetZoneIds[t];
            if (targetZoneId == zoneId)
            {
                // Target is in this zone, no downstream seeding needed
                continue;
            }

            var zonePath = _zoneGraph.FindZonePath(zoneId, targetZoneId);
            if (zonePath != null && zonePath.Count > 1 && zonePath.Count < shortestPathLength)
            {
                shortestPathLength = zonePath.Count;
                nearestTargetZoneId = targetZoneId;
            }
        }

        if (!nearestTargetZoneId.HasValue)
        {
            return;
        }

        // Get path to nearest target zone
        var pathToTarget = _zoneGraph.FindZonePath(zoneId, nearestTargetZoneId.Value);
        if (pathToTarget == null || pathToTarget.Count <= 1)
        {
            return;
        }

        int nextZoneId = pathToTarget[1];

        // Get ALL portals between zones
        var portals = _zoneGraph.FindAllPortalsBetweenZones(zoneId, nextZoneId);
        if (portals.Count == 0)
        {
            return;
        }

        // Try to find downstream target-set flow
        int targetsHash = ComputeTargetsHash(targets);
        ZoneFlowDataHandle downstreamHandle;

        if (!_targetSetFlowCache.TryGetValue((nextZoneId, targetsHash), out downstreamHandle))
        {
            // Downstream flow doesn't exist - recursively create it
            if (recursionDepth >= MaxTargetSetRecursionDepth)
            {
                return; // Max depth reached, fall back to direct steering
            }

            visitedZones ??= new HashSet<int>();
            if (visitedZones.Contains(nextZoneId))
            {
                return; // Cycle detected
            }
            visitedZones.Add(zoneId);

            // Copy targets to pooled array since span may be invalidated by recursion
            var targetsCopy = ArrayPool<(int, int)>.Shared.Rent(targets.Length);
            int targetsLength = targets.Length;
            for (int i = 0; i < targetsLength; i++)
            {
                targetsCopy[i] = targets[i];
            }

            try
            {
                var newHandle = GetOrCreateTargetSetZoneFlowRecursive(
                    nextZoneId, targetsHash,
                    new ReadOnlySpan<(int, int)>(targetsCopy, 0, targetsLength),
                    recursionDepth + 1, visitedZones);

                if (!newHandle.HasValue)
                {
                    return;
                }
                downstreamHandle = newHandle.Value;
            }
            finally
            {
                ArrayPool<(int, int)>.Shared.Return(targetsCopy);
            }
        }

        var downstreamView = ZoneFlowData.Api.FromHandle(_poolRegistry, downstreamHandle);
        if (!downstreamView.IsAlive || !downstreamView.IsComplete)
        {
            return;
        }

        // Seed from ALL portals (reuse existing helper)
        for (int portalIndex = 0; portalIndex < portals.Count; portalIndex++)
        {
            SeedPortalFromDownstream(handle, portals[portalIndex], downstreamHandle, sectorMinTileX, sectorMinTileY, tileSize);
        }
    }

    public bool IsNearSeed(Fixed64Vec2 worldPos, Fixed64 arrivalDistance)
    {
        if (_currentSeeds == null || _currentSeeds.Count == 0)
        {
            return false;
        }

        Fixed64 tileSizeFixed = Fixed64.FromInt(_worldProvider.TileSize);
        int centerTileX = (worldPos.X / tileSizeFixed).ToInt();
        int centerTileY = (worldPos.Y / tileSizeFixed).ToInt();

        Fixed64 halfTile = tileSizeFixed / Fixed64.FromInt(2);
        Fixed64 arrivalDistSquared = arrivalDistance * arrivalDistance;

        for (int seedIndex = 0; seedIndex < _currentSeeds.Count; seedIndex++)
        {
            var (seedTileX, seedTileY, _) = _currentSeeds[seedIndex];

            int deltaX = seedTileX - centerTileX;
            int deltaY = seedTileY - centerTileY;

            if (Math.Abs(deltaX) > 1 || Math.Abs(deltaY) > 1)
            {
                continue;
            }

            Fixed64 targetCenterX = Fixed64.FromInt(seedTileX) * tileSizeFixed + halfTile;
            Fixed64 targetCenterY = Fixed64.FromInt(seedTileY) * tileSizeFixed + halfTile;
            Fixed64 distX = targetCenterX - worldPos.X;
            Fixed64 distY = targetCenterY - worldPos.Y;
            Fixed64 distSquared = distX * distX + distY * distY;

            if (distSquared <= arrivalDistSquared)
            {
                return true;
            }
        }

        return false;
    }

    public void InvalidateSector(int sectorX, int sectorY)
    {
        // ZoneGraph.InvalidateSector now rebuilds the sector and neighbors
        _zoneGraph.InvalidateSector(sectorX, sectorY);
        _flowsDirty = true;

        // Clear flow caches for the affected sectors (center + 4 neighbors)
        InvalidateSectorFlowCache(sectorX, sectorY);
        InvalidateSectorFlowCache(sectorX - 1, sectorY);
        InvalidateSectorFlowCache(sectorX + 1, sectorY);
        InvalidateSectorFlowCache(sectorX, sectorY - 1);
        InvalidateSectorFlowCache(sectorX, sectorY + 1);
    }

    public void InvalidateTile(int tileX, int tileY)
    {
        var (sectorX, sectorY) = _zoneGraph.TileToSector(tileX, tileY);
        InvalidateSector(sectorX, sectorY);
    }

    private void InvalidateSectorFlowCache(int sectorX, int sectorY)
    {
        _multiTargetKeysToRemoveBuffer.Clear();
        foreach (var kvp in _multiTargetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive && view.SectorX == sectorX && view.SectorY == sectorY)
            {
                _multiTargetKeysToRemoveBuffer.Add(kvp.Key);
            }
        }

        for (int keyIndex = 0; keyIndex < _multiTargetKeysToRemoveBuffer.Count; keyIndex++)
        {
            int zoneId = _multiTargetKeysToRemoveBuffer[keyIndex];
            EvictMultiTargetZone(zoneId);
        }

        _singleDestKeysToRemoveBuffer.Clear();
        foreach (var kvp in _singleDestFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive && view.SectorX == sectorX && view.SectorY == sectorY)
            {
                _singleDestKeysToRemoveBuffer.Add(kvp.Key);
            }
        }

        for (int keyIndex = 0; keyIndex < _singleDestKeysToRemoveBuffer.Count; keyIndex++)
        {
            var key = _singleDestKeysToRemoveBuffer[keyIndex];
            EvictSingleDestZone(key);
        }

        // Evict target-set flows in this sector
        _targetSetKeysToRemoveBuffer.Clear();
        foreach (var kvp in _targetSetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive && view.SectorX == sectorX && view.SectorY == sectorY)
            {
                _targetSetKeysToRemoveBuffer.Add(kvp.Key);
            }
        }

        for (int keyIndex = 0; keyIndex < _targetSetKeysToRemoveBuffer.Count; keyIndex++)
        {
            var key = _targetSetKeysToRemoveBuffer[keyIndex];
            EvictTargetSetZone(key);
        }
    }

    private void InvalidateSingleDestCachesForDestination(int destTileX, int destTileY)
    {
        _singleDestKeysToRemoveBuffer.Clear();
        foreach (var kvp in _singleDestFlowCache)
        {
            var (zoneId, cachedDestX, cachedDestY, _) = kvp.Key;
            if (cachedDestX == destTileX && cachedDestY == destTileY)
            {
                _singleDestKeysToRemoveBuffer.Add(kvp.Key);
            }
        }

        for (int keyIndex = 0; keyIndex < _singleDestKeysToRemoveBuffer.Count; keyIndex++)
        {
            var key = _singleDestKeysToRemoveBuffer[keyIndex];
            EvictSingleDestZone(key);
        }
    }

    private void InvalidateAllMultiTargetCaches()
    {
        _multiTargetKeysToRemoveBuffer.Clear();
        foreach (var kvp in _multiTargetFlowCache)
        {
            _multiTargetKeysToRemoveBuffer.Add(kvp.Key);
        }

        for (int keyIndex = 0; keyIndex < _multiTargetKeysToRemoveBuffer.Count; keyIndex++)
        {
            int zoneId = _multiTargetKeysToRemoveBuffer[keyIndex];
            EvictMultiTargetZone(zoneId);
        }
    }

    private ZoneFlowDataHandle? GetOrCreateMultiTargetZoneFlow(int zoneId)
    {
        if (_multiTargetFlowCache.TryGetValue(zoneId, out var existingHandle))
        {
            var existingView = ZoneFlowData.Api.FromHandle(_poolRegistry, existingHandle);
            if (existingView.IsAlive)
            {
                if (existingView.IsComplete)
                {
                    TouchMultiTarget(zoneId);
                }
                return existingHandle;
            }
        }

        var zone = _zoneGraph.GetZoneById(zoneId);
        if (!zone.HasValue)
        {
            return null;
        }

        return CreateMultiTargetZoneFlow(zoneId, zone.Value.SectorX, zone.Value.SectorY);
    }

    private ZoneFlowDataHandle? GetOrCreateSingleDestZoneFlow(int zoneId, int destTileX, int destTileY, bool ignoreBuildings = false)
    {
        var key = (zoneId, destTileX, destTileY, ignoreBuildings);
        if (_singleDestFlowCache.TryGetValue(key, out var existingHandle))
        {
            var existingView = ZoneFlowData.Api.FromHandle(_poolRegistry, existingHandle);
            if (existingView.IsAlive && existingView.IsComplete)
            {
                TouchSingleDest(key);
                return existingHandle;
            }
        }

        var zone = _zoneGraph.GetZoneById(zoneId);
        if (!zone.HasValue)
        {
            return null;
        }

        EnsureFlowsAlongPath(zoneId, destTileX, destTileY, ignoreBuildings);

        if (_singleDestFlowCache.TryGetValue(key, out var newHandle))
        {
            return newHandle;
        }

        return null;
    }

    private ZoneFlowDataHandle CreateMultiTargetZoneFlow(int zoneId, int sectorX, int sectorY)
    {
        // Evict if no free slots
        while (_multiTargetLruFreeHead < 0)
        {
            EvictOldestMultiTarget();
        }

        var flowCells = RentFlowCells();
        var distances = RentDistances();

        var initData = new ZoneFlowData
        {
            FlowCells = flowCells,
            Distances = distances,
            ZoneId = zoneId,
            SectorX = sectorX,
            SectorY = sectorY,
            IsComplete = false
        };

        var newView = ZoneFlowData.Api.Create(_poolRegistry, initData);
        var newHandle = newView.Handle;

        _multiTargetFlowCache[zoneId] = newHandle;

        // Allocate a slot from free list
        int slot = _multiTargetLruFreeHead;
        _multiTargetLruFreeHead = _multiTargetLruSlots[slot].Next;

        // Add to LRU list at head
        _multiTargetSlotToZone[slot] = zoneId;
        _multiTargetZoneToSlot[zoneId] = slot;
        _multiTargetLruSlots[slot].Prev = -1;
        _multiTargetLruSlots[slot].Next = _multiTargetLruHead;
        if (_multiTargetLruHead >= 0)
        {
            _multiTargetLruSlots[_multiTargetLruHead].Prev = slot;
        }
        _multiTargetLruHead = slot;
        if (_multiTargetLruTail < 0)
        {
            _multiTargetLruTail = slot;
        }

        ComputeMultiTargetZoneFlow(newHandle, zoneId, sectorX, sectorY);

        return newHandle;
    }


    private void ComputeMultiTargetZoneFlow(ZoneFlowDataHandle handle, int zoneId, int sectorX, int sectorY)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        int tileSize = _worldProvider.TileSize;
        int sectorMinTileX = sectorX * ZoneGraph.SectorSize;
        int sectorMinTileY = sectorY * ZoneGraph.SectorSize;

        _dijkstraQueue.Value!.Clear();
        _visited.Value!.Clear();
        _blocked.Value!.Clear();

        if (_currentSeeds != null)
        {
            for (int seedIndex = 0; seedIndex < _currentSeeds.Count; seedIndex++)
            {
                var (seedTileX, seedTileY, seedCost) = _currentSeeds[seedIndex];
                int? seedZoneId = _zoneGraph.GetZoneIdAtTile(seedTileX, seedTileY);

                if (seedZoneId == zoneId)
                {
                    int localX = seedTileX - sectorMinTileX;
                    int localY = seedTileY - sectorMinTileY;

                    if (localX >= 0 && localX < ZoneGraph.SectorSize &&
                        localY >= 0 && localY < ZoneGraph.SectorSize)
                    {
                        int cellIndex = localX + localY * ZoneGraph.SectorSize;
                        if (seedCost < view.Distances[cellIndex])
                        {
                            view.Distances[cellIndex] = seedCost;
                            _dijkstraQueue.Value!.Enqueue((seedTileX, seedTileY), seedCost.Raw);
                        }
                    }
                }
            }
        }

        SeedFromDownstreamZonesMultiTarget(handle, zoneId, sectorMinTileX, sectorMinTileY, tileSize);

        RunDijkstraAndComputeDirections(handle, zoneId, sectorMinTileX, sectorMinTileY, tileSize);
    }


    private readonly ThreadLocal<List<int>> _pathZoneBuffer = new ThreadLocal<List<int>>(() => new List<int>(32));
    private readonly ThreadLocal<List<ZoneFlowDataHandle>> _pathFlowHandles = new ThreadLocal<List<ZoneFlowDataHandle>>(() => new List<ZoneFlowDataHandle>(32));

    public static bool DebugLogEnabled = false;

    private void EnsureFlowsAlongPath(int startZoneId, int destTileX, int destTileY, bool ignoreBuildings = false)
    {
        int? destZoneId = _zoneGraph.GetZoneIdAtTile(destTileX, destTileY);
        if (!destZoneId.HasValue)
        {
            return;
        }

        var zonePath = _zoneGraph.FindZonePath(startZoneId, destZoneId.Value, destTileX, destTileY);
        if (zonePath == null || zonePath.Count == 0)
        {
            return;
        }

        if (DebugLogEnabled)
        {
            _debugSb.Clear();
            _debugSb.Append("[FlowField] EnsureFlowsAlongPath: start=");
            _debugSb.Append(startZoneId);
            _debugSb.Append(" dest=(");
            _debugSb.Append(destTileX);
            _debugSb.Append(',');
            _debugSb.Append(destTileY);
            _debugSb.Append(") pathLen=");
            _debugSb.Append(zonePath.Count);
            Console.WriteLine(_debugSb.ToString());
        }

        var pathZoneBuffer = _pathZoneBuffer.Value!;
        var pathFlowHandles = _pathFlowHandles.Value!;

        pathZoneBuffer.Clear();
        pathZoneBuffer.AddRange(zonePath);

        pathFlowHandles.Clear();
        for (int pathIndex = 0; pathIndex < pathZoneBuffer.Count; pathIndex++)
        {
            int pathZoneId = pathZoneBuffer[pathIndex];
            var key = (pathZoneId, destTileX, destTileY, ignoreBuildings);

            if (_singleDestFlowCache.TryGetValue(key, out var existingHandle))
            {
                var existingView = ZoneFlowData.Api.FromHandle(_poolRegistry, existingHandle);
                if (existingView.IsAlive && existingView.IsComplete)
                {
                    pathFlowHandles.Add(existingHandle);
                    continue;
                }
            }

            var zone = _zoneGraph.GetZoneById(pathZoneId);
            if (!zone.HasValue)
            {
                pathFlowHandles.Add(default);
                continue;
            }

            var flowHandle = CreateFlowShellForPath(pathZoneId, zone.Value.SectorX, zone.Value.SectorY, destTileX, destTileY, ignoreBuildings);
            pathFlowHandles.Add(flowHandle);
        }

        for (int pathIndex = pathZoneBuffer.Count - 1; pathIndex >= 0; pathIndex--)
        {
            var flowHandle = pathFlowHandles[pathIndex];
            if (flowHandle.Equals(default))
            {
                continue;
            }

            var flowView = ZoneFlowData.Api.FromHandle(_poolRegistry, flowHandle);
            if (!flowView.IsAlive || flowView.IsComplete)
            {
                continue;
            }

            int pathZoneId = pathZoneBuffer[pathIndex];
            ZoneFlowDataHandle? downstreamHandle = null;
            List<ZonePortal> portals = _emptyPortalList;

            if (pathIndex < pathZoneBuffer.Count - 1)
            {
                int nextZoneId = pathZoneBuffer[pathIndex + 1];
                downstreamHandle = pathFlowHandles[pathIndex + 1];
                // Get ALL portals between zones (handles multiple gaps/passages)
                portals = _zoneGraph.FindAllPortalsBetweenZones(pathZoneId, nextZoneId);
            }

            ComputeSingleDestFlowWithDownstream(flowHandle, pathZoneId, flowView.SectorX, flowView.SectorY, destTileX, destTileY, downstreamHandle, portals, ignoreBuildings);
        }
    }

    private ZoneFlowDataHandle CreateFlowShellForPath(int zoneId, int sectorX, int sectorY, int destTileX, int destTileY, bool ignoreBuildings = false)
    {
        if (DebugLogEnabled)
        {
            _debugSb.Clear();
            _debugSb.Append("[FlowField] CreateFlowShell: zone=");
            _debugSb.Append(zoneId);
            _debugSb.Append(" sector=(");
            _debugSb.Append(sectorX);
            _debugSb.Append(',');
            _debugSb.Append(sectorY);
            _debugSb.Append(") cacheSize=");
            _debugSb.Append(_singleDestFlowCache.Count);
            Console.WriteLine(_debugSb.ToString());
        }

        // Evict if no free slots
        while (_singleDestLruFreeHead < 0)
        {
            EvictOldestSingleDest();
        }

        var flowCells = RentFlowCells();
        var distances = RentDistances();

        var initData = new ZoneFlowData
        {
            FlowCells = flowCells,
            Distances = distances,
            ZoneId = zoneId,
            SectorX = sectorX,
            SectorY = sectorY,
            IsComplete = false
        };

        var newView = ZoneFlowData.Api.Create(_poolRegistry, initData);
        var newHandle = newView.Handle;

        var key = (zoneId, destTileX, destTileY, ignoreBuildings);
        _singleDestFlowCache[key] = newHandle;

        // Allocate a slot from free list
        int slot = _singleDestLruFreeHead;
        _singleDestLruFreeHead = _singleDestLruSlots[slot].Next;

        // Add to LRU list at head
        _singleDestSlotToKey[slot] = key;
        _singleDestKeyToSlot[key] = slot;
        _singleDestLruSlots[slot].Prev = -1;
        _singleDestLruSlots[slot].Next = _singleDestLruHead;
        if (_singleDestLruHead >= 0)
        {
            _singleDestLruSlots[_singleDestLruHead].Prev = slot;
        }
        _singleDestLruHead = slot;
        if (_singleDestLruTail < 0)
        {
            _singleDestLruTail = slot;
        }

        return newHandle;
    }

    private void ComputeSingleDestFlowWithDownstream(ZoneFlowDataHandle handle, int zoneId, int sectorX, int sectorY, int destTileX, int destTileY, ZoneFlowDataHandle? downstreamHandle, List<ZonePortal> portals, bool ignoreBuildings = false)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        int tileSize = _worldProvider.TileSize;
        int sectorMinTileX = sectorX * ZoneGraph.SectorSize;
        int sectorMinTileY = sectorY * ZoneGraph.SectorSize;

        _dijkstraQueue.Value!.Clear();
        _visited.Value!.Clear();
        _blocked.Value!.Clear();

        int? destZoneId = _zoneGraph.GetZoneIdAtTile(destTileX, destTileY);
        if (destZoneId == zoneId)
        {
            int localX = destTileX - sectorMinTileX;
            int localY = destTileY - sectorMinTileY;

            if (localX >= 0 && localX < ZoneGraph.SectorSize &&
                localY >= 0 && localY < ZoneGraph.SectorSize)
            {
                int cellIndex = localX + localY * ZoneGraph.SectorSize;
                view.Distances[cellIndex] = Fixed64.Zero;
                _dijkstraQueue.Value!.Enqueue((destTileX, destTileY), 0L);
            }
        }

        if (downstreamHandle.HasValue && portals.Count > 0)
        {
            var downstreamView = ZoneFlowData.Api.FromHandle(_poolRegistry, downstreamHandle.Value);
            if (downstreamView.IsAlive && downstreamView.IsComplete)
            {
                // Seed from ALL portals between zones (handles multiple gaps/passages)
                for (int portalIndex = 0; portalIndex < portals.Count; portalIndex++)
                {
                    SeedPortalFromDownstream(handle, portals[portalIndex], downstreamHandle.Value, sectorMinTileX, sectorMinTileY, tileSize, ignoreBuildings);
                }
            }
        }

        RunDijkstraAndComputeDirections(handle, zoneId, sectorMinTileX, sectorMinTileY, tileSize, ignoreBuildings);
    }

    private void SeedFromDownstreamZonesMultiTarget(ZoneFlowDataHandle handle, int zoneId, int sectorMinTileX, int sectorMinTileY, int tileSize)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        int? nearestSeedZoneId = FindNearestSeedZone();
        if (!nearestSeedZoneId.HasValue)
        {
            return;
        }

        var zonePath = _zoneGraph.FindZonePath(zoneId, nearestSeedZoneId.Value);
        if (zonePath == null || zonePath.Count <= 1)
        {
            return;
        }

        int nextZoneId = zonePath[1];
        // Get ALL portals between zones (handles multiple gaps/passages)
        var portals = _zoneGraph.FindAllPortalsBetweenZones(zoneId, nextZoneId);
        if (portals.Count == 0)
        {
            return;
        }

        if (!_multiTargetFlowCache.TryGetValue(nextZoneId, out var downstreamHandle))
        {
            return;
        }

        var downstreamView = ZoneFlowData.Api.FromHandle(_poolRegistry, downstreamHandle);
        if (!downstreamView.IsAlive || !downstreamView.IsComplete)
        {
            return;
        }

        // Seed from ALL portals
        for (int portalIndex = 0; portalIndex < portals.Count; portalIndex++)
        {
            SeedPortalFromDownstream(handle, portals[portalIndex], downstreamHandle, sectorMinTileX, sectorMinTileY, tileSize);
        }
    }

    private void SeedPortalFromDownstream(ZoneFlowDataHandle handle, ZonePortal portal, ZoneFlowDataHandle downstreamHandle, int sectorMinTileX, int sectorMinTileY, int tileSize, bool ignoreBuildings = false)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        var downstreamView = ZoneFlowData.Api.FromHandle(_poolRegistry, downstreamHandle);
        if (!downstreamView.IsAlive)
        {
            return;
        }

        int sectorX = view.SectorX;
        int sectorY = view.SectorY;
        int downstreamSectorX = downstreamView.SectorX;
        int downstreamSectorY = downstreamView.SectorY;

        // Direction from current sector to downstream sector
        int dirToDownstreamX = downstreamSectorX - sectorX;
        int dirToDownstreamY = downstreamSectorY - sectorY;

        // The portal tiles might be in either sector depending on which sector created it.
        // We need to find the edge tiles on OUR side of the boundary.
        // The downstream tiles are one step in the direction of the downstream sector.

        for (int portalTileY = portal.StartTileY; portalTileY <= portal.EndTileY; portalTileY++)
        {
            for (int portalTileX = portal.StartTileX; portalTileX <= portal.EndTileX; portalTileX++)
            {
                // Determine which tile is on OUR side and which is on the DOWNSTREAM side
                int ourTileX;
                int ourTileY;
                int downstreamTileX;
                int downstreamTileY;

                // Portal is a vertical line (same X for all tiles) - sectors differ in X
                if (dirToDownstreamX != 0)
                {
                    // If downstream is to the right (+X), our tile is the left one
                    // If downstream is to the left (-X), our tile is the right one
                    if (dirToDownstreamX > 0)
                    {
                        // Downstream is to the right
                        ourTileX = Math.Min(portalTileX, portalTileX - 1);
                        downstreamTileX = Math.Max(portalTileX, portalTileX + 1);
                    }
                    else
                    {
                        // Downstream is to the left
                        ourTileX = Math.Max(portalTileX, portalTileX + 1);
                        downstreamTileX = Math.Min(portalTileX, portalTileX - 1);
                    }
                    // Check which side the portal tile is on
                    int portalLocalX = portalTileX - sectorMinTileX;
                    if (portalLocalX >= 0 && portalLocalX < ZoneGraph.SectorSize)
                    {
                        // Portal tile is in our sector
                        ourTileX = portalTileX;
                        downstreamTileX = portalTileX + dirToDownstreamX;
                    }
                    else
                    {
                        // Portal tile is in downstream sector, our tile is one step back
                        ourTileX = portalTileX - dirToDownstreamX;
                        downstreamTileX = portalTileX;
                    }
                    ourTileY = portalTileY;
                    downstreamTileY = portalTileY;
                }
                // Portal is a horizontal line (same Y for all tiles) - sectors differ in Y
                else if (dirToDownstreamY != 0)
                {
                    // Check which side the portal tile is on
                    int portalLocalY = portalTileY - sectorMinTileY;
                    if (portalLocalY >= 0 && portalLocalY < ZoneGraph.SectorSize)
                    {
                        // Portal tile is in our sector
                        ourTileY = portalTileY;
                        downstreamTileY = portalTileY + dirToDownstreamY;
                    }
                    else
                    {
                        // Portal tile is in downstream sector, our tile is one step back
                        ourTileY = portalTileY - dirToDownstreamY;
                        downstreamTileY = portalTileY;
                    }
                    ourTileX = portalTileX;
                    downstreamTileX = portalTileX;
                }
                else
                {
                    // Same sector? Shouldn't happen for cross-sector portals
                    continue;
                }

                int localX = ourTileX - sectorMinTileX;
                int localY = ourTileY - sectorMinTileY;

                if (localX < 0 || localX >= ZoneGraph.SectorSize ||
                    localY < 0 || localY >= ZoneGraph.SectorSize)
                {
                    continue;
                }

                if (IsTileBlocked(ourTileX, ourTileY, tileSize, ignoreBuildings))
                {
                    continue;
                }

                Fixed64 downstreamCost = GetDistanceFromDownstream(downstreamHandle, downstreamTileX, downstreamTileY);
                if (downstreamCost >= MaxDistance)
                {
                    continue;
                }

                Fixed64 portalCost = downstreamCost + CardinalCost;

                int cellIndex = localX + localY * ZoneGraph.SectorSize;
                if (portalCost < view.Distances[cellIndex])
                {
                    view.Distances[cellIndex] = portalCost;
                    _dijkstraQueue.Value!.Enqueue((ourTileX, ourTileY), portalCost.Raw);
                }
            }
        }
    }

    private int? FindNearestSeedZone()
    {
        if (_currentSeeds == null || _currentSeeds.Count == 0)
        {
            return null;
        }

        Fixed64 lowestCost = MaxDistance;
        int? bestZoneId = null;

        for (int seedIndex = 0; seedIndex < _currentSeeds.Count; seedIndex++)
        {
            var (seedTileX, seedTileY, seedCost) = _currentSeeds[seedIndex];
            if (seedCost < lowestCost)
            {
                int? seedZoneId = _zoneGraph.GetZoneIdAtTile(seedTileX, seedTileY);
                if (seedZoneId.HasValue)
                {
                    lowestCost = seedCost;
                    bestZoneId = seedZoneId;
                }
            }
        }

        return bestZoneId;
    }

    private Fixed64 GetDistanceFromDownstream(ZoneFlowDataHandle handle, int tileX, int tileY)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive || !view.IsComplete)
        {
            return MaxDistance;
        }

        int localX = tileX - view.SectorX * ZoneGraph.SectorSize;
        int localY = tileY - view.SectorY * ZoneGraph.SectorSize;

        if (localX < 0 || localX >= ZoneGraph.SectorSize || localY < 0 || localY >= ZoneGraph.SectorSize)
        {
            return MaxDistance;
        }

        int cellIndex = localX + localY * ZoneGraph.SectorSize;
        return view.Distances[cellIndex];
    }

    private void RunDijkstraAndComputeDirections(ZoneFlowDataHandle handle, int zoneId, int sectorMinTileX, int sectorMinTileY, int tileSize, bool ignoreBuildings = false)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        var dijkstraQueue = _dijkstraQueue.Value!;
        var visited = _visited.Value!;

        if (dijkstraQueue.Count == 0)
        {
            view.IsComplete = true;
            return;
        }

        var sector = _zoneGraph.GetSector(view.SectorX, view.SectorY);
        if (!sector.HasValue)
        {
            view.IsComplete = true;
            return;
        }

        ReadOnlySpan<(int dx, int dy, bool isDiagonal)> neighbors = stackalloc (int, int, bool)[]
        {
            (-1, 0, false), (1, 0, false), (0, -1, false), (0, 1, false),
            (-1, -1, true), (1, -1, true), (-1, 1, true), (1, 1, true)
        };

        while (dijkstraQueue.Count > 0)
        {
            var (currentTileX, currentTileY) = dijkstraQueue.Dequeue();

            if (visited.Contains((currentTileX, currentTileY)))
            {
                continue;
            }
            visited.Add((currentTileX, currentTileY));

            int localX = currentTileX - sectorMinTileX;
            int localY = currentTileY - sectorMinTileY;

            if (localX < 0 || localX >= ZoneGraph.SectorSize || localY < 0 || localY >= ZoneGraph.SectorSize)
            {
                continue;
            }

            int cellZoneIndex = sector.Value.TileZoneIndices[localX + localY * ZoneGraph.SectorSize];
            if (cellZoneIndex < 0)
            {
                continue;
            }

            int cellZoneId = sector.Value.ZoneIds[cellZoneIndex];
            if (cellZoneId != zoneId)
            {
                continue;
            }

            int currentIndex = localX + localY * ZoneGraph.SectorSize;
            Fixed64 currentDistance = view.Distances[currentIndex];
            Fixed64 currentWallDistance = sector.Value.WallDistances[currentIndex];
            Fixed64 wallCost = WallCostFactor / (Fixed64.OneValue + currentWallDistance);

            for (int neighborIdx = 0; neighborIdx < neighbors.Length; neighborIdx++)
            {
                var (deltaX, deltaY, isDiagonal) = neighbors[neighborIdx];
                int neighborTileX = currentTileX + deltaX;
                int neighborTileY = currentTileY + deltaY;

                int neighborLocalX = neighborTileX - sectorMinTileX;
                int neighborLocalY = neighborTileY - sectorMinTileY;

                if (neighborLocalX < 0 || neighborLocalX >= ZoneGraph.SectorSize ||
                    neighborLocalY < 0 || neighborLocalY >= ZoneGraph.SectorSize)
                {
                    continue;
                }

                if (visited.Contains((neighborTileX, neighborTileY)))
                {
                    continue;
                }

                int neighborCellIndex = neighborLocalX + neighborLocalY * ZoneGraph.SectorSize;
                int neighborZoneIndex = sector.Value.TileZoneIndices[neighborCellIndex];
                if (neighborZoneIndex < 0)
                {
                    continue;
                }

                int neighborZoneId = sector.Value.ZoneIds[neighborZoneIndex];
                if (neighborZoneId != zoneId)
                {
                    continue;
                }

                if (isDiagonal)
                {
                    int adjX1 = currentTileX + deltaX;
                    int adjY1 = currentTileY;
                    int adjX2 = currentTileX;
                    int adjY2 = currentTileY + deltaY;

                    int adjLocalX1 = adjX1 - sectorMinTileX;
                    int adjLocalY1 = adjY1 - sectorMinTileY;
                    int adjLocalX2 = adjX2 - sectorMinTileX;
                    int adjLocalY2 = adjY2 - sectorMinTileY;

                    if (adjLocalX1 >= 0 && adjLocalX1 < ZoneGraph.SectorSize &&
                        adjLocalY1 >= 0 && adjLocalY1 < ZoneGraph.SectorSize)
                    {
                        int adjZoneIndex1 = sector.Value.TileZoneIndices[adjLocalX1 + adjLocalY1 * ZoneGraph.SectorSize];
                        if (adjZoneIndex1 < 0 || sector.Value.ZoneIds[adjZoneIndex1] != zoneId)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    if (adjLocalX2 >= 0 && adjLocalX2 < ZoneGraph.SectorSize &&
                        adjLocalY2 >= 0 && adjLocalY2 < ZoneGraph.SectorSize)
                    {
                        int adjZoneIndex2 = sector.Value.TileZoneIndices[adjLocalX2 + adjLocalY2 * ZoneGraph.SectorSize];
                        if (adjZoneIndex2 < 0 || sector.Value.ZoneIds[adjZoneIndex2] != zoneId)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                Fixed64 moveCost = isDiagonal ? DiagonalCost : CardinalCost;
                moveCost = moveCost + wallCost;

                Fixed64 newDistance = currentDistance + moveCost;

                Fixed64 existingDistance = view.Distances[neighborCellIndex];

                if (newDistance < existingDistance)
                {
                    view.Distances[neighborCellIndex] = newDistance;
                    dijkstraQueue.Enqueue((neighborTileX, neighborTileY), newDistance.Raw);
                }
            }
        }

        ComputeZoneFlowDirections(handle, sectorMinTileX, sectorMinTileY, tileSize, zoneId, sector.Value);
        view.IsComplete = true;
    }

    private void ComputeZoneFlowDirections(ZoneFlowDataHandle handle, int sectorMinTileX, int sectorMinTileY, int tileSize, int zoneId, ZoneSector sector)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return;
        }

        int sectorSize = ZoneGraph.SectorSize;

        for (int localY = 0; localY < sectorSize; localY++)
        {
            for (int localX = 0; localX < sectorSize; localX++)
            {
                int cellIndex = localX + localY * sectorSize;

                int zoneIndex = sector.TileZoneIndices[cellIndex];
                if (zoneIndex < 0)
                {
                    continue;
                }

                int cellZoneId = sector.ZoneIds[zoneIndex];
                if (cellZoneId != zoneId)
                {
                    continue;
                }

                Fixed64 currentDistance = view.Distances[cellIndex];

                if (currentDistance >= MaxDistance - Fixed64.OneValue)
                {
                    continue;
                }

                int tileX = sectorMinTileX + localX;
                int tileY = sectorMinTileY + localY;

                Fixed64 leftDist = GetNeighborDistance(view.Distances, sector, localX - 1, localY, sectorSize, zoneId, currentDistance);
                Fixed64 rightDist = GetNeighborDistance(view.Distances, sector, localX + 1, localY, sectorSize, zoneId, currentDistance);
                Fixed64 upDist = GetNeighborDistance(view.Distances, sector, localX, localY - 1, sectorSize, zoneId, currentDistance);
                Fixed64 downDist = GetNeighborDistance(view.Distances, sector, localX, localY + 1, sectorSize, zoneId, currentDistance);

                Fixed64 gradX = rightDist - leftDist;
                Fixed64 gradY = downDist - upDist;

                Fixed64 magnitudeSq = gradX * gradX + gradY * gradY;
                Fixed64 magnitude = Fixed64.Sqrt(magnitudeSq);

                Fixed64 dirX = Fixed64.Zero;
                Fixed64 dirY = Fixed64.Zero;

                if (magnitude > MinMagnitude)
                {
                    dirX = -gradX / magnitude;
                    dirY = -gradY / magnitude;
                }

                view.FlowCells[cellIndex] = new FlowCell
                {
                    Direction = new Fixed64Vec2(dirX, dirY),
                    Distance = currentDistance
                };
            }
        }
    }

    private Fixed64 GetNeighborDistance(Fixed64[] distances, ZoneSector sector, int localX, int localY, int sectorSize, int zoneId, Fixed64 fallback)
    {
        if (localX < 0 || localX >= sectorSize || localY < 0 || localY >= sectorSize)
        {
            return fallback;
        }

        int cellIndex = localX + localY * sectorSize;
        int zoneIndex = sector.TileZoneIndices[cellIndex];

        if (zoneIndex < 0)
        {
            return fallback;
        }

        int cellZoneId = sector.ZoneIds[zoneIndex];
        if (cellZoneId != zoneId)
        {
            return fallback;
        }

        Fixed64 neighborDist = distances[cellIndex];

        if (neighborDist >= MaxDistance - Fixed64.OneValue)
        {
            return fallback;
        }

        return neighborDist;
    }

    private Fixed64Vec2 GetZoneFlowDirection(ZoneFlowDataHandle handle, int tileX, int tileY)
    {
        var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
        if (!view.IsAlive)
        {
            return Fixed64Vec2.Zero;
        }

        if (!view.IsComplete)
        {
            return Fixed64Vec2.Zero;
        }

        int localX = tileX - view.SectorX * ZoneGraph.SectorSize;
        int localY = tileY - view.SectorY * ZoneGraph.SectorSize;

        if (localX < 0 || localX >= ZoneGraph.SectorSize || localY < 0 || localY >= ZoneGraph.SectorSize)
        {
            return Fixed64Vec2.Zero;
        }

        int cellIndex = localX + localY * ZoneGraph.SectorSize;
        var cell = view.FlowCells[cellIndex];

        return cell.Direction;
    }

    private Fixed64Vec2 FindDirectionToNearestSeed(int currentTileX, int currentTileY)
    {
        if (_currentSeeds == null || _currentSeeds.Count == 0)
        {
            return Fixed64Vec2.Zero;
        }

        Fixed64 nearestDistSquared = MaxDistance;
        int nearestTileX = currentTileX;
        int nearestTileY = currentTileY;

        for (int seedIndex = 0; seedIndex < _currentSeeds.Count; seedIndex++)
        {
            var (seedTileX, seedTileY, cost) = _currentSeeds[seedIndex];
            int deltaX = seedTileX - currentTileX;
            int deltaY = seedTileY - currentTileY;
            Fixed64 distSquared = Fixed64.FromInt(deltaX * deltaX + deltaY * deltaY) + cost;

            if (distSquared < nearestDistSquared)
            {
                nearestDistSquared = distSquared;
                nearestTileX = seedTileX;
                nearestTileY = seedTileY;
            }
        }

        return NormalizeDirection(Fixed64.FromInt(nearestTileX - currentTileX), Fixed64.FromInt(nearestTileY - currentTileY));
    }

    private static Fixed64Vec2 NormalizeDirection(Fixed64 deltaX, Fixed64 deltaY)
    {
        Fixed64 magnitudeSq = deltaX * deltaX + deltaY * deltaY;
        Fixed64 magnitude = Fixed64.Sqrt(magnitudeSq);
        if (magnitude > MinMagnitude)
        {
            return new Fixed64Vec2(deltaX / magnitude, deltaY / magnitude);
        }
        return Fixed64Vec2.Zero;
    }

    private bool IsTileBlocked(int tileX, int tileY, int tileSize, bool ignoreBuildings = false)
    {
        var blocked = _blocked.Value!;
        // Note: We don't cache when ignoreBuildings=true since the cache doesn't distinguish
        // between the two modes. This is fine since ignoreBuildings queries are less frequent.
        if (!ignoreBuildings && blocked.Contains((tileX, tileY)))
        {
            return true;
        }

        Fixed64 halfTileSize = Fixed64.FromInt(tileSize) / Fixed64.FromInt(2);
        Fixed64 worldX = Fixed64.FromInt(tileX * tileSize) + halfTileSize;
        Fixed64 worldY = Fixed64.FromInt(tileY * tileSize) + halfTileSize;

        bool isBlocked;
        if (ignoreBuildings)
        {
            // Only check terrain - ignore buildings
            isBlocked = _worldProvider.IsBlockedByTerrain(worldX, worldY);
        }
        else
        {
            // Check terrain and buildings
            isBlocked = _worldProvider.IsBlocked(worldX, worldY);
        }

        if (isBlocked)
        {
            if (!ignoreBuildings)
            {
                blocked.Add((tileX, tileY));
            }
            return true;
        }

        return false;
    }

    private FlowCell[] RentFlowCells()
    {
        if (_flowCellPool.TryPop(out var array))
        {
            int cellCount = ZoneGraph.SectorSize * ZoneGraph.SectorSize;
            for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
            {
                array[cellIndex] = new FlowCell { Direction = Fixed64Vec2.Zero, Distance = MaxDistance };
            }
            return array;
        }

        int newCellCount = ZoneGraph.SectorSize * ZoneGraph.SectorSize;
        var newArray = new FlowCell[newCellCount];
        for (int cellIndex = 0; cellIndex < newCellCount; cellIndex++)
        {
            newArray[cellIndex] = new FlowCell { Direction = Fixed64Vec2.Zero, Distance = MaxDistance };
        }
        return newArray;
    }

    private Fixed64[] RentDistances()
    {
        if (_distancePool.TryPop(out var array))
        {
            Array.Fill(array, MaxDistance);
            return array;
        }

        int cellCount = ZoneGraph.SectorSize * ZoneGraph.SectorSize;
        var newArray = new Fixed64[cellCount];
        Array.Fill(newArray, MaxDistance);
        return newArray;
    }

    private void ReturnFlowCells(FlowCell[] array)
    {
        _flowCellPool.Push(array);
    }

    private void ReturnDistances(Fixed64[] array)
    {
        _distancePool.Push(array);
    }

    /// <summary>
    /// Moves the zone to the front of the LRU list. MUST be called under _writeLock.
    /// Zero-allocation: uses index manipulation instead of LinkedListNode allocation.
    /// </summary>
    private void TouchMultiTarget(int zoneId)
    {
        if (!_multiTargetZoneToSlot.TryGetValue(zoneId, out int slot))
        {
            return;
        }

        // Already at head?
        if (_multiTargetLruHead == slot)
        {
            return;
        }

        // Unlink from current position
        int prev = _multiTargetLruSlots[slot].Prev;
        int next = _multiTargetLruSlots[slot].Next;

        if (prev >= 0)
        {
            _multiTargetLruSlots[prev].Next = next;
        }
        if (next >= 0)
        {
            _multiTargetLruSlots[next].Prev = prev;
        }
        if (_multiTargetLruTail == slot)
        {
            _multiTargetLruTail = prev;
        }

        // Link at head
        _multiTargetLruSlots[slot].Prev = -1;
        _multiTargetLruSlots[slot].Next = _multiTargetLruHead;
        if (_multiTargetLruHead >= 0)
        {
            _multiTargetLruSlots[_multiTargetLruHead].Prev = slot;
        }
        _multiTargetLruHead = slot;
    }

    /// <summary>
    /// Moves the key to the front of the LRU list. MUST be called under _writeLock.
    /// Zero-allocation: uses index manipulation instead of LinkedListNode allocation.
    /// </summary>
    private void TouchSingleDest((int, int, int, bool) key)
    {
        if (!_singleDestKeyToSlot.TryGetValue(key, out int slot))
        {
            return;
        }

        // Already at head?
        if (_singleDestLruHead == slot)
        {
            return;
        }

        // Unlink from current position
        int prev = _singleDestLruSlots[slot].Prev;
        int next = _singleDestLruSlots[slot].Next;

        if (prev >= 0)
        {
            _singleDestLruSlots[prev].Next = next;
        }
        if (next >= 0)
        {
            _singleDestLruSlots[next].Prev = prev;
        }
        if (_singleDestLruTail == slot)
        {
            _singleDestLruTail = prev;
        }

        // Link at head
        _singleDestLruSlots[slot].Prev = -1;
        _singleDestLruSlots[slot].Next = _singleDestLruHead;
        if (_singleDestLruHead >= 0)
        {
            _singleDestLruSlots[_singleDestLruHead].Prev = slot;
        }
        _singleDestLruHead = slot;
    }

    private void EvictOldestMultiTarget()
    {
        if (_multiTargetLruTail < 0)
        {
            return;
        }

        int slot = _multiTargetLruTail;
        int zoneId = _multiTargetSlotToZone[slot];
        EvictMultiTargetZone(zoneId);
    }

    private void EvictOldestSingleDest()
    {
        if (_singleDestLruTail < 0)
        {
            return;
        }

        int slot = _singleDestLruTail;
        var key = _singleDestSlotToKey[slot];
        EvictSingleDestZone(key);
    }

    private void EvictMultiTargetZone(int zoneId)
    {
        if (_multiTargetFlowCache.TryGetValue(zoneId, out var handle))
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
            if (view.IsAlive)
            {
                ReturnFlowCells(view.FlowCells);
                ReturnDistances(view.Distances);
            }
        }

        if (_multiTargetZoneToSlot.TryGetValue(zoneId, out int slot))
        {
            // Unlink from LRU list
            int prev = _multiTargetLruSlots[slot].Prev;
            int next = _multiTargetLruSlots[slot].Next;

            if (prev >= 0)
            {
                _multiTargetLruSlots[prev].Next = next;
            }
            else
            {
                _multiTargetLruHead = next;
            }

            if (next >= 0)
            {
                _multiTargetLruSlots[next].Prev = prev;
            }
            else
            {
                _multiTargetLruTail = prev;
            }

            // Return slot to free list
            _multiTargetLruSlots[slot].Next = _multiTargetLruFreeHead;
            _multiTargetLruSlots[slot].Prev = -1;
            _multiTargetSlotToZone[slot] = -1;
            _multiTargetLruFreeHead = slot;

            _multiTargetZoneToSlot.Remove(zoneId);
        }

        _multiTargetFlowCache.TryRemove(zoneId, out _);
    }

    private void EvictSingleDestZone((int, int, int, bool) key)
    {
        if (_singleDestFlowCache.TryGetValue(key, out var handle))
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
            if (view.IsAlive)
            {
                ReturnFlowCells(view.FlowCells);
                ReturnDistances(view.Distances);
            }
        }

        if (_singleDestKeyToSlot.TryGetValue(key, out int slot))
        {
            // Unlink from LRU list
            int prev = _singleDestLruSlots[slot].Prev;
            int next = _singleDestLruSlots[slot].Next;

            if (prev >= 0)
            {
                _singleDestLruSlots[prev].Next = next;
            }
            else
            {
                _singleDestLruHead = next;
            }

            if (next >= 0)
            {
                _singleDestLruSlots[next].Prev = prev;
            }
            else
            {
                _singleDestLruTail = prev;
            }

            // Return slot to free list
            _singleDestLruSlots[slot].Next = _singleDestLruFreeHead;
            _singleDestLruSlots[slot].Prev = -1;
            _singleDestSlotToKey[slot] = (-1, -1, -1, false);
            _singleDestLruFreeHead = slot;

            _singleDestKeyToSlot.Remove(key);
        }

        _singleDestFlowCache.TryRemove(key, out _);
    }

    private void TouchTargetSet((int zoneId, int targetsHash) key)
    {
        if (!_targetSetKeyToSlot.TryGetValue(key, out int slot))
        {
            return;
        }

        // Already at head?
        if (_targetSetLruHead == slot)
        {
            return;
        }

        // Unlink from current position
        int prev = _targetSetLruSlots[slot].Prev;
        int next = _targetSetLruSlots[slot].Next;

        if (prev >= 0)
        {
            _targetSetLruSlots[prev].Next = next;
        }
        if (next >= 0)
        {
            _targetSetLruSlots[next].Prev = prev;
        }
        if (_targetSetLruTail == slot)
        {
            _targetSetLruTail = prev;
        }

        // Link at head
        _targetSetLruSlots[slot].Prev = -1;
        _targetSetLruSlots[slot].Next = _targetSetLruHead;
        if (_targetSetLruHead >= 0)
        {
            _targetSetLruSlots[_targetSetLruHead].Prev = slot;
        }
        _targetSetLruHead = slot;
    }

    private void EvictOldestTargetSet()
    {
        if (_targetSetLruTail < 0)
        {
            return;
        }

        int slot = _targetSetLruTail;
        var key = _targetSetSlotToKey[slot];
        EvictTargetSetZone(key);
    }

    private void EvictTargetSetZone((int zoneId, int targetsHash) key)
    {
        if (_targetSetFlowCache.TryGetValue(key, out var handle))
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, handle);
            if (view.IsAlive)
            {
                ReturnFlowCells(view.FlowCells);
                ReturnDistances(view.Distances);
            }
        }

        if (_targetSetKeyToSlot.TryGetValue(key, out int slot))
        {
            // Unlink from LRU list
            int prev = _targetSetLruSlots[slot].Prev;
            int next = _targetSetLruSlots[slot].Next;

            if (prev >= 0)
            {
                _targetSetLruSlots[prev].Next = next;
            }
            else
            {
                _targetSetLruHead = next;
            }

            if (next >= 0)
            {
                _targetSetLruSlots[next].Prev = prev;
            }
            else
            {
                _targetSetLruTail = prev;
            }

            // Return slot to free list
            _targetSetLruSlots[slot].Next = _targetSetLruFreeHead;
            _targetSetLruSlots[slot].Prev = -1;
            _targetSetSlotToKey[slot] = (-1, -1);
            _targetSetLruFreeHead = slot;

            _targetSetKeyToSlot.Remove(key);
        }

        _targetSetFlowCache.TryRemove(key, out _);
    }

    private void ClearTargetSetFlows()
    {
        foreach (var kvp in _targetSetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive)
            {
                ReturnFlowCells(view.FlowCells);
                ReturnDistances(view.Distances);
            }
        }

        _targetSetFlowCache.Clear();
        _targetSetKeyToSlot.Clear();
        _targetSetLruHead = -1;
        _targetSetLruTail = -1;
        InitializeTargetSetFreeList();
    }

    private void ClearMultiTargetFlows()
    {
        foreach (var kvp in _multiTargetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive)
            {
                ReturnFlowCells(view.FlowCells);
                ReturnDistances(view.Distances);
            }
        }

        _multiTargetFlowCache.Clear();
        _multiTargetZoneToSlot.Clear();
        _multiTargetLruHead = -1;
        _multiTargetLruTail = -1;
        InitializeMultiTargetFreeList();
    }

    private void ClearSingleDestFlows()
    {
        foreach (var kvp in _singleDestFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive)
            {
                ReturnFlowCells(view.FlowCells);
                ReturnDistances(view.Distances);
            }
        }

        _singleDestFlowCache.Clear();
        _singleDestKeyToSlot.Clear();
        _singleDestLruHead = -1;
        _singleDestLruTail = -1;
        InitializeSingleDestFreeList();
    }

    private void ClearAllFlows()
    {
        ClearTargetSetFlows();
        ClearMultiTargetFlows();
        ClearSingleDestFlows();
    }

    /// <summary>
    /// Invalidates all cached flow fields. Call this after rollback or game restart
    /// to ensure stale flow directions are discarded.
    /// </summary>
    public void InvalidateAllFlows()
    {
        _flowsDirty = true;
        _pendingInvalidations.Clear();
        ClearSeeds();  // Clear attack target seeds - they need to be recalculated from SimWorld state
        ClearAllFlows();
        ClearRecentPaths();  // Clear cached zone paths - they become stale after rollback
    }

    public int GetZoneFlowCount()
    {
        return _multiTargetFlowCache.Count + _singleDestFlowCache.Count;
    }

    public ZoneGraph GetZoneGraph()
    {
        return _zoneGraph;
    }

    /// <summary>
    /// IZoneFlowService interface method - returns the zone graph as IZoneGraph.
    /// </summary>
    IZoneGraph IZoneFlowService.GetZoneGraph() => _zoneGraph;

    public void ClearRecentPaths()
    {
        _zoneGraph.ClearRecentPaths();
    }

    /// <summary>
    /// Invokes the callback for each cached zone flow. Zero-allocation alternative to IEnumerable.
    /// Note: To avoid delegate allocations, cache the callback as a static readonly field.
    /// </summary>
    public void ForEachCachedZoneFlow<TState>(TState state, Action<int, int, int, TState> callback)
    {
        foreach (var kvp in _multiTargetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive && view.IsComplete)
            {
                callback(kvp.Key, view.SectorX, view.SectorY, state);
            }
        }

        foreach (var kvp in _singleDestFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive && view.IsComplete)
            {
                callback(kvp.Key.zoneId, view.SectorX, view.SectorY, state);
            }
        }
    }

    /// <summary>
    /// Returns the count of cached zone flows without allocating.
    /// </summary>
    public int GetCachedZoneFlowCount()
    {
        int count = 0;
        foreach (var kvp in _multiTargetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive && view.IsComplete)
            {
                count++;
            }
        }

        foreach (var kvp in _singleDestFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (view.IsAlive && view.IsComplete)
            {
                count++;
            }
        }

        return count;
    }

    public Fixed64Vec2 GetCachedFlowDirectionDebug(int zoneId, int tileX, int tileY)
    {
        if (_multiTargetFlowCache.TryGetValue(zoneId, out var multiHandle))
        {
            var dir = GetZoneFlowDirection(multiHandle, tileX, tileY);
            if (dir != Fixed64Vec2.Zero)
            {
                return dir;
            }
        }

        foreach (var kvp in _singleDestFlowCache)
        {
            if (kvp.Key.zoneId == zoneId)
            {
                var dir = GetZoneFlowDirection(kvp.Value, tileX, tileY);
                if (dir != Fixed64Vec2.Zero)
                {
                    return dir;
                }
            }
        }

        return Fixed64Vec2.Zero;
    }

    public Fixed64Vec2 GetAnyCachedFlowDirection(int tileX, int tileY)
    {
        // Check target-set cache first (most recent player commands)
        foreach (var kvp in _targetSetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (!view.IsAlive || !view.IsComplete) continue;

            var dir = GetZoneFlowDirection(kvp.Value, tileX, tileY);
            if (dir != Fixed64Vec2.Zero)
            {
                return dir;
            }
        }

        // Check single-dest cache
        foreach (var kvp in _singleDestFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (!view.IsAlive || !view.IsComplete) continue;

            var dir = GetZoneFlowDirection(kvp.Value, tileX, tileY);
            if (dir != Fixed64Vec2.Zero)
            {
                return dir;
            }
        }

        // Check multi-target cache (enemy pathfinding)
        foreach (var kvp in _multiTargetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (!view.IsAlive || !view.IsComplete) continue;

            var dir = GetZoneFlowDirection(kvp.Value, tileX, tileY);
            if (dir != Fixed64Vec2.Zero)
            {
                return dir;
            }
        }

        return Fixed64Vec2.Zero;
    }

    public (Fixed64Vec2 direction, FlowCacheType cacheType) GetAnyCachedFlowDirectionWithType(int tileX, int tileY)
    {
        // Check target-set cache first (most recent player commands)
        foreach (var kvp in _targetSetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (!view.IsAlive || !view.IsComplete) continue;

            var dir = GetZoneFlowDirection(kvp.Value, tileX, tileY);
            if (dir != Fixed64Vec2.Zero)
            {
                return (dir, FlowCacheType.TargetSet);
            }
        }

        // Check single-dest cache
        foreach (var kvp in _singleDestFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (!view.IsAlive || !view.IsComplete) continue;

            var dir = GetZoneFlowDirection(kvp.Value, tileX, tileY);
            if (dir != Fixed64Vec2.Zero)
            {
                return (dir, FlowCacheType.SingleDest);
            }
        }

        // Check multi-target cache (enemy pathfinding)
        foreach (var kvp in _multiTargetFlowCache)
        {
            var view = ZoneFlowData.Api.FromHandle(_poolRegistry, kvp.Value);
            if (!view.IsAlive || !view.IsComplete) continue;

            var dir = GetZoneFlowDirection(kvp.Value, tileX, tileY);
            if (dir != Fixed64Vec2.Zero)
            {
                return (dir, FlowCacheType.MultiTarget);
            }
        }

        return (Fixed64Vec2.Zero, FlowCacheType.None);
    }
}

