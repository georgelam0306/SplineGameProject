using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Global fog of war visibility grid state.
/// 256x256 grid matching the tile map (1 cell = 1 tile = 32x32 pixels).
/// Each cell stores visibility state: 0 = unexplored, 1 = visible, 2 = fogged.
/// </summary>
[SimDataTable]
public partial struct FogOfWarGridStateRow
{
    // 256x256 grid = 65536 cells total
    // Each cell is 1 byte: 0 = unexplored, 1 = visible, 2 = fogged
    // Memory: 64KB for snapshotting
    [Array2D(256, 256)] public byte Visibility;
}
