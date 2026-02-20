using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Resource deposits on the map that can be harvested.
/// Uses tile coordinates for static placement.
/// </summary>
[SimTable(Capacity = 500)]
public partial struct ResourceNodeRow
{
    // Tile position
    public ushort TileX;
    public ushort TileY;

    // Resource type and amount
    public ResourceTypeId TypeId;
    public int RemainingAmount;
    public int MaxAmount;
    public int HarvestRate;        // Amount per harvest tick
    public byte HarvesterCount;    // Currently assigned harvesters

    // State
    public ResourceNodeFlags Flags;
}
