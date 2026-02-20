using System;

namespace Grid;

/// <summary>
/// Marks a readonly partial struct as a data grid coordinate type.
/// The generator will add conversion methods and constants.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
public sealed class DataGridAttribute : Attribute
{
    /// <summary>Size of each cell in pixels (e.g., 256 for noise grid, 128 for threat grid).</summary>
    public int CellSizePixels { get; set; }

    /// <summary>Grid width and height in cells (e.g., 32 for a 32x32 grid).</summary>
    public int Dimensions { get; set; }
}
