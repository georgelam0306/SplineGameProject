using BenchmarkDotNet.Attributes;
using Core;

namespace DieDrifterDie.Benchmarks;

/// <summary>
/// Benchmarks comparing multi-table query iteration vs manual separate loops.
/// Target: ≤1-2% overhead for per-entity iteration.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MultiTableQueryBenchmarks
{
    private SimWorld _world = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new SimWorld();

        // Populate units (5000)
        for (int i = 0; i < 5000; i++)
        {
            var handle = _world.UnitBenchmarkRows.Allocate();
            int slot = _world.UnitBenchmarkRows.GetSlot(handle);
            _world.UnitBenchmarkRows.Position(slot) = new Fixed64Vec2(
                Fixed64.FromInt(i % 100),
                Fixed64.FromInt(i / 100));
            _world.UnitBenchmarkRows.Health(slot) = 100;
            _world.UnitBenchmarkRows.MaxHealth(slot) = 100;
        }

        // Populate enemies (20000)
        for (int i = 0; i < 20000; i++)
        {
            var handle = _world.EnemyBenchmarkRows.Allocate();
            int slot = _world.EnemyBenchmarkRows.GetSlot(handle);
            _world.EnemyBenchmarkRows.Position(slot) = new Fixed64Vec2(
                Fixed64.FromInt(i % 200),
                Fixed64.FromInt(i / 200));
            _world.EnemyBenchmarkRows.Health(slot) = 50;
            _world.EnemyBenchmarkRows.MaxHealth(slot) = 50;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world.Dispose();
    }

    /// <summary>
    /// Baseline: Two separate for loops, one per table.
    /// Uses Count instead of Capacity since slots are contiguous.
    /// Accesses table through _world on each iteration.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int ManualSeparateLoops()
    {
        int total = 0;

        // Loop 1: UnitBenchmarkRows - slots are contiguous [0, Count-1]
        int unitCount = _world.UnitBenchmarkRows.Count;
        for (int slot = 0; slot < unitCount; slot++)
        {
            _world.UnitBenchmarkRows.Health(slot) -= 1;
            total++;
        }

        // Loop 2: EnemyBenchmarkRows - slots are contiguous [0, Count-1]
        int enemyCount = _world.EnemyBenchmarkRows.Count;
        for (int slot = 0; slot < enemyCount; slot++)
        {
            _world.EnemyBenchmarkRows.Health(slot) -= 1;
            total++;
        }

        return total;
    }

    /// <summary>
    /// Optimized manual: Cache table reference and use span directly.
    /// This should be the fastest possible approach.
    /// </summary>
    [Benchmark]
    public int ManualOptimized_Spans()
    {
        int total = 0;

        // Use spans directly - same pattern as chunked
        var unitHealths = _world.UnitBenchmarkRows.HealthSpan;
        int unitCount = _world.UnitBenchmarkRows.Count;
        for (int i = 0; i < unitCount; i++)
        {
            unitHealths[i] -= 1;
            total++;
        }

        var enemyHealths = _world.EnemyBenchmarkRows.HealthSpan;
        int enemyCount = _world.EnemyBenchmarkRows.Count;
        for (int i = 0; i < enemyCount; i++)
        {
            enemyHealths[i] -= 1;
            total++;
        }

        return total;
    }

    /// <summary>
    /// Multi-table query: Per-entity iteration.
    /// </summary>
    [Benchmark]
    public int MultiTableQuery_PerEntity()
    {
        int total = 0;

        foreach (var entity in _world.Query<IDamageableBenchmark>())
        {
            entity.Health -= 1;
            total++;
        }

        return total;
    }

    /// <summary>
    /// Multi-table query: Chunked iteration (SIMD-friendly).
    /// Uses Count since slots are contiguous [0, Count-1] - no mask check needed.
    /// </summary>
    [Benchmark]
    public int MultiTableQuery_Chunked()
    {
        int total = 0;

        foreach (var chunk in _world.Query<IDamageableBenchmark>().ByTable())
        {
            var healths = chunk.Healths;
            int count = chunk.Count;  // Slots are contiguous, no mask needed

            for (int i = 0; i < count; i++)
            {
                healths[i] -= 1;
                total++;
            }
        }

        return total;
    }
}
