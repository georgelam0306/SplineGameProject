using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FixedMath;

BenchmarkRunner.Run<Fixed64Benchmarks>();
BenchmarkRunner.Run<VectorBenchmarks>();

[MemoryDiagnoser]
[ShortRunJob]
public class Fixed64Benchmarks
{
    private const int N = 1_000;
    private Fixed64[] _values = null!;
    private Fixed64[] _angles = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _values = new Fixed64[N];
        _angles = new Fixed64[N];
        for (int i = 0; i < N; i++)
        {
            _values[i] = Fixed64.FromDouble(0.1 + rng.NextDouble() * 99.9);
            _angles[i] = Fixed64.FromDouble(rng.NextDouble() * Math.PI * 2);
        }
    }

    [Benchmark] public Fixed64 Add_Checked() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N; i++) s = s + _values[i]; return s; }
    [Benchmark] public Fixed64 Add_Fast() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N; i++) s = Fixed64.AddFast(s, _values[i]); return s; }
    [Benchmark] public Fixed64 Multiply_Checked() { Fixed64 r = Fixed64.OneValue; for (int i = 0; i < N; i++) r = (i & 1) == 0 ? r * _values[i] : r / _values[i]; return r; }
    [Benchmark] public Fixed64 Sqrt() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N; i++) s = Fixed64.AddFast(s, Fixed64.Sqrt(_values[i])); return s; }
    [Benchmark] public Fixed64 Sin() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N; i++) s = Fixed64.AddFast(s, Fixed64.Sin(_angles[i])); return s; }
    [Benchmark] public Fixed64 Cos() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N; i++) s = Fixed64.AddFast(s, Fixed64.Cos(_angles[i])); return s; }
    [Benchmark] public Fixed64 Atan2() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N; i++) s = Fixed64.AddFast(s, Fixed64.Atan2(_values[i], _angles[i])); return s; }
}

[MemoryDiagnoser]
[ShortRunJob]
public class VectorBenchmarks
{
    private const int N = 1_000;
    private Fixed64Vec3[] _vecs = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _vecs = new Fixed64Vec3[N];
        for (int i = 0; i < N; i++)
            _vecs[i] = Fixed64Vec3.FromFloat((float)((rng.NextDouble()-0.5)*200), (float)((rng.NextDouble()-0.5)*200), (float)((rng.NextDouble()-0.5)*200));
    }

    [Benchmark] public Fixed64 Vec3_LengthSquared() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N; i++) s = Fixed64.AddFast(s, _vecs[i].LengthSquared()); return s; }
    [Benchmark] public Fixed64 Vec3_Length() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N; i++) s = Fixed64.AddFast(s, _vecs[i].Length()); return s; }
    [Benchmark] public Fixed64Vec3 Vec3_Normalize() { var s = Fixed64Vec3.Zero; for (int i = 0; i < N; i++) { var n = _vecs[i].Normalized(); s = new Fixed64Vec3(Fixed64.AddFast(s.X,n.X), Fixed64.AddFast(s.Y,n.Y), Fixed64.AddFast(s.Z,n.Z)); } return s; }
    [Benchmark] public Fixed64 Vec3_DistanceSquared() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N-1; i++) s = Fixed64.AddFast(s, Fixed64Vec3.DistanceSquared(_vecs[i], _vecs[i+1])); return s; }
    [Benchmark] public Fixed64 Vec3_Distance() { Fixed64 s = Fixed64.Zero; for (int i = 0; i < N-1; i++) s = Fixed64.AddFast(s, Fixed64Vec3.Distance(_vecs[i], _vecs[i+1])); return s; }
}
