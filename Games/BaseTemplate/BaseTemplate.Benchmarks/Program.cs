using BenchmarkDotNet.Running;
using BaseTemplate.Benchmarks;

// Select which benchmark to run based on args
if (args.Contains("--spatial"))
{
    BenchmarkRunner.Run<SpatialQueryBenchmarks>();
}
else if (args.Contains("--grid"))
{
    BenchmarkRunner.Run<GridSerializationBenchmarks>();
}
else if (args.Contains("--query"))
{
    BenchmarkRunner.Run<MultiTableQueryBenchmarks>();
}
else if (args.Contains("--gamedata"))
{
    BenchmarkRunner.Run<GameDocDbLoadingBenchmarks>();
}
else if (args.Contains("--snapshot"))
{
    BenchmarkRunner.Run<SimWorldSnapshotBenchmarks>();
}
else
{
    // Default: show available benchmarks
    Console.WriteLine("Available benchmarks:");
    Console.WriteLine("  --spatial   : Spatial query performance (QueryBox vs linear iteration)");
    Console.WriteLine("  --grid      : Grid serialization (full copy vs sparse)");
    Console.WriteLine("  --query     : Multi-table query performance");
    Console.WriteLine("  --gamedata  : GameDocDb loading performance");
    Console.WriteLine("  --snapshot  : SimWorld snapshot serialization/deserialization");
    Console.WriteLine();
    Console.WriteLine("Running SimWorldSnapshotBenchmarks (rollback netcode focus)...");
    BenchmarkRunner.Run<SimWorldSnapshotBenchmarks>();
}
