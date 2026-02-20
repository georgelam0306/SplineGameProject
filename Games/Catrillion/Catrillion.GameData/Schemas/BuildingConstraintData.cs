using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Defines placement constraints for buildings.
/// Each building can have multiple constraints (1:N relationship via BuildingTypeId).
/// </summary>
[GameDocTable("BuildingConstraints")]
[StructLayout(LayoutKind.Sequential)]
public struct BuildingConstraintData
{
    /// <summary>Unique constraint ID.</summary>
    [PrimaryKey]
    public int Id;

    /// <summary>Descriptive name for this constraint (for registry lookup).</summary>
    public GameDataId Name;

    /// <summary>Building type this constraint applies to (FK to BuildingTypeData).</summary>
    [ForeignKey(typeof(BuildingTypeData))]
    public int BuildingTypeId;

    /// <summary>Type of constraint to check.</summary>
    public ConstraintType Type;

    /// <summary>Target ID for type-based constraints (BuildingTypeId or ResourceTypeId).</summary>
    public int TargetId;

    /// <summary>Target flags for flag-based constraints.</summary>
    public BuildingConstraintFlags TargetFlags;

    /// <summary>Search radius in world units (pixels). 0 = adjacent tiles only.</summary>
    public Fixed64 Radius;

    /// <summary>Minimum count required. 0 = at least 1 must exist.</summary>
    public int MinCount;

    /// <summary>Maximum count allowed. -1 = unlimited.</summary>
    public int MaxCount;

    /// <summary>Localized message ID to show when constraint fails.</summary>
    public GameDataId FailureMessageId;
}
