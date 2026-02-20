using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Core;
using SimTable;

namespace Catrillion.Simulation.DerivedSystems;

/// <summary>
/// Derived state for power grid. NOT serialized - rebuilt from BuildingRows on rollback.
/// Uses flood-fill from power generators to determine which tiles have power coverage.
/// 256x256 grid matching the tile map (1 cell = 1 tile = 32x32 pixels).
/// </summary>
public sealed class PowerGrid : IDerivedSimSystem
{
    private readonly SimWorld _world;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly int _tileSize;

    private readonly byte[] _isPowered;
    private bool _dirty = true;

    // Temporary arrays for flood-fill (reused to avoid allocations)
    private readonly bool[] _visited;
    private readonly int[] _queue;
    private int _queueHead;
    private int _queueTail;

    public PowerGrid(SimWorld world, GameDataManager<GameDocDb> gameData)
    {
        _world = world;
        _gameData = gameData;
        _tileSize = gameData.Db.MapConfigData.FindById(0).TileSize;

        _isPowered = new byte[PowerCell.GridWidth * PowerCell.GridHeight];
        _visited = new bool[1000]; // Max buildings
        _queue = new int[1000];
    }

    /// <summary>
    /// Mark power grid as dirty. Called after rollback/snapshot restore.
    /// </summary>
    public void Invalidate()
    {
        _dirty = true;
    }

    /// <summary>
    /// Rebuild power grid from authoritative BuildingRow data if dirty.
    /// Uses flood-fill from power generators, then marks tiles within their range.
    /// NOTE: This only populates the tile grid - it does NOT modify BuildingRow flags.
    /// The IsPowered flag on buildings is set by ModifierApplicationSystem.
    /// </summary>
    public void Rebuild()
    {
        if (!_dirty) return;
        _dirty = false;

        var buildings = _world.BuildingRows;
        var db = _gameData.Db;

        // Reset visited array
        Array.Clear(_visited, 0, Math.Min(_visited.Length, buildings.Count));

        // Clear the power grid
        Array.Clear(_isPowered, 0, _isPowered.Length);

        // First pass: Find all generators and add to queue
        _queueHead = 0;
        _queueTail = 0;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            ref readonly var typeData = ref db.BuildingTypeData.FindById((int)row.TypeId);

            // Generators (negative power consumption) seed the flood-fill
            if (typeData.PowerConsumption < 0)
            {
                _visited[slot] = true;
                _queue[_queueTail++] = slot;
            }
        }

        // Flood-fill from generators to mark powered tiles
        while (_queueHead < _queueTail)
        {
            int currentSlot = _queue[_queueHead++];
            if (!buildings.TryGetRow(currentSlot, out var current)) continue;

            ref readonly var currentType = ref db.BuildingTypeData.FindById((int)current.TypeId);
            var connectionRadius = currentType.PowerConnectionRadius;

            // Skip if this building can't relay power
            if (connectionRadius <= Fixed64.Zero) continue;

            // Mark tiles within this building's power range
            MarkPoweredTiles(current.Position, connectionRadius);

            // Find buildings within connection radius and add to queue
            foreach (int neighborSlot in buildings.QueryRadius(current.Position, connectionRadius))
            {
                if (_visited[neighborSlot]) continue;

                if (!buildings.TryGetRow(neighborSlot, out var neighbor)) continue;
                if (!neighbor.Flags.HasFlag(BuildingFlags.IsActive)) continue;

                _visited[neighborSlot] = true;
                _queue[_queueTail++] = neighborSlot;
            }
        }
    }

    /// <summary>
    /// Marks all tiles within radius of the given position as powered.
    /// </summary>
    private void MarkPoweredTiles(Fixed64Vec2 center, Fixed64 radius)
    {
        float centerX = center.X.ToFloat();
        float centerY = center.Y.ToFloat();
        float radiusF = radius.ToFloat();
        float radiusSq = radiusF * radiusF;

        // Calculate bounding box in tile coordinates
        int minTileX = Math.Max(0, (int)((centerX - radiusF) / _tileSize));
        int minTileY = Math.Max(0, (int)((centerY - radiusF) / _tileSize));
        int maxTileX = Math.Min(PowerCell.GridWidth - 1, (int)((centerX + radiusF) / _tileSize));
        int maxTileY = Math.Min(PowerCell.GridHeight - 1, (int)((centerY + radiusF) / _tileSize));

        // Mark tiles within radius
        for (int tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                // Check if tile center is within radius
                float tileCenterX = tileX * _tileSize + _tileSize / 2.0f;
                float tileCenterY = tileY * _tileSize + _tileSize / 2.0f;

                float dx = tileCenterX - centerX;
                float dy = tileCenterY - centerY;
                float distSq = dx * dx + dy * dy;

                if (distSq <= radiusSq)
                {
                    _isPowered[tileY * PowerCell.GridWidth + tileX] = 1;
                }
            }
        }
    }

    /// <summary>
    /// Checks if a tile has power coverage. O(1) lookup.
    /// </summary>
    public bool IsTilePowered(int tileX, int tileY)
    {
        if (tileX < 0 || tileX >= PowerCell.GridWidth) return false;
        if (tileY < 0 || tileY >= PowerCell.GridHeight) return false;
        return _isPowered[tileY * PowerCell.GridWidth + tileX] != 0;
    }

    /// <summary>
    /// Checks if a building is connected to power.
    /// O(1) lookup - checks if any tile of the building footprint has power.
    /// </summary>
    public bool IsBuildingPowered(int tileX, int tileY, int width, int height)
    {
        for (int ty = tileY; ty < tileY + height; ty++)
        {
            if (ty < 0 || ty >= PowerCell.GridHeight) continue;

            for (int tx = tileX; tx < tileX + width; tx++)
            {
                if (tx < 0 || tx >= PowerCell.GridWidth) continue;

                if (_isPowered[ty * PowerCell.GridWidth + tx] != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a hypothetical building placement would be connected to power.
    /// O(1) lookup - checks if any tile of the building footprint has power.
    /// </summary>
    public bool WouldBePowered(int tileX, int tileY, int width, int height)
    {
        for (int ty = tileY; ty < tileY + height; ty++)
        {
            if (ty < 0 || ty >= PowerCell.GridHeight) continue;

            for (int tx = tileX; tx < tileX + width; tx++)
            {
                if (tx < 0 || tx >= PowerCell.GridWidth) continue;

                if (_isPowered[ty * PowerCell.GridWidth + tx] != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
