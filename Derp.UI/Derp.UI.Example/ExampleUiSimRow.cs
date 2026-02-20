using SimTable;

namespace Derp.UI.Example;

[SimTable(Capacity = 16, CellSize = 4, GridSize = 16)]
public partial struct ExampleUiSimRow
{
    [Column] public int Id;
    [Column] public int Counter;
}

