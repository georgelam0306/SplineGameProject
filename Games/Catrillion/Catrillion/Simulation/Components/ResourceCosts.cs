using System;
using System.Runtime.InteropServices;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Multi-resource cost bundle for building/unit costs and upkeep.
/// Value type for zero-allocation passing.
/// Works with global GameResourcesRow (shared base co-op).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ResourceCosts
{
    public int Gold;
    public int Wood;
    public int Stone;
    public int Iron;
    public int Oil;

    public static ResourceCosts Zero => default;

    public bool IsZero => Gold == 0 && Wood == 0 && Stone == 0 && Iron == 0 && Oil == 0;

    /// <summary>
    /// Returns true if global resources have at least this amount of each resource.
    /// </summary>
    public bool CanAfford(ref GameResourcesRow resources)
    {
        return resources.Gold >= Gold
            && resources.Wood >= Wood
            && resources.Stone >= Stone
            && resources.Iron >= Iron
            && resources.Oil >= Oil;
    }

    /// <summary>
    /// Deducts resources from global pool. Call CanAfford first!
    /// </summary>
    public void Deduct(ref GameResourcesRow resources)
    {
        resources.Gold -= Gold;
        resources.Wood -= Wood;
        resources.Stone -= Stone;
        resources.Iron -= Iron;
        resources.Oil -= Oil;
    }

    /// <summary>
    /// Adds resources to global pool, clamping to max storage.
    /// </summary>
    public void AddClamped(ref GameResourcesRow resources)
    {
        resources.Gold = Math.Min(resources.Gold + Gold, resources.MaxGold);
        resources.Wood = Math.Min(resources.Wood + Wood, resources.MaxWood);
        resources.Stone = Math.Min(resources.Stone + Stone, resources.MaxStone);
        resources.Iron = Math.Min(resources.Iron + Iron, resources.MaxIron);
        resources.Oil = Math.Min(resources.Oil + Oil, resources.MaxOil);
    }

    /// <summary>
    /// Adds resources to global pool without clamping.
    /// </summary>
    public void Add(ref GameResourcesRow resources)
    {
        resources.Gold += Gold;
        resources.Wood += Wood;
        resources.Stone += Stone;
        resources.Iron += Iron;
        resources.Oil += Oil;
    }
}
