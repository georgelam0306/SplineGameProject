using Friflo.Engine.ECS;
using Catrillion.Rendering.DualGrid.Utilities;

namespace Catrillion.Rendering.DualGrid.Components;

public struct TerrainGrid : IComponent
{
    public int Width;
    public int Height;
    public TerrainType[]? Cells;
}

