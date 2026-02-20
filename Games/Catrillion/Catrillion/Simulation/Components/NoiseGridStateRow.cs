using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Global noise grid state for zombie pathfinding.
/// 32x32 grid covering the 256x256 tile map (each cell = 8x8 tiles = 256x256 pixels).
/// Noise levels decay over time and attract zombies toward noise sources.
/// </summary>
[SimDataTable]
public partial struct NoiseGridStateRow
{
    // 32x32 grid = 1024 cells total
    // Each cell covers 256x256 pixels of the 8192x8192 pixel map
    // Memory layout is contiguous row-major for blittable snapshotting
    [Array2D(32, 32)] public Fixed64 Noise;
}
