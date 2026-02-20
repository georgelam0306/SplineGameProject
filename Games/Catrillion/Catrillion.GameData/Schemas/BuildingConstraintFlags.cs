using System;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Bitwise flags for building constraint categories.
/// Used with RequiresNearbyBuildingFlag/ExcludesNearbyBuildingFlag for flexible placement rules.
/// </summary>
[Flags]
public enum BuildingConstraintFlags : uint
{
    None = 0,

    /// <summary>Building produces power (e.g., Command Center, Power Plant).</summary>
    ProducesPower = 1 << 0,

    /// <summary>Building consumes power and needs to be within power connection range.</summary>
    RequiresPowerConnection = 1 << 1,

    /// <summary>Building can relay power to other buildings within its connection radius.</summary>
    RelaysPower = 1 << 2,

    /// <summary>Defensive structure (walls, turrets).</summary>
    Defensive = 1 << 3,

    /// <summary>Resource production building (mines, farms, etc.).</summary>
    ResourceProduction = 1 << 4,

    /// <summary>Storage building (warehouses, silos).</summary>
    Storage = 1 << 5,
}
