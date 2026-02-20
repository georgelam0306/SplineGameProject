using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Global threat grid state for zombie AI decision making.
/// 64x64 grid covering the 256x256 tile map (each cell = 4x4 tiles = 128x128 pixels).
/// Threat accumulates from player proximity, noise events, and combat.
/// Separate from noise grid - threat persists longer and influences state transitions.
/// </summary>
[SimDataTable]
public partial struct ThreatGridStateRow
{
    // 64x64 grid = 4096 cells total
    // Each cell covers 128x128 pixels of the 8192x8192 pixel map
    // Memory layout is contiguous row-major for blittable snapshotting

    /// <summary>
    /// Current threat level per cell. Decays at ThreatDecayRate per frame.
    /// </summary>
    [Array2D(64, 64)] public Fixed64 Threat;

    /// <summary>
    /// Peak threat level per cell (memory). Decays slower than current threat.
    /// Used for zombie "memory" - they remember where threats were.
    /// </summary>
    [Array2D(64, 64)] public Fixed64 PeakThreat;
}
