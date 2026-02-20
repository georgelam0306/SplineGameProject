using BenchmarkDotNet.Attributes;
using FixedMath;

namespace FixedMath.Benchmarks;

[MemoryDiagnoser]
public class Fixed64Benchmarks
{
    private Fixed64 _a;
    private Fixed64 _b;
    private Fixed64 _angle;
    private float _floatA;
    private float _floatB;
    private float _floatAngle;

    [GlobalSetup]
    public void Setup()
    {
        _a = Fixed64.FromFloat(123.456f);
        _b = Fixed64.FromFloat(78.901f);
        _angle = Fixed64.FromFloat(1.234f);
        _floatA = 123.456f;
        _floatB = 78.901f;
        _floatAngle = 1.234f;
    }

    // ============================================================
    // Basic Arithmetic - Fixed64 vs float
    // ============================================================

    [Benchmark]
    public Fixed64 Fixed64_Add() => _a + _b;

    [Benchmark]
    public float Float_Add() => _floatA + _floatB;

    [Benchmark]
    public Fixed64 Fixed64_Multiply() => _a * _b;

    [Benchmark]
    public float Float_Multiply() => _floatA * _floatB;

    [Benchmark]
    public Fixed64 Fixed64_Divide() => _a / _b;

    [Benchmark]
    public float Float_Divide() => _floatA / _floatB;

    [Benchmark]
    public Fixed64 Fixed64_MultiplyFast() => Fixed64.MulFast(_a, _b);

    // ============================================================
    // Math Functions
    // ============================================================

    [Benchmark]
    public Fixed64 Fixed64_Sqrt() => Fixed64.Sqrt(_a);

    [Benchmark]
    public float Float_Sqrt() => MathF.Sqrt(_floatA);

    [Benchmark]
    public Fixed64 Fixed64_Sin() => Fixed64.Sin(_angle);

    [Benchmark]
    public float Float_Sin() => MathF.Sin(_floatAngle);

    [Benchmark]
    public Fixed64 Fixed64_SinLUT() => Fixed64.SinLUT(_angle);

    [Benchmark]
    public Fixed64 Fixed64_Cos() => Fixed64.Cos(_angle);

    [Benchmark]
    public float Float_Cos() => MathF.Cos(_floatAngle);

    [Benchmark]
    public Fixed64 Fixed64_CosLUT() => Fixed64.CosLUT(_angle);

    [Benchmark]
    public void Fixed64_SinCosLUT()
    {
        Fixed64.SinCosLUT(_angle, out _, out _);
    }

    [Benchmark]
    public Fixed64 Fixed64_Atan2() => Fixed64.Atan2(_a, _b);

    [Benchmark]
    public float Float_Atan2() => MathF.Atan2(_floatA, _floatB);

    // ============================================================
    // Power and Log
    // ============================================================

    [Benchmark]
    public Fixed64 Fixed64_Pow2() => Fixed64.Pow2(_a);

    [Benchmark]
    public Fixed64 Fixed64_Pow_Int3() => Fixed64.Pow(_a, 3);

    [Benchmark]
    public Fixed64 Fixed64_Log2() => Fixed64.Log2(_a);

    [Benchmark]
    public float Float_Log2() => MathF.Log2(_floatA);

    [Benchmark]
    public Fixed64 Fixed64_Exp() => Fixed64.Exp(Fixed64.FromFloat(2.5f));

    [Benchmark]
    public float Float_Exp() => MathF.Exp(2.5f);

    // ============================================================
    // Interpolation
    // ============================================================

    [Benchmark]
    public Fixed64 Fixed64_Lerp() => Fixed64.Lerp(_a, _b, Fixed64.FromFloat(0.5f));

    [Benchmark]
    public Fixed64 Fixed64_SmoothStep() => Fixed64.SmoothStep(Fixed64.Zero, Fixed64.OneValue, Fixed64.FromFloat(0.5f));

    [Benchmark]
    public Fixed64 Fixed64_MoveTowards() => Fixed64.MoveTowards(_a, _b, Fixed64.FromFloat(10f));
}
