using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Links workshop buildings to available research items.
/// Each workshop building can have multiple research items (1:N relationship via BuildingTypeId).
/// </summary>
[GameDocTable("BuildingResearch")]
[StructLayout(LayoutKind.Sequential)]
public struct BuildingResearchData
{
    /// <summary>Unique mapping ID.</summary>
    [PrimaryKey]
    public int Id;

    /// <summary>Descriptive name for this mapping (for code generation).</summary>
    public GameDataId Name;

    /// <summary>Building type that can perform this research (FK to BuildingTypeData).</summary>
    [ForeignKey(typeof(BuildingTypeData))]
    public int BuildingTypeId;

    /// <summary>Research item available at this building (FK to ResearchItemData).</summary>
    [ForeignKey(typeof(ResearchItemData))]
    public int ResearchItemId;
}
