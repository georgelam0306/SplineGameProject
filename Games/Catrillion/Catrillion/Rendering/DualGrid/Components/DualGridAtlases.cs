using Friflo.Engine.ECS;
using Raylib_cs;

namespace Catrillion.Rendering.DualGrid.Components;

public struct DualGridAtlases : IComponent
{
    public Texture2D Grass;
    public Texture2D Dirt;
    public Texture2D Water;
    public Texture2D Mountain;
    public bool Loaded;

    public int SrcTileWidth;
    public int SrcTileHeight;
    public int Columns;
}

