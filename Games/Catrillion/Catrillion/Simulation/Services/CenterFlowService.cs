using Core;
using FlowField;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Provides a permanent flow field toward the map center.
/// This is the default pathfinding target for zombies when no other targets exist.
/// Uses the IZoneFlowService's Dijkstra algorithm for proper obstacle handling.
/// </summary>
public sealed class CenterFlowService
{
    private readonly IZoneFlowService _flowService;
    private readonly IWorldProvider _worldProvider;

    private int _centerTileX;
    private int _centerTileY;
    private int _buildingWidth;
    private int _buildingHeight;
    private bool _initialized;
    private bool _useBuilding;

    public CenterFlowService(IZoneFlowService flowService, IWorldProvider worldProvider)
    {
        _flowService = flowService;
        _worldProvider = worldProvider;
    }

    /// <summary>
    /// Initialize with specific center coordinates.
    /// </summary>
    public void Initialize(int centerTileX, int centerTileY)
    {
        _centerTileX = centerTileX;
        _centerTileY = centerTileY;
        _buildingWidth = 0;
        _buildingHeight = 0;
        _useBuilding = false;
        _initialized = true;
    }

    /// <summary>
    /// Initialize with Command Center building dimensions for proper perimeter pathing.
    /// </summary>
    public void Initialize(int tileX, int tileY, int width, int height)
    {
        _centerTileX = tileX;
        _centerTileY = tileY;
        _buildingWidth = width;
        _buildingHeight = height;
        _useBuilding = true;
        _initialized = true;
    }

    public bool IsInitialized => _initialized;
    public int CenterTileX => _centerTileX;
    public int CenterTileY => _centerTileY;
    public int BuildingWidth => _buildingWidth;
    public int BuildingHeight => _buildingHeight;

    /// <summary>
    /// Get flow direction toward map center using Dijkstra flow field.
    /// </summary>
    /// <param name="worldPos">Current world position</param>
    /// <param name="ignoreBuildings">If true, ignores buildings when computing the flow (used for zombies)</param>
    public Fixed64Vec2 GetFlowDirection(Fixed64Vec2 worldPos, bool ignoreBuildings = false)
    {
        if (!_initialized)
        {
            return Fixed64Vec2.Zero;
        }

        // Use building perimeter pathing if initialized with building dimensions
        if (_useBuilding)
        {
            return _flowService.GetFlowDirectionForBuildingDestination(
                worldPos, _centerTileX, _centerTileY, _buildingWidth, _buildingHeight, ignoreBuildings);
        }

        // Use ZoneFlowService's single-destination flow field
        // This computes proper Dijkstra paths and handles obstacles
        return _flowService.GetFlowDirectionForDestination(worldPos, _centerTileX, _centerTileY, ignoreBuildings);
    }

    /// <summary>
    /// Get cached flow direction (doesn't compute new flows if not cached).
    /// More efficient for frequent queries when we know flow exists.
    /// </summary>
    /// <param name="worldPos">Current world position</param>
    /// <param name="ignoreBuildings">If true, ignores buildings when computing the flow (used for zombies)</param>
    public Fixed64Vec2 GetFlowDirectionCached(Fixed64Vec2 worldPos, bool ignoreBuildings = false)
    {
        if (!_initialized)
        {
            return Fixed64Vec2.Zero;
        }

        return _flowService.GetFlowDirectionForDestinationCached(worldPos, _centerTileX, _centerTileY, ignoreBuildings);
    }

    /// <summary>
    /// Get the center world position.
    /// </summary>
    public Fixed64Vec2 GetCenterWorldPosition()
    {
        int tileSize = _worldProvider.TileSize;
        Fixed64 halfTile = Fixed64.FromInt(tileSize / 2);
        return new Fixed64Vec2(
            Fixed64.FromInt(_centerTileX * tileSize) + halfTile,
            Fixed64.FromInt(_centerTileY * tileSize) + halfTile
        );
    }
}
