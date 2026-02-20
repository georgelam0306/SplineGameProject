using Friflo.Engine.ECS;

namespace Catrillion.Rendering.DualGrid.Components;

/// <summary>
/// Marker component identifying a chunk entity and its grid coordinates.
/// </summary>
public struct MapChunk : IComponent
{
    public int ChunkX;
    public int ChunkY;
}
