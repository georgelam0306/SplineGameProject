#nullable enable
using Microsoft.CodeAnalysis;

namespace Grid.Generator;

/// <summary>
/// Model for the [WorldGrid] assembly-level attribute.
/// </summary>
internal sealed class WorldGridModel
{
    public int TileSize { get; }
    public int WidthTiles { get; }
    public int HeightTiles { get; }
    public string Namespace { get; }

    public WorldGridModel(int tileSize, int widthTiles, int heightTiles, string ns)
    {
        TileSize = tileSize;
        WidthTiles = widthTiles;
        HeightTiles = heightTiles;
        Namespace = ns;
    }

    // Derived constants
    public int MapWidthPixels => WidthTiles * TileSize;
    public int MapHeightPixels => HeightTiles * TileSize;
}

/// <summary>
/// Model for the [DataGrid] struct-level attribute.
/// </summary>
internal sealed class DataGridModel
{
    public string TypeName { get; }
    public int CellSizePixels { get; }
    public int Dimensions { get; }
    public string Namespace { get; }
    public INamedTypeSymbol TypeSymbol { get; }

    public DataGridModel(string typeName, int cellSizePixels, int dimensions, string ns, INamedTypeSymbol typeSymbol)
    {
        TypeName = typeName;
        CellSizePixels = cellSizePixels;
        Dimensions = dimensions;
        Namespace = ns;
        TypeSymbol = typeSymbol;
    }
}
