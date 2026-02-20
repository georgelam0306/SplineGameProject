using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Singleton row containing flow field service state that needs to be snapshotted.
/// Includes seed targets, dirty flags, and pending sector invalidations.
/// </summary>
[SimDataTable]
public partial struct FlowFieldStateRow
{
    /// <summary>
    /// Whether flow fields need recomputation.
    /// </summary>
    public bool FlowsDirty;

    /// <summary>
    /// Number of active seed targets (max 16).
    /// </summary>
    public int SeedCount;

    /// <summary>
    /// Seed target tile X coordinates.
    /// </summary>
    [Array(16)] public int SeedTileX;

    /// <summary>
    /// Seed target tile Y coordinates.
    /// </summary>
    [Array(16)] public int SeedTileY;

    /// <summary>
    /// Seed target costs (for weighted pathfinding).
    /// </summary>
    [Array(16)] public Fixed64 SeedCost;

    /// <summary>
    /// Number of sectors pending invalidation (max 64).
    /// </summary>
    public int PendingInvalidationCount;

    /// <summary>
    /// Pending invalidation sector X coordinates.
    /// </summary>
    [Array(64)] public int PendingSectorX;

    /// <summary>
    /// Pending invalidation sector Y coordinates.
    /// </summary>
    [Array(64)] public int PendingSectorY;

    /// <summary>
    /// Hash of current seeds for change detection.
    /// </summary>
    public int SeedsHash;
}
