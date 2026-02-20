using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BaseTemplate.Simulation.Components;

namespace BaseTemplate.Benchmarks;

/// <summary>
/// Benchmarks for SimWorld snapshot serialization/deserialization performance.
/// Tests the rollback netcode's core operation: saving and restoring game state.
///
/// Note: Entity population is done via reflection to avoid direct dependency on
/// generated table accessors which aren't visible from the benchmarks assembly.
///
/// Key metrics:
/// - Snapshot size in bytes
/// - Save (serialize) time
/// - Load (deserialize) time
/// - Roundtrip time
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SimWorldSnapshotBenchmarks
{
    private SimWorld _world = null!;
    private byte[] _snapshotBuffer = null!;

    [Params("Empty", "Light", "Medium", "Heavy")]
    public string LoadProfile { get; set; } = "Empty";

    [GlobalSetup]
    public unsafe void Setup()
    {
        _world = new SimWorld();

        // Use reflection to populate tables since direct access to generated members
        // isn't available from this assembly
        PopulateWorld(_world, LoadProfile);

        // Run spatial sort via BeginFrame
        _world.BeginFrame();

        // Allocate snapshot buffer
        int snapshotSize = _world.TotalSnapshotSize;
        _snapshotBuffer = GC.AllocateArray<byte>(snapshotSize, pinned: true);

        // Pre-populate buffer with a save
        fixed (byte* ptr = _snapshotBuffer)
        {
            _world.SaveTo(ptr);
        }
    }

    private static void PopulateWorld(SimWorld world, string profile)
    {
        // Get table types and allocate methods via reflection
        var worldType = world.GetType();

        switch (profile)
        {
            case "Empty":
                // No entities, just singletons
                break;

            case "Light":
                // Light load: 1000 zombies, 50 units
                AllocateEntities(world, worldType, "ZombieRows", 1000);
                AllocateEntities(world, worldType, "CombatUnitRows", 50);
                AllocateEntities(world, worldType, "ProjectileRows", 20);
                break;

            case "Medium":
                // Medium load: 5000 zombies, 200 units
                AllocateEntities(world, worldType, "ZombieRows", 5000);
                AllocateEntities(world, worldType, "CombatUnitRows", 200);
                AllocateEntities(world, worldType, "ProjectileRows", 100);
                AllocateEntities(world, worldType, "BuildingRows", 50);
                break;

            case "Heavy":
                // Heavy load: 15000 zombies, 500 units (realistic RTS scenario)
                AllocateEntities(world, worldType, "ZombieRows", 15000);
                AllocateEntities(world, worldType, "CombatUnitRows", 500);
                AllocateEntities(world, worldType, "ProjectileRows", 500);
                AllocateEntities(world, worldType, "BuildingRows", 100);
                AllocateEntities(world, worldType, "ResourceNodeRows", 50);
                AllocateEntities(world, worldType, "CommandQueueRows", 200);
                break;
        }
    }

    private static void AllocateEntities(SimWorld world, Type worldType, string tableName, int count)
    {
        var tableField = worldType.GetField(tableName);
        if (tableField == null) return;

        var table = tableField.GetValue(world);
        if (table == null) return;

        var allocateMethod = table.GetType().GetMethod("Allocate", Type.EmptyTypes);
        if (allocateMethod == null) return;

        for (int i = 0; i < count; i++)
        {
            allocateMethod.Invoke(table, null);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world?.Dispose();
        _world = null!;
        _snapshotBuffer = null!;
    }

    // ============ Size Metrics ============

    [Benchmark]
    public int GetSnapshotSize()
    {
        return _world.TotalSnapshotSize;
    }

    [Benchmark]
    public int GetSlabSize()
    {
        return _world.TotalSlabSize;
    }

    [Benchmark]
    public int GetMetaSize()
    {
        return _world.TotalMetaSize;
    }

    // ============ Save (Serialize) ============

    [Benchmark]
    public unsafe void Save()
    {
        fixed (byte* ptr = _snapshotBuffer)
        {
            _world.SaveTo(ptr);
        }
    }

    // ============ Load (Deserialize) ============

    [Benchmark]
    public unsafe void Load()
    {
        fixed (byte* ptr = _snapshotBuffer)
        {
            _world.LoadFrom(ptr);
        }
    }

    // ============ Roundtrip ============

    [Benchmark(Baseline = true)]
    public unsafe void Roundtrip()
    {
        fixed (byte* ptr = _snapshotBuffer)
        {
            _world.SaveTo(ptr);
            _world.LoadFrom(ptr);
        }
    }

    // ============ Entry Point ============

    public static void Run()
    {
        BenchmarkRunner.Run<SimWorldSnapshotBenchmarks>();
    }
}
