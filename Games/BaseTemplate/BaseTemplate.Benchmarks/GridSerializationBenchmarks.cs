using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Core;

namespace BaseTemplate.Benchmarks;

/// <summary>
/// Benchmarks comparing grid serialization approaches for rollback netcode.
/// Tests full copy vs sparse serialization at different grid sizes and fill percentages.
///
/// Key question: Is full memcpy of mostly-empty grids acceptable, or do we need sparse storage?
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class GridSerializationBenchmarks
{
    // Grid data - using Fixed64 (8 bytes) like real ThreatGrid
    private Fixed64[] _grid32 = null!;     // 32x32 = 1,024 cells = 8 KB
    private Fixed64[] _grid64 = null!;     // 64x64 = 4,096 cells = 32 KB
    private Fixed64[] _grid128 = null!;    // 128x128 = 16,384 cells = 128 KB
    private Fixed64[] _grid256 = null!;    // 256x256 = 65,536 cells = 512 KB

    // Snapshot buffers (destination for serialization)
    private byte[] _snapshot32 = null!;
    private byte[] _snapshot64 = null!;
    private byte[] _snapshot128 = null!;
    private byte[] _snapshot256 = null!;

    // Sparse buffers - list of (index, value) pairs
    private List<(int index, Fixed64 value)> _sparse32 = null!;
    private List<(int index, Fixed64 value)> _sparse64 = null!;
    private List<(int index, Fixed64 value)> _sparse128 = null!;
    private List<(int index, Fixed64 value)> _sparse256 = null!;

    [Params(0, 10, 50, 100)]
    public int FillPercentage { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Allocate grids
        _grid32 = new Fixed64[32 * 32];
        _grid64 = new Fixed64[64 * 64];
        _grid128 = new Fixed64[128 * 128];
        _grid256 = new Fixed64[256 * 256];

        // Allocate snapshot buffers
        _snapshot32 = new byte[_grid32.Length * sizeof(long)];
        _snapshot64 = new byte[_grid64.Length * sizeof(long)];
        _snapshot128 = new byte[_grid128.Length * sizeof(long)];
        _snapshot256 = new byte[_grid256.Length * sizeof(long)];

        // Allocate sparse lists (capacity based on fill %)
        int sparse32Capacity = Math.Max(1, _grid32.Length * FillPercentage / 100);
        int sparse64Capacity = Math.Max(1, _grid64.Length * FillPercentage / 100);
        int sparse128Capacity = Math.Max(1, _grid128.Length * FillPercentage / 100);
        int sparse256Capacity = Math.Max(1, _grid256.Length * FillPercentage / 100);

        _sparse32 = new List<(int, Fixed64)>(sparse32Capacity);
        _sparse64 = new List<(int, Fixed64)>(sparse64Capacity);
        _sparse128 = new List<(int, Fixed64)>(sparse128Capacity);
        _sparse256 = new List<(int, Fixed64)>(sparse256Capacity);

        // Fill grids based on FillPercentage
        var random = new Random(42);
        FillGrid(_grid32, random, FillPercentage);
        FillGrid(_grid64, random, FillPercentage);
        FillGrid(_grid128, random, FillPercentage);
        FillGrid(_grid256, random, FillPercentage);
    }

    private void FillGrid(Fixed64[] grid, Random random, int fillPercent)
    {
        // Clear grid first
        Array.Clear(grid);

        if (fillPercent == 0) return;

        int cellsToFill = grid.Length * fillPercent / 100;

        // Randomly distribute non-zero values
        var indices = Enumerable.Range(0, grid.Length).OrderBy(_ => random.Next()).Take(cellsToFill).ToList();

        foreach (int idx in indices)
        {
            // Random threat value between 1 and 200
            grid[idx] = Fixed64.FromInt(random.Next(1, 201));
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _grid32 = null!;
        _grid64 = null!;
        _grid128 = null!;
        _grid256 = null!;
        _snapshot32 = null!;
        _snapshot64 = null!;
        _snapshot128 = null!;
        _snapshot256 = null!;
        _sparse32 = null!;
        _sparse64 = null!;
        _sparse128 = null!;
        _sparse256 = null!;
    }

    // ============ Full Copy Serialization (Current Approach) ============

    [Benchmark(Baseline = true)]
    public void FullCopy_32x32()
    {
        // Simulate blittable memcpy - this is what the current SimTable generator does
        var srcSpan = MemoryMarshal.AsBytes(_grid32.AsSpan());
        srcSpan.CopyTo(_snapshot32);
    }

    [Benchmark]
    public void FullCopy_64x64()
    {
        var srcSpan = MemoryMarshal.AsBytes(_grid64.AsSpan());
        srcSpan.CopyTo(_snapshot64);
    }

    [Benchmark]
    public void FullCopy_128x128()
    {
        var srcSpan = MemoryMarshal.AsBytes(_grid128.AsSpan());
        srcSpan.CopyTo(_snapshot128);
    }

    [Benchmark]
    public void FullCopy_256x256()
    {
        var srcSpan = MemoryMarshal.AsBytes(_grid256.AsSpan());
        srcSpan.CopyTo(_snapshot256);
    }

    // ============ Full Copy Restore ============

    [Benchmark]
    public void FullRestore_32x32()
    {
        var dstSpan = MemoryMarshal.AsBytes(_grid32.AsSpan());
        _snapshot32.AsSpan().CopyTo(dstSpan);
    }

    [Benchmark]
    public void FullRestore_64x64()
    {
        var dstSpan = MemoryMarshal.AsBytes(_grid64.AsSpan());
        _snapshot64.AsSpan().CopyTo(dstSpan);
    }

    [Benchmark]
    public void FullRestore_128x128()
    {
        var dstSpan = MemoryMarshal.AsBytes(_grid128.AsSpan());
        _snapshot128.AsSpan().CopyTo(dstSpan);
    }

    [Benchmark]
    public void FullRestore_256x256()
    {
        var dstSpan = MemoryMarshal.AsBytes(_grid256.AsSpan());
        _snapshot256.AsSpan().CopyTo(dstSpan);
    }

    // ============ Sparse Serialization (Only Non-Zero Cells) ============

    [Benchmark]
    public int SparseSave_32x32()
    {
        _sparse32.Clear();
        for (int i = 0; i < _grid32.Length; i++)
        {
            if (_grid32[i] != Fixed64.Zero)
            {
                _sparse32.Add((i, _grid32[i]));
            }
        }
        return _sparse32.Count;
    }

    [Benchmark]
    public int SparseSave_64x64()
    {
        _sparse64.Clear();
        for (int i = 0; i < _grid64.Length; i++)
        {
            if (_grid64[i] != Fixed64.Zero)
            {
                _sparse64.Add((i, _grid64[i]));
            }
        }
        return _sparse64.Count;
    }

    [Benchmark]
    public int SparseSave_128x128()
    {
        _sparse128.Clear();
        for (int i = 0; i < _grid128.Length; i++)
        {
            if (_grid128[i] != Fixed64.Zero)
            {
                _sparse128.Add((i, _grid128[i]));
            }
        }
        return _sparse128.Count;
    }

    [Benchmark]
    public int SparseSave_256x256()
    {
        _sparse256.Clear();
        for (int i = 0; i < _grid256.Length; i++)
        {
            if (_grid256[i] != Fixed64.Zero)
            {
                _sparse256.Add((i, _grid256[i]));
            }
        }
        return _sparse256.Count;
    }

    // ============ Sparse Restore ============

    [Benchmark]
    public void SparseRestore_32x32()
    {
        // Must clear first, then apply sparse values
        Array.Clear(_grid32);
        foreach (var (index, value) in _sparse32)
        {
            _grid32[index] = value;
        }
    }

    [Benchmark]
    public void SparseRestore_64x64()
    {
        Array.Clear(_grid64);
        foreach (var (index, value) in _sparse64)
        {
            _grid64[index] = value;
        }
    }

    [Benchmark]
    public void SparseRestore_128x128()
    {
        Array.Clear(_grid128);
        foreach (var (index, value) in _sparse128)
        {
            _grid128[index] = value;
        }
    }

    [Benchmark]
    public void SparseRestore_256x256()
    {
        Array.Clear(_grid256);
        foreach (var (index, value) in _sparse256)
        {
            _grid256[index] = value;
        }
    }

    // ============ Snapshot + Restore Roundtrip ============

    [Benchmark]
    public void Roundtrip_FullCopy_32x32()
    {
        // Save
        var srcSpan = MemoryMarshal.AsBytes(_grid32.AsSpan());
        srcSpan.CopyTo(_snapshot32);

        // Restore
        var dstSpan = MemoryMarshal.AsBytes(_grid32.AsSpan());
        _snapshot32.AsSpan().CopyTo(dstSpan);
    }

    [Benchmark]
    public void Roundtrip_FullCopy_64x64()
    {
        var srcSpan = MemoryMarshal.AsBytes(_grid64.AsSpan());
        srcSpan.CopyTo(_snapshot64);

        var dstSpan = MemoryMarshal.AsBytes(_grid64.AsSpan());
        _snapshot64.AsSpan().CopyTo(dstSpan);
    }

    [Benchmark]
    public void Roundtrip_Sparse_32x32()
    {
        // Save
        _sparse32.Clear();
        for (int i = 0; i < _grid32.Length; i++)
        {
            if (_grid32[i] != Fixed64.Zero)
            {
                _sparse32.Add((i, _grid32[i]));
            }
        }

        // Restore
        Array.Clear(_grid32);
        foreach (var (index, value) in _sparse32)
        {
            _grid32[index] = value;
        }
    }

    [Benchmark]
    public void Roundtrip_Sparse_64x64()
    {
        _sparse64.Clear();
        for (int i = 0; i < _grid64.Length; i++)
        {
            if (_grid64[i] != Fixed64.Zero)
            {
                _sparse64.Add((i, _grid64[i]));
            }
        }

        Array.Clear(_grid64);
        foreach (var (index, value) in _sparse64)
        {
            _grid64[index] = value;
        }
    }

    public static void Run()
    {
        BenchmarkDotNet.Running.BenchmarkRunner.Run<GridSerializationBenchmarks>();
    }
}
