using Catrillion.Simulation.DerivedSystems;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Facade for power network queries. Delegates to PowerGrid derived system.
/// </summary>
public sealed class PowerNetworkService
{
    private readonly PowerGrid _powerGrid;

    public PowerNetworkService(PowerGrid powerGrid)
    {
        _powerGrid = powerGrid;
    }

    /// <summary>
    /// Marks the power network as needing recalculation.
    /// Call this when buildings are added or removed.
    /// </summary>
    public void InvalidateNetwork()
    {
        _powerGrid.Invalidate();
    }

    /// <summary>
    /// Checks if a hypothetical building placement would be connected to power.
    /// O(1) lookup using the power grid.
    /// </summary>
    public bool WouldBePowered(int tileX, int tileY, int width, int height)
    {
        return _powerGrid.WouldBePowered(tileX, tileY, width, height);
    }

    /// <summary>
    /// Checks if a specific tile has power.
    /// </summary>
    public bool IsTilePowered(int tileX, int tileY)
    {
        return _powerGrid.IsTilePowered(tileX, tileY);
    }

    /// <summary>
    /// Checks if a building at the given tile position is connected to power.
    /// </summary>
    public bool IsBuildingPowered(int tileX, int tileY, int width, int height)
    {
        return _powerGrid.IsBuildingPowered(tileX, tileY, width, height);
    }
}
