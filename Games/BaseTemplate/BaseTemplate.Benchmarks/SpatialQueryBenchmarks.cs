using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Core;

namespace BaseTemplate.Benchmarks;

/// <summary>
/// Benchmarks comparing different spatial query methods on a 100k entity table.
/// Entities are distributed across a large world to test chunked spatial hash performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SpatialQueryBenchmarks
{
    private BenchmarkRowTable _table = null!;

    // Query bounds
    private Fixed64 _smallBoxMinX, _smallBoxMaxX, _smallBoxMinY, _smallBoxMaxY;
    private Fixed64 _mediumBoxMinX, _mediumBoxMaxX, _mediumBoxMinY, _mediumBoxMaxY;
    private Fixed64 _largeBoxMinX, _largeBoxMaxX, _largeBoxMinY, _largeBoxMaxY;

    [Params(100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _table = new BenchmarkRowTable();

        // Distribute entities across a large world (50k x 50k units)
        // This will create many chunks with ChunkSize = 4096
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < EntityCount; i++)
        {
            var handle = _table.Allocate();
            int slot = _table.GetSlot(handle);

            // Random position across 50k x 50k world
            int x = random.Next(0, 50_000);
            int y = random.Next(0, 50_000);

            _table.Position(slot) = new Fixed64Vec2(Fixed64.FromInt(x), Fixed64.FromInt(y));
            _table.Value(slot) = i;  // Some value
        }

        // Run spatial sort to populate chunk structures
        _table.SpatialSort();

        // Define query boxes of different sizes
        // Small: ~500x500 area (fraction of one chunk)
        _smallBoxMinX = Fixed64.FromInt(10_000);
        _smallBoxMaxX = Fixed64.FromInt(10_500);
        _smallBoxMinY = Fixed64.FromInt(10_000);
        _smallBoxMaxY = Fixed64.FromInt(10_500);

        // Medium: ~5000x5000 area (several chunks)
        _mediumBoxMinX = Fixed64.FromInt(10_000);
        _mediumBoxMaxX = Fixed64.FromInt(15_000);
        _mediumBoxMinY = Fixed64.FromInt(10_000);
        _mediumBoxMaxY = Fixed64.FromInt(15_000);

        // Large: ~20000x20000 area (many chunks)
        _largeBoxMinX = Fixed64.FromInt(5_000);
        _largeBoxMaxX = Fixed64.FromInt(25_000);
        _largeBoxMinY = Fixed64.FromInt(5_000);
        _largeBoxMaxY = Fixed64.FromInt(25_000);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _table?.Dispose();
        _table = null!;
    }

    // ============ Linear Iteration Baselines ============

    [Benchmark(Baseline = true)]
    public int LinearIteration_SmallBox()
    {
        int count = 0;
        for (int slot = 0; slot < _table.Count; slot++)
        {
            if (!_table.TryGetRow(slot, out var row)) continue;

            if (row.Position.X >= _smallBoxMinX && row.Position.X <= _smallBoxMaxX &&
                row.Position.Y >= _smallBoxMinY && row.Position.Y <= _smallBoxMaxY)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark]
    public int LinearIteration_MediumBox()
    {
        int count = 0;
        for (int slot = 0; slot < _table.Count; slot++)
        {
            if (!_table.TryGetRow(slot, out var row)) continue;

            if (row.Position.X >= _mediumBoxMinX && row.Position.X <= _mediumBoxMaxX &&
                row.Position.Y >= _mediumBoxMinY && row.Position.Y <= _mediumBoxMaxY)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark]
    public int LinearIteration_LargeBox()
    {
        int count = 0;
        for (int slot = 0; slot < _table.Count; slot++)
        {
            if (!_table.TryGetRow(slot, out var row)) continue;

            if (row.Position.X >= _largeBoxMinX && row.Position.X <= _largeBoxMaxX &&
                row.Position.Y >= _largeBoxMinY && row.Position.Y <= _largeBoxMaxY)
            {
                count++;
            }
        }
        return count;
    }

    // ============ QueryBox (Chunked Spatial Hash) ============

    [Benchmark]
    public int QueryBox_SmallBox()
    {
        int count = 0;
        foreach (int slot in _table.QueryBox(_smallBoxMinX, _smallBoxMaxX, _smallBoxMinY, _smallBoxMaxY))
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int QueryBox_MediumBox()
    {
        int count = 0;
        foreach (int slot in _table.QueryBox(_mediumBoxMinX, _mediumBoxMaxX, _mediumBoxMinY, _mediumBoxMaxY))
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int QueryBox_LargeBox()
    {
        int count = 0;
        foreach (int slot in _table.QueryBox(_largeBoxMinX, _largeBoxMaxX, _largeBoxMinY, _largeBoxMaxY))
        {
            count++;
        }
        return count;
    }

    // ============ Manual Chunk/Cell Iteration (bypassing QueryBox abstraction) ============

    [Benchmark]
    public int ManualChunk_SmallBox()
    {
        return ManualChunkQuery(_smallBoxMinX.Raw, _smallBoxMaxX.Raw, _smallBoxMinY.Raw, _smallBoxMaxY.Raw);
    }

    [Benchmark]
    public int ManualChunk_MediumBox()
    {
        return ManualChunkQuery(_mediumBoxMinX.Raw, _mediumBoxMaxX.Raw, _mediumBoxMinY.Raw, _mediumBoxMaxY.Raw);
    }

    [Benchmark]
    public int ManualChunk_LargeBox()
    {
        return ManualChunkQuery(_largeBoxMinX.Raw, _largeBoxMaxX.Raw, _largeBoxMinY.Raw, _largeBoxMaxY.Raw);
    }

    private int ManualChunkQuery(long minXRaw, long maxXRaw, long minYRaw, long maxYRaw)
    {
        int count = 0;
        const int chunkSize = BenchmarkRowTable.ChunkSize;

        // Calculate chunk range
        int minWorldX = (int)(minXRaw >> 16);
        int maxWorldX = (int)(maxXRaw >> 16);
        int minWorldY = (int)(minYRaw >> 16);
        int maxWorldY = (int)(maxYRaw >> 16);
        int minChunkX = minWorldX >= 0 ? minWorldX / chunkSize : (minWorldX - chunkSize + 1) / chunkSize;
        int maxChunkX = maxWorldX >= 0 ? maxWorldX / chunkSize : (maxWorldX - chunkSize + 1) / chunkSize;
        int minChunkY = minWorldY >= 0 ? minWorldY / chunkSize : (minWorldY - chunkSize + 1) / chunkSize;
        int maxChunkY = maxWorldY >= 0 ? maxWorldY / chunkSize : (maxWorldY - chunkSize + 1) / chunkSize;

        // Iterate all active chunks
        int activeChunkCount = _table.ActiveChunkCount;
        for (int chunkIdx = 0; chunkIdx < activeChunkCount; chunkIdx++)
        {
            var key = _table.GetActiveChunk(chunkIdx);

            // Skip chunks outside box range
            if (key.X < minChunkX || key.X > maxChunkX ||
                key.Y < minChunkY || key.Y > maxChunkY)
                continue;

            int poolIdx = _table.GetChunkPoolIndex(key);
            if (poolIdx < 0) continue;

            // Use EntityCount directly instead of summing all cells
            int entityIdx = _table.GetChunkCellStart(poolIdx, 0);
            int entityEnd = entityIdx + _table.GetChunkEntityCount(poolIdx);

            // Check each entity
            for (int i = entityIdx; i < entityEnd; i++)
            {
                int slot = _table.GetSortedSlot(i);
                var pos = _table.Position(slot);
                if (pos.X.Raw >= minXRaw && pos.X.Raw <= maxXRaw &&
                    pos.Y.Raw >= minYRaw && pos.Y.Raw <= maxYRaw)
                {
                    count++;
                }
            }
        }

        return count;
    }

    // ============ SpatialSort Benchmark ============

    [Benchmark]
    public void SpatialSort()
    {
        _table.SpatialSort();
    }

    // ============ Entry Point ============

    public static void Run()
    {
        BenchmarkRunner.Run<SpatialQueryBenchmarks>();
    }
}
