using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catrillion.GameData;
using Catrillion.GameData.Schemas;
using GameDocDatabase.Runtime;
using System.Security.Cryptography;

namespace Catrillion.Benchmarks;

/// <summary>
/// Benchmarks for GameDocDb loading performance.
/// Tests binary loading, checksum computation, and table access patterns.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[RankColumn]
public class GameDocDbLoadingBenchmarks
{
    private string _binPath = null!;
    private string _jsonDataPath = null!;
    private byte[] _binaryData = null!;
    private GameDocDb _db = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Find the GameData paths relative to the benchmark execution
        var baseDir = AppContext.BaseDirectory;

        // Try to find the GameData.bin file
        var possiblePaths = new[]
        {
            Path.Combine(baseDir, "GameData.bin"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Catrillion.GameData", "Data", "GameData.bin"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Outputs", "Catrillion", "bin", "Debug", "net9.0", "GameData.bin"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Outputs", "Catrillion", "bin", "Release", "net9.0", "GameData.bin"),
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                _binPath = fullPath;
                break;
            }
        }

        if (string.IsNullOrEmpty(_binPath))
        {
            // Build it if needed
            _jsonDataPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Catrillion.GameData", "Data"));
            _binPath = Path.Combine(_jsonDataPath, "GameData.bin");

            if (!File.Exists(_binPath) && Directory.Exists(_jsonDataPath))
            {
                GameDataBinaryBuilder.Build(_jsonDataPath, _binPath);
            }
        }
        else
        {
            _jsonDataPath = Path.GetDirectoryName(_binPath) ?? "";
        }

        if (!File.Exists(_binPath))
        {
            throw new FileNotFoundException($"GameData.bin not found. Searched paths: {string.Join(", ", possiblePaths)}");
        }

        // Pre-load binary data for some benchmarks
        _binaryData = File.ReadAllBytes(_binPath);

        // Pre-load db for lookup benchmarks
        _db = GameDataBinaryLoader.Load(_binPath);

        Console.WriteLine($"Binary file size: {_binaryData.Length:N0} bytes");
        Console.WriteLine($"Binary path: {_binPath}");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Nothing to dispose - GameDocDb doesn't implement IDisposable
    }

    /// <summary>
    /// Measures the time to load GameDocDb from binary file (includes file I/O).
    /// </summary>
    [Benchmark(Description = "Load from binary file")]
    public GameDocDb LoadFromBinary()
    {
        return GameDataBinaryLoader.Load(_binPath);
    }

    /// <summary>
    /// Measures the time to load with checksum computation (includes SHA256 hash).
    /// </summary>
    [Benchmark(Description = "Load with checksum")]
    public (GameDocDb, byte[]) LoadWithChecksum()
    {
        return GameDataBinaryLoader.LoadWithChecksum(_binPath);
    }

    /// <summary>
    /// Measures just the checksum computation on pre-loaded data.
    /// </summary>
    [Benchmark(Description = "Compute SHA256 checksum only")]
    public byte[] ComputeChecksumOnly()
    {
        return SHA256.HashData(_binaryData);
    }

    /// <summary>
    /// Measures file read time only (no parsing).
    /// </summary>
    [Benchmark(Description = "File.ReadAllBytes only")]
    public byte[] FileReadOnly()
    {
        return File.ReadAllBytes(_binPath);
    }

    /// <summary>
    /// Measures memory-mapped file loading via BinaryLoader.
    /// </summary>
    [Benchmark(Description = "Memory-mapped BinaryLoader")]
    public BinaryLoader MemoryMappedLoad()
    {
        var loader = BinaryLoader.Load(_binPath);
        loader.Dispose();
        return loader;
    }

    /// <summary>
    /// Measures single FindById lookup on ZombieTypeData table.
    /// </summary>
    [Benchmark(Description = "FindById<ZombieTypeData>(1)")]
    public ZombieTypeData FindByIdSingle()
    {
        return _db.ZombieTypeData.FindById(1);
    }

    /// <summary>
    /// Measures sequential lookup of all ZombieTypeData IDs.
    /// </summary>
    [Benchmark(Description = "FindById all ZombieTypeData")]
    public int FindByIdAll()
    {
        int count = 0;
        var table = _db.ZombieTypeData;
        foreach (var item in table.All)
        {
            var found = table.FindById(item.Id);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Measures iteration over all ZombieTypeData.
    /// </summary>
    [Benchmark(Description = "Iterate all ZombieTypeData")]
    public int IterateAll()
    {
        int count = 0;
        foreach (var item in _db.ZombieTypeData.All)
        {
            count++;
        }
        return count;
    }
}
