using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Config;
using Catrillion.Rendering.Components;
using Catrillion.Rendering.DualGrid.Components;
using Core;

namespace Catrillion.Rendering.DualGrid.Systems;

public sealed class ChunkRenderCacheSystem : QuerySystem<DualGridData, DualGridAtlases, Tilemap, Transform2D, ChunkRenderCache>
{
    protected override void OnUpdate()
    {
        foreach (var entity in Query.Entities)
        {
            ref var cache = ref entity.GetComponent<ChunkRenderCache>();

            if (!cache.IsInitialized)
            {
                int chunkPixelSize = GameConfig.Map.ChunkSize * GameConfig.Map.TileSize;
                cache.CachedTexture = Raylib.LoadRenderTexture(chunkPixelSize, chunkPixelSize);
                Raylib.SetTextureFilter(cache.CachedTexture.Texture, TextureFilter.Point);
                cache.IsInitialized = true;
                cache.IsDirty = true;
            }

            if (!cache.IsDirty)
            {
                continue;
            }

            ref var dualGrid = ref entity.GetComponent<DualGridData>();
            ref var atlases = ref entity.GetComponent<DualGridAtlases>();
            ref var tilemap = ref entity.GetComponent<Tilemap>();

            if (!atlases.Loaded || dualGrid.Width <= 0 || dualGrid.Height <= 0 || tilemap.TileSize <= 0)
            {
                continue;
            }

            RenderChunkToTexture(ref cache, ref dualGrid, ref atlases, tilemap.TileSize);
            cache.IsDirty = false;
        }
    }

    private static void RenderChunkToTexture(
        ref ChunkRenderCache cache,
        ref DualGridData dualGrid,
        ref DualGridAtlases atlases,
        int pixelTileSize)
    {
        int srcTileWidth = atlases.SrcTileWidth;
        int srcTileHeight = atlases.SrcTileHeight;
        int columns = atlases.Columns;
        int dualGridStride = dualGrid.Width + 1;

        Raylib.BeginTextureMode(cache.CachedTexture);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));

        for (int cornerY = 0; cornerY <= dualGrid.Height; cornerY++)
        {
            for (int cornerX = 0; cornerX <= dualGrid.Width; cornerX++)
            {
                int linearIndex = cornerX + cornerY * dualGridStride;

                DrawLayerToTexture(atlases.Water, dualGrid.WaterCornerTileIndices, dualGrid.WaterCornerRotations,
                    linearIndex, columns, srcTileWidth, srcTileHeight, pixelTileSize, cornerX, cornerY);

                DrawLayerToTexture(atlases.Dirt, dualGrid.DirtCornerTileIndices, dualGrid.DirtCornerRotations,
                    linearIndex, columns, srcTileWidth, srcTileHeight, pixelTileSize, cornerX, cornerY);

                DrawLayerToTexture(atlases.Grass, dualGrid.GrassCornerTileIndices, dualGrid.GrassCornerRotations,
                    linearIndex, columns, srcTileWidth, srcTileHeight, pixelTileSize, cornerX, cornerY);

                DrawLayerToTexture(atlases.Mountain, dualGrid.MountainCornerTileIndices, dualGrid.MountainCornerRotations,
                    linearIndex, columns, srcTileWidth, srcTileHeight, pixelTileSize, cornerX, cornerY);
            }
        }

        Raylib.EndTextureMode();
    }

    private static void DrawLayerToTexture(
        Texture2D texture,
        int[]? tiles,
        float[]? rotations,
        int linearIndex,
        int columns,
        int srcTileWidth,
        int srcTileHeight,
        int pixelTileSize,
        int cornerX,
        int cornerY)
    {
        if (tiles is null || rotations is null)
        {
            return;
        }

        int cornerTileIndex = tiles[linearIndex];
        if (cornerTileIndex < 0)
        {
            return;
        }

        float rotation = rotations[linearIndex];

        int srcX = (cornerTileIndex % columns) * srcTileWidth;
        int srcY = (cornerTileIndex / columns) * srcTileHeight;
        var sourceRect = new Rectangle(srcX, srcY, srcTileWidth, srcTileHeight);

        float localX = cornerX * pixelTileSize;// - pixelTileSize * 0.5f;
        float localY = cornerY * pixelTileSize;// - pixelTileSize * 0.5f;
        var destRect = new Rectangle(localX, localY, pixelTileSize, pixelTileSize);
        var origin = new Vector2(pixelTileSize * 0.5f, pixelTileSize * 0.5f);

        Raylib.DrawTexturePro(texture, sourceRect, destRect, origin, rotation, Color.White);
    }
}

