using Core;

namespace FlowField;

/// <summary>
/// Interface for providing world data to the flow field system.
/// Games implement this to bridge their world/structure services.
/// All coordinates use Fixed64 for cross-platform determinism.
/// </summary>
public interface IWorldProvider
{
    /// <summary>
    /// Size of each tile in world units.
    /// </summary>
    int TileSize { get; }

    /// <summary>
    /// Returns true if the world position is blocked (unwalkable).
    /// Checks both terrain and buildings.
    /// </summary>
    bool IsBlocked(Fixed64 worldX, Fixed64 worldY);

    /// <summary>
    /// Returns true if the world position is blocked by terrain only (water, mountains).
    /// Ignores buildings - used for zombie pathfinding.
    /// </summary>
    bool IsBlockedByTerrain(Fixed64 worldX, Fixed64 worldY);
}
