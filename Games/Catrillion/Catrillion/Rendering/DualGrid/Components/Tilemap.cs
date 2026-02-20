using Friflo.Engine.ECS;

namespace Catrillion.Rendering.DualGrid.Components;

public struct Tilemap : IComponent
{
    public int Width;
    public int Height;
    public int TileSize;
    public int[]? Tiles;
}

