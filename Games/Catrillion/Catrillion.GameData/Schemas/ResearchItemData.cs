using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Static data for research items that can be researched at workshop buildings.
/// </summary>
[GameDocTable("ResearchItems")]
[StructLayout(LayoutKind.Sequential)]
public struct ResearchItemData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Name for display and debugging.</summary>
    public GameDataId Name;

    /// <summary>Display name for UI (preserved as string at runtime).</summary>
    public StringHandle DisplayName;

    /// <summary>3-letter abbreviation for UI display.</summary>
    public StringHandle Abbreviation;

    /// <summary>Tech ID to unlock upon completion (bit index in UnlockedTech, 0-63).</summary>
    public int UnlocksTechId;

    /// <summary>Research time in frames (60 = 1 second).</summary>
    public int ResearchTime;

    // === Research Costs ===
    /// <summary>Gold cost to research.</summary>
    public int CostGold;
    /// <summary>Wood cost to research.</summary>
    public int CostWood;
    /// <summary>Stone cost to research.</summary>
    public int CostStone;
    /// <summary>Iron cost to research.</summary>
    public int CostIron;
    /// <summary>Oil cost to research.</summary>
    public int CostOil;

    /// <summary>Required tech ID to be available for research (-1 = no prerequisite).</summary>
    public int PrerequisiteTechId;

    /// <summary>Display order within workshop (lower = first).</summary>
    public int DisplayOrder;
}
