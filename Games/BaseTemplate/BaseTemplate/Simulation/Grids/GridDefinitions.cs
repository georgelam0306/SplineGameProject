using BaseTemplate.GameApp.Config;
using Grid;

// Assembly-level world grid definition - references GameConfig as the source of truth
[assembly: WorldGrid(
    TileSize = GameConfig.Map.TileSize,
    WidthTiles = GameConfig.Map.WidthTiles,
    HeightTiles = GameConfig.Map.HeightTiles,
    Namespace = "BaseTemplate.Simulation.Grids")]

namespace BaseTemplate.Simulation.Grids;

/// <summary>
/// Power grid: 256x256 cells, each 32px (1:1 with tiles).
/// Used for building placement validation - tiles within range of powered broadcasters.
/// </summary>
[DataGrid(
    CellSizePixels = GameConfig.PowerGrid.CellSizePixels,
    Dimensions = GameConfig.PowerGrid.Dimensions)]
public readonly partial struct PowerCell { }
