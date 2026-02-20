using Friflo.Engine.ECS;
using Raylib_cs;

namespace Catrillion.Rendering.Components;

public struct SpriteRenderer : IComponent
{
    public int Width;
    public int Height;
    public Color Color;
    public string? TexturePath;
    public int SourceX;
    public int SourceY;
    public int SourceWidth;
    public int SourceHeight;
    public bool IsVisible;
}

