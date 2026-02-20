using System.Collections.Generic;
using Friflo.Engine.ECS;

namespace DieDrifterDie.Presentation.Rendering.Services;

/// <summary>
/// Registry for active chunk entities indexed by chunk coordinates.
/// </summary>
public sealed class ChunkRegistry
{
    private readonly Dictionary<(int, int), Entity> _chunks;
    private readonly int _chunkSize;
    private readonly int _tileSize;
    private readonly int _chunkSizePx;

    public int TileSize => _tileSize;
    public int ChunkSizePx => _chunkSizePx;

    public ChunkRegistry(int chunkSize, int tileSize)
    {
        _chunks = new Dictionary<(int, int), Entity>();
        _chunkSize = chunkSize;
        _tileSize = tileSize;
        _chunkSizePx = chunkSize * tileSize;
    }

    public void RegisterChunk(int chunkX, int chunkY, Entity chunk)
    {
        _chunks[(chunkX, chunkY)] = chunk;
    }

    public void UnregisterChunk(int chunkX, int chunkY)
    {
        _chunks.Remove((chunkX, chunkY));
    }

    public Entity? GetChunk(int chunkX, int chunkY)
    {
        if (_chunks.TryGetValue((chunkX, chunkY), out var entity) && entity.Id != 0)
        {
            return entity;
        }
        return null;
    }

    public void Clear()
    {
        _chunks.Clear();
    }
}
