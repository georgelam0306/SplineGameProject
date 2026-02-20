using BenchmarkDotNet.Attributes;
using FixedMath;

namespace FixedMath.Benchmarks;

[MemoryDiagnoser]
public class RandomBenchmarks
{
    private int _frame;
    private int _slot;
    private int _salt;
    private int _seed;
    private Random _systemRandom = null!;
    private Fixed64[] _weights = null!;

    [GlobalSetup]
    public void Setup()
    {
        _frame = 1000;
        _slot = 42;
        _salt = 7;
        _seed = 12345;
        _systemRandom = new Random(_seed);
        _weights = new Fixed64[]
        {
            Fixed64.FromFloat(1.0f),
            Fixed64.FromFloat(2.0f),
            Fixed64.FromFloat(3.0f),
            Fixed64.FromFloat(4.0f)
        };
    }

    [Benchmark]
    public uint DeterministicRandom_Hash() => DeterministicRandom.Hash(_frame, _slot, _salt);

    [Benchmark]
    public int DeterministicRandom_RangeInt() => DeterministicRandom.Range(_frame, _slot, _salt, 0, 100);

    [Benchmark]
    public int SystemRandom_RangeInt() => _systemRandom.Next(0, 100);

    [Benchmark]
    public Fixed64 DeterministicRandom_Value() => DeterministicRandom.Value(_frame, _slot, _salt);

    [Benchmark]
    public double SystemRandom_Value() => _systemRandom.NextDouble();

    [Benchmark]
    public Fixed64 DeterministicRandom_RangeFixed64() => DeterministicRandom.Range(
        _frame, _slot, _salt,
        Fixed64.FromFloat(0f), Fixed64.FromFloat(100f)
    );

    [Benchmark]
    public bool DeterministicRandom_Bool() => DeterministicRandom.Bool(_frame, _slot, _salt);

    [Benchmark]
    public bool DeterministicRandom_Chance() => DeterministicRandom.Chance(_frame, _slot, _salt, Fixed64.FromFloat(0.3f));

    [Benchmark]
    public Fixed64 DeterministicRandom_Angle() => DeterministicRandom.Angle(_frame, _slot, _salt);

    [Benchmark]
    public Fixed64Vec2 DeterministicRandom_UnitVector2D() => DeterministicRandom.UnitVector2D(_frame, _slot, _salt);

    [Benchmark]
    public Fixed64Vec2 DeterministicRandom_InsideUnitCircle() => DeterministicRandom.InsideUnitCircle(_frame, _slot, _salt);

    [Benchmark]
    public Fixed64Vec3 DeterministicRandom_UnitVector3D() => DeterministicRandom.UnitVector3D(_frame, _slot, _salt);

    [Benchmark]
    public Fixed64Vec3 DeterministicRandom_InsideUnitSphere() => DeterministicRandom.InsideUnitSphere(_frame, _slot, _salt);

    [Benchmark]
    public int DeterministicRandom_WeightedChoice() => DeterministicRandom.WeightedChoice(_frame, _slot, _salt, _weights);

    [Benchmark]
    public uint DeterministicRandom_HashWithSeed() => DeterministicRandom.HashWithSeed(_seed, _frame, _slot, _salt);
}
