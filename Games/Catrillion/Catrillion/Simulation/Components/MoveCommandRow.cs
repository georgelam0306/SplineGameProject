using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

[SimTable(Capacity = 64, EvictionPolicy = EvictionPolicy.LRU, LRUKeyField = "IssuedFrame")]
public partial struct MoveCommandRow
{
    // Note: StableId from SimTable serves as the group identifier for units
    public Fixed64Vec2 Destination;
    public int IssuedFrame;
    public bool IsActive;

    /// <summary>
    /// Number of formation slot positions stored (max 1024).
    /// </summary>
    public int SlotCount;

    /// <summary>
    /// Formation slot X tile coordinates for multi-target flow field.
    /// </summary>
    [Array(1024)] public int SlotTileX;

    /// <summary>
    /// Formation slot Y tile coordinates for multi-target flow field.
    /// </summary>
    [Array(1024)] public int SlotTileY;
}
