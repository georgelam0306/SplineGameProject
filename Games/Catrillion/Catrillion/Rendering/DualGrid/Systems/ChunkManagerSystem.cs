using System;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Camera;
using Catrillion.Config;
using Catrillion.Rendering.DualGrid.Components;
using Catrillion.Rendering.Services;
using Catrillion.Simulation.Services;

namespace Catrillion.Rendering.DualGrid.Systems;

/// <summary>
/// Manages dynamic loading and unloading of terrain chunks based on camera position.
/// </summary>
public sealed class ChunkManagerSystem : BaseSystem
{
    private readonly EntityStore _store;
    private readonly CameraManager _cameraManager;
    private readonly ChunkRegistry _chunkRegistry;
    private readonly ChunkGenerator _chunkGenerator;
    private readonly TerrainDataService _terrainData;

    private int _lastCenterChunkX;
    private int _lastCenterChunkY;
    private bool _hasInitialized;
    private bool _terrainWasGenerated;

    // Pre-allocated arrays for chunk management (avoid allocations in hot path)
    private readonly Entity[] _oldChunks;
    private readonly (int x, int y)[] _oldCoords;

    private static readonly int Radius = GameConfig.DualGrid.ChunkLoadRadius;
    private static readonly int GridSize = Radius * 2 + 1;

    public ChunkManagerSystem(
        EntityStore store,
        CameraManager cameraManager,
        ChunkRegistry chunkRegistry,
        ChunkGenerator chunkGenerator,
        TerrainDataService terrainData)
    {
        _store = store;
        _cameraManager = cameraManager;
        _chunkRegistry = chunkRegistry;
        _chunkGenerator = chunkGenerator;
        _terrainData = terrainData;

        int gridCells = GridSize * GridSize;
        _oldChunks = new Entity[gridCells];
        _oldCoords = new (int, int)[gridCells];
    }

    protected override void OnUpdateGroup()
    {
        int chunkSizePx = _chunkRegistry.ChunkSizePx;
        if (chunkSizePx <= 0)
        {
            return;
        }

        // Check if terrain just became available - regenerate all chunks
        if (_terrainData.IsGenerated && !_terrainWasGenerated)
        {
            _terrainWasGenerated = true;
            if (_hasInitialized)
            {
                RegenerateAllChunks();
            }
        }

        // Calculate current center chunk from camera position
        int centerChunkX = (int)Math.Floor(_cameraManager.TargetX / chunkSizePx);
        int centerChunkY = (int)Math.Floor(_cameraManager.TargetY / chunkSizePx);

        // First-time initialization: create chunks without trying to process old chunks
        if (!_hasInitialized)
        {
            InitializeChunks(centerChunkX, centerChunkY);
            _lastCenterChunkX = centerChunkX;
            _lastCenterChunkY = centerChunkY;
            _hasInitialized = true;
            return;
        }

        // Only update if center changed
        if (centerChunkX == _lastCenterChunkX && centerChunkY == _lastCenterChunkY)
        {
            return;
        }

        UpdateChunks(centerChunkX, centerChunkY);
        _lastCenterChunkX = centerChunkX;
        _lastCenterChunkY = centerChunkY;
    }

    private void InitializeChunks(int centerX, int centerY)
    {
        // First-time setup: just create chunks around the initial position
        for (int offsetY = -Radius; offsetY <= Radius; offsetY++)
        {
            for (int offsetX = -Radius; offsetX <= Radius; offsetX++)
            {
                int chunkX = centerX + offsetX;
                int chunkY = centerY + offsetY;

                var newChunk = _chunkGenerator.GenerateChunk(_store, chunkX, chunkY);
                _chunkRegistry.RegisterChunk(chunkX, chunkY, newChunk);
            }
        }
    }

    private void UpdateChunks(int centerX, int centerY)
    {
        // Collect existing chunks in old range
        int oldChunkCount = 0;
        for (int offsetY = -Radius; offsetY <= Radius; offsetY++)
        {
            for (int offsetX = -Radius; offsetX <= Radius; offsetX++)
            {
                int chunkX = _lastCenterChunkX + offsetX;
                int chunkY = _lastCenterChunkY + offsetY;

                var chunk = _chunkRegistry.GetChunk(chunkX, chunkY);
                if (chunk.HasValue)
                {
                    _oldChunks[oldChunkCount] = chunk.Value;
                    _oldCoords[oldChunkCount] = (chunkX, chunkY);
                    oldChunkCount++;
                }
            }
        }

        // Clear registry for re-registration
        _chunkRegistry.Clear();

        // Process old chunks: keep or delete
        for (int i = 0; i < oldChunkCount; i++)
        {
            var (chunkX, chunkY) = _oldCoords[i];
            var chunk = _oldChunks[i];

            bool inRange = chunkX >= centerX - Radius && chunkX <= centerX + Radius &&
                           chunkY >= centerY - Radius && chunkY <= centerY + Radius;

            if (inRange)
            {
                _chunkRegistry.RegisterChunk(chunkX, chunkY, chunk);
            }
            else
            {
                DisposeAndDeleteChunk(chunk);
            }
        }

        // Create new chunks in new range
        for (int offsetY = -Radius; offsetY <= Radius; offsetY++)
        {
            for (int offsetX = -Radius; offsetX <= Radius; offsetX++)
            {
                int chunkX = centerX + offsetX;
                int chunkY = centerY + offsetY;

                if (_chunkRegistry.GetChunk(chunkX, chunkY).HasValue)
                {
                    continue;
                }

                var newChunk = _chunkGenerator.GenerateChunk(_store, chunkX, chunkY);
                _chunkRegistry.RegisterChunk(chunkX, chunkY, newChunk);
            }
        }
    }

    private void RegenerateAllChunks()
    {
        // Delete all existing chunks and recreate them with new terrain data
        for (int offsetY = -Radius; offsetY <= Radius; offsetY++)
        {
            for (int offsetX = -Radius; offsetX <= Radius; offsetX++)
            {
                int chunkX = _lastCenterChunkX + offsetX;
                int chunkY = _lastCenterChunkY + offsetY;
                var chunk = _chunkRegistry.GetChunk(chunkX, chunkY);
                if (chunk.HasValue)
                {
                    DisposeAndDeleteChunk(chunk.Value);
                }
            }
        }
        _chunkRegistry.Clear();
        InitializeChunks(_lastCenterChunkX, _lastCenterChunkY);
    }

    private static void DisposeAndDeleteChunk(Entity chunk)
    {
        // Unload render texture (owned by chunk)
        if (chunk.HasComponent<ChunkRenderCache>())
        {
            ref var cache = ref chunk.GetComponent<ChunkRenderCache>();
            if (cache.IsInitialized)
            {
                Raylib.UnloadRenderTexture(cache.CachedTexture);
            }
        }

        // Note: DualGridAtlases textures are shared and NOT unloaded here
        // They are managed by DualGridTextureService

        chunk.DeleteEntity();
    }
}
