using Core;
using SimTable;

namespace Catrillion.Benchmarks;

/// <summary>
/// Benchmark-specific SimTable row with 100k capacity for performance testing.
/// Uses chunked spatial mode with ChunkSize = 4096.
/// </summary>
[SimTable(Capacity = 100_000, CellSize = 16, GridSize = 256, ChunkSize = 8192)]
public partial struct BenchmarkRow
{
    public Fixed64Vec2 Position;
    public int Value;
}
