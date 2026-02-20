using Friflo.Engine.ECS;
using Catrillion.Config;
using Catrillion.Rendering.Components;
using Catrillion.Rendering.DualGrid.Components;
using Catrillion.Rendering.DualGrid.Utilities;
using Catrillion.Simulation.Services;
using Core;

namespace Catrillion.Rendering.DualGrid;

/// <summary>
/// Generates chunk entities with terrain and dual-grid data.
/// </summary>
public sealed class ChunkGenerator
{
    private readonly int _chunkSize;
    private readonly int _tileSize;
    private readonly TerrainDataService? _terrainData;

    public ChunkGenerator(int chunkSize, int tileSize, TerrainDataService? terrainData = null)
    {
        _chunkSize = chunkSize;
        _tileSize = tileSize;
        _terrainData = terrainData;
    }

    public Entity GenerateChunk(EntityStore store, int chunkX, int chunkY)
    {
        // Generate terrain with +2 border for dual-grid edge calculation
        int generatedSize = _chunkSize + 2;
        var terrain = new TerrainType[generatedSize * generatedSize];

        // Fill terrain from procedural generation or default to grass
        int startTileX = chunkX * _chunkSize - 1;  // -1 for border
        int startTileY = chunkY * _chunkSize - 1;

        for (int y = 0; y < generatedSize; y++)
        {
            for (int x = 0; x < generatedSize; x++)
            {
                int globalTileX = startTileX + x;
                int globalTileY = startTileY + y;
                int index = y * generatedSize + x;

                if (_terrainData != null && _terrainData.IsGenerated)
                {
                    terrain[index] = _terrainData.GetTerrainAt(globalTileX, globalTileY);
                }
                else
                {
                    terrain[index] = TerrainType.Grass;
                }
            }
        }

        // Calculate dual-grid tiles from the larger terrain
        DualGridCalculator.CalculateDualGrid(
            terrain,
            generatedSize,
            generatedSize,
            out var grassTiles,
            out var grassRot,
            out var dirtTiles,
            out var dirtRot,
            out var waterTiles,
            out var waterRot,
            out var mountainTiles,
            out var mountainRot);

        // Trim dual-grid data to final chunk size (remove border)
        int dualGridWidth = generatedSize + 1;
        int finalWidth = _chunkSize + 1;
        int finalHeight = _chunkSize + 1;
        int finalTotal = finalWidth * finalHeight;

        var finalGrassTiles = new int[finalTotal];
        var finalGrassRot = new float[finalTotal];
        var finalDirtTiles = new int[finalTotal];
        var finalDirtRot = new float[finalTotal];
        var finalWaterTiles = new int[finalTotal];
        var finalWaterRot = new float[finalTotal];
        var finalMountainTiles = new int[finalTotal];
        var finalMountainRot = new float[finalTotal];

        for (int y = 0; y < finalHeight; y++)
        {
            for (int x = 0; x < finalWidth; x++)
            {
                int sourceIndex = (x + 1) + (y + 1) * dualGridWidth;
                int destIndex = x + y * finalWidth;

                finalGrassTiles[destIndex] = grassTiles[sourceIndex];
                finalGrassRot[destIndex] = grassRot[sourceIndex];
                finalDirtTiles[destIndex] = dirtTiles[sourceIndex];
                finalDirtRot[destIndex] = dirtRot[sourceIndex];
                finalWaterTiles[destIndex] = waterTiles[sourceIndex];
                finalWaterRot[destIndex] = waterRot[sourceIndex];
                finalMountainTiles[destIndex] = mountainTiles[sourceIndex];
                finalMountainRot[destIndex] = mountainRot[sourceIndex];
            }
        }

        // Trim terrain data to final chunk size
        var finalTerrain = new TerrainType[_chunkSize * _chunkSize];
        var indices = new int[_chunkSize * _chunkSize];

        for (int y = 0; y < _chunkSize; y++)
        {
            for (int x = 0; x < _chunkSize; x++)
            {
                int sourceIndex = (x + 1) + (y + 1) * generatedSize;
                int destIndex = x + y * _chunkSize;
                finalTerrain[destIndex] = terrain[sourceIndex];
                indices[destIndex] = terrain[sourceIndex] == TerrainType.None ? -1 : 0;
            }
        }

        // Create chunk entity with all components
        var entity = store.CreateEntity();

        entity.AddComponent(new MapChunk { ChunkX = chunkX, ChunkY = chunkY });

        entity.AddComponent(new Tilemap
        {
            Width = _chunkSize,
            Height = _chunkSize,
            TileSize = _tileSize,
            Tiles = indices,
        });

        entity.AddComponent(new TerrainGrid
        {
            Width = _chunkSize,
            Height = _chunkSize,
            Cells = finalTerrain,
        });

        entity.AddComponent(new DualGridData
        {
            Width = _chunkSize,
            Height = _chunkSize,
            GrassCornerTileIndices = finalGrassTiles,
            GrassCornerRotations = finalGrassRot,
            DirtCornerTileIndices = finalDirtTiles,
            DirtCornerRotations = finalDirtRot,
            WaterCornerTileIndices = finalWaterTiles,
            WaterCornerRotations = finalWaterRot,
            MountainCornerTileIndices = finalMountainTiles,
            MountainCornerRotations = finalMountainRot,
        });

        entity.AddComponent(new DualGridAtlases
        {
            SrcTileWidth = GameConfig.DualGrid.AtlasTileWidth,
            SrcTileHeight = GameConfig.DualGrid.AtlasTileHeight,
            Columns = GameConfig.DualGrid.AtlasColumns,
            Loaded = false,
        });

        var chunkPos = new System.Numerics.Vector2(chunkX * _chunkSize * _tileSize, chunkY * _chunkSize * _tileSize);
        entity.AddComponent(new Transform2D { Position = chunkPos });

        entity.AddComponent(new ChunkRenderCache
        {
            IsInitialized = false,
            IsDirty = true,
        });

        return entity;
    }
}
