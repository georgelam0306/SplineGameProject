using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Core;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Service for zone/tier lookup and zombie type filtering.
/// Provides distance-based tier calculations for procedural generation.
/// </summary>
public sealed class ZoneTierService
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly Fixed64 _mapCenterX;
    private readonly Fixed64 _mapCenterY;
    private readonly Fixed64 _mapHalfSize;  // In pixels

    // Cached zone boundary distances squared (for fast lookup)
    private readonly Fixed64[] _zoneBoundariesSq;
    private readonly int _tierCount;

    public ZoneTierService(GameDataManager<GameDocDb> gameData)
    {
        _gameData = gameData;

        // Load map config
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        int mapWidthPx = mapConfig.WidthTiles * mapConfig.TileSize;
        int mapHeightPx = mapConfig.HeightTiles * mapConfig.TileSize;

        _mapCenterX = Fixed64.FromInt(mapWidthPx / 2);
        _mapCenterY = Fixed64.FromInt(mapHeightPx / 2);

        // Use the smaller dimension's half as the "radius" for zone calculations
        int halfSize = (mapWidthPx < mapHeightPx ? mapWidthPx : mapHeightPx) / 2;
        _mapHalfSize = Fixed64.FromInt(halfSize);

        // Cache zone boundaries
        // Assumes tiers are defined with Id 0, 1, 2, 3
        _tierCount = 4;
        _zoneBoundariesSq = new Fixed64[_tierCount];

        for (int i = 0; i < _tierCount; i++)
        {
            ref readonly var tierConfig = ref gameData.Db.ZoneTierConfigData.FindById(i);
            Fixed64 outerRadius = _mapHalfSize * tierConfig.OuterRadiusRatio;
            _zoneBoundariesSq[i] = outerRadius * outerRadius;
        }
    }

    /// <summary>
    /// Returns the tier (0-3) for a world position.
    /// Tier 0 = safe zone (center), Tier 3 = outer edge.
    /// </summary>
    public int GetTierAt(Fixed64 worldX, Fixed64 worldY)
    {
        Fixed64 dx = worldX - _mapCenterX;
        Fixed64 dy = worldY - _mapCenterY;
        Fixed64 distSq = dx * dx + dy * dy;

        // Find which tier this distance falls into
        for (int tier = 0; tier < _tierCount; tier++)
        {
            if (distSq <= _zoneBoundariesSq[tier])
            {
                return tier;
            }
        }

        // Beyond all defined zones - return outer tier
        return _tierCount - 1;
    }

    /// <summary>
    /// Returns the tier (0-3) for a tile position.
    /// </summary>
    public int GetTierAtTile(int tileX, int tileY, int tileSize)
    {
        Fixed64 worldX = Fixed64.FromInt(tileX * tileSize + tileSize / 2);
        Fixed64 worldY = Fixed64.FromInt(tileY * tileSize + tileSize / 2);
        return GetTierAt(worldX, worldY);
    }

    /// <summary>
    /// Checks if a zombie type is allowed in the given tier.
    /// </summary>
    public bool IsZombieTypeAllowed(int tierId, ZombieTypeId typeId)
    {
        ref readonly var tierConfig = ref _gameData.Db.ZoneTierConfigData.FindById(tierId);
        int typeBit = 1 << (int)typeId;
        return (tierConfig.AllowedZombieTypesMask & typeBit) != 0;
    }

    /// <summary>
    /// Gets the allowed zombie types mask for a tier.
    /// </summary>
    public int GetAllowedTypesMask(int tierId)
    {
        ref readonly var tierConfig = ref _gameData.Db.ZoneTierConfigData.FindById(tierId);
        return tierConfig.AllowedZombieTypesMask;
    }

    /// <summary>
    /// Gets the zombie density multiplier for a tier.
    /// </summary>
    public Fixed64 GetZombieDensityMultiplier(int tierId)
    {
        ref readonly var tierConfig = ref _gameData.Db.ZoneTierConfigData.FindById(tierId);
        return tierConfig.ZombieDensityMultiplier;
    }

    /// <summary>
    /// Gets the resource density multiplier for a tier.
    /// </summary>
    public Fixed64 GetResourceDensityMultiplier(int tierId)
    {
        ref readonly var tierConfig = ref _gameData.Db.ZoneTierConfigData.FindById(tierId);
        return tierConfig.ResourceDensityMultiplier;
    }

    /// <summary>
    /// Gets the tier config data for a tier.
    /// </summary>
    public ref readonly ZoneTierConfigData GetTierConfig(int tierId)
    {
        return ref _gameData.Db.ZoneTierConfigData.FindById(tierId);
    }

    /// <summary>
    /// Gets the map center position in world coordinates.
    /// </summary>
    public Fixed64Vec2 GetMapCenter()
    {
        return new Fixed64Vec2(_mapCenterX, _mapCenterY);
    }

    /// <summary>
    /// Gets the map half size (radius for zone calculations).
    /// </summary>
    public Fixed64 GetMapHalfSize()
    {
        return _mapHalfSize;
    }

    /// <summary>
    /// Gets the total number of tiers.
    /// </summary>
    public int TierCount => _tierCount;
}
