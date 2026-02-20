namespace SimTable;

/// <summary>
/// Interface for derived systems that cache computed state from SimWorld.
/// Derived systems are invalidated after snapshot restore and rebuilt on demand.
/// </summary>
public interface IDerivedSimSystem
{
    /// <summary>
    /// Mark all cached state as dirty. Called after rollback/snapshot restore.
    /// </summary>
    void Invalidate();

    /// <summary>
    /// Rebuild cached state from authoritative data if dirty.
    /// Called every BeginFrame in registered order.
    /// </summary>
    void Rebuild();
}
