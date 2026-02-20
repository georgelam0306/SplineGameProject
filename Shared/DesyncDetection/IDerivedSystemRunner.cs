namespace DerpTech.DesyncDetection;

/// <summary>
/// Interface for derived system management.
/// Derived systems compute cached data from SimWorld (e.g., occupancy grids, power networks).
/// Must be invalidated after snapshot restore to force recalculation.
/// </summary>
public interface IDerivedSystemRunner
{
    /// <summary>
    /// Marks all derived data as invalid, forcing recalculation on next access.
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Immediately rebuilds all derived data from current SimWorld state.
    /// </summary>
    void RebuildAll();
}
