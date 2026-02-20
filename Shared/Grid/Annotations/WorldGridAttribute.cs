using System;

namespace Grid;

/// <summary>
/// Defines the world grid configuration for the entire assembly.
/// Only one per assembly. Generates the WorldTile coordinate type.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class WorldGridAttribute : Attribute
{
    /// <summary>Size of each tile in pixels (e.g., 32).</summary>
    public int TileSize { get; set; } = 32;

    /// <summary>Map width in tiles (e.g., 256).</summary>
    public int WidthTiles { get; set; } = 256;

    /// <summary>Map height in tiles (e.g., 256).</summary>
    public int HeightTiles { get; set; } = 256;

    /// <summary>Namespace for generated types. Defaults to "Grid.Generated".</summary>
    public string Namespace { get; set; } = "Grid.Generated";
}
