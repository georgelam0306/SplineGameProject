using Friflo.Engine.ECS;
using Raylib_cs;

namespace Catrillion.Rendering.DualGrid.Components;

public struct ChunkRenderCache : IComponent
{
    public RenderTexture2D CachedTexture;
    public bool IsInitialized;
    public bool IsDirty;
}

