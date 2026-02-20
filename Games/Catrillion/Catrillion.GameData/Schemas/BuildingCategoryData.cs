using System.Runtime.InteropServices;
using GameDocDatabase;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Static data for building categories (folders in the build UI).
/// </summary>
[GameDocTable("BuildingCategories")]
[StructLayout(LayoutKind.Sequential)]
public struct BuildingCategoryData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Category name for display (e.g., "Housing", "Economy").</summary>
    public GameDataId Name;

    /// <summary>Sort order in UI (lower = first).</summary>
    public int DisplayOrder;
}
