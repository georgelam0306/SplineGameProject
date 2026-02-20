using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Rendering.Components;
using Catrillion.Rendering.DualGrid.Components;
using Core;

namespace Catrillion.Rendering.DualGrid.Systems;

public sealed class RenderDualGridSystem : QuerySystem<DualGridData, Tilemap, Transform2D, ChunkRenderCache>
{
    public static int LastDrawCallCount;

    protected override void OnUpdate()
    {
        LastDrawCallCount = 0;

        foreach (var entity in Query.Entities)
        {
            ref var cache = ref entity.GetComponent<ChunkRenderCache>();
            ref var tilemap = ref entity.GetComponent<Tilemap>();
            ref var transform = ref entity.GetComponent<Transform2D>();

            if (!cache.IsInitialized || tilemap.TileSize <= 0)
            {
                continue;
            }

            int chunkPixelWidth = tilemap.Width * tilemap.TileSize;
            int chunkPixelHeight = tilemap.Height * tilemap.TileSize;

            var sourceRect = new Rectangle(0, 0, chunkPixelWidth, -chunkPixelHeight);
            var destRect = new Rectangle(transform.Position.X, transform.Position.Y, chunkPixelWidth, chunkPixelHeight);

            Raylib.DrawTexturePro(cache.CachedTexture.Texture, sourceRect, destRect, Vector2.Zero, 0f, Color.White);
            LastDrawCallCount++;
        }
    }
}

