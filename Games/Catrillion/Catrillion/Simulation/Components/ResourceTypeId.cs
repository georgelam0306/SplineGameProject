namespace Catrillion.Simulation.Components;

/// <summary>
/// Resource types for economy system.
/// </summary>
public enum ResourceTypeId : byte
{
    Gold = 0,
    Energy = 1,  // Power grid (not stockpiled)
    Wood = 2,
    Stone = 3,
    Iron = 4,
    Oil = 5
}
