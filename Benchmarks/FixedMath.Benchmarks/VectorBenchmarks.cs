using System.Numerics;
using BenchmarkDotNet.Attributes;
using FixedMath;

namespace FixedMath.Benchmarks;

[MemoryDiagnoser]
public class VectorBenchmarks
{
    private Fixed64Vec2 _fixedVec2A;
    private Fixed64Vec2 _fixedVec2B;
    private Fixed64Vec3 _fixedVec3A;
    private Fixed64Vec3 _fixedVec3B;
    private Vector2 _vec2A;
    private Vector2 _vec2B;
    private Vector3 _vec3A;
    private Vector3 _vec3B;

    [GlobalSetup]
    public void Setup()
    {
        _fixedVec2A = Fixed64Vec2.FromFloat(10.5f, 20.3f);
        _fixedVec2B = Fixed64Vec2.FromFloat(5.2f, 8.7f);
        _fixedVec3A = Fixed64Vec3.FromFloat(10.5f, 20.3f, 15.8f);
        _fixedVec3B = Fixed64Vec3.FromFloat(5.2f, 8.7f, 3.1f);
        _vec2A = new Vector2(10.5f, 20.3f);
        _vec2B = new Vector2(5.2f, 8.7f);
        _vec3A = new Vector3(10.5f, 20.3f, 15.8f);
        _vec3B = new Vector3(5.2f, 8.7f, 3.1f);
    }

    // ============================================================
    // Vec2 Operations
    // ============================================================

    [Benchmark]
    public Fixed64Vec2 Fixed64Vec2_Add() => _fixedVec2A + _fixedVec2B;

    [Benchmark]
    public Vector2 Vector2_Add() => _vec2A + _vec2B;

    [Benchmark]
    public Fixed64Vec2 Fixed64Vec2_Multiply() => _fixedVec2A * Fixed64.FromFloat(2.5f);

    [Benchmark]
    public Vector2 Vector2_Multiply() => _vec2A * 2.5f;

    [Benchmark]
    public Fixed64 Fixed64Vec2_Dot() => Fixed64Vec2.Dot(_fixedVec2A, _fixedVec2B);

    [Benchmark]
    public float Vector2_Dot() => Vector2.Dot(_vec2A, _vec2B);

    [Benchmark]
    public Fixed64 Fixed64Vec2_Length() => _fixedVec2A.Length();

    [Benchmark]
    public float Vector2_Length() => _vec2A.Length();

    [Benchmark]
    public Fixed64Vec2 Fixed64Vec2_Normalize() => _fixedVec2A.Normalized();

    [Benchmark]
    public Vector2 Vector2_Normalize() => Vector2.Normalize(_vec2A);

    [Benchmark]
    public Fixed64 Fixed64Vec2_Distance() => Fixed64Vec2.Distance(_fixedVec2A, _fixedVec2B);

    [Benchmark]
    public float Vector2_Distance() => Vector2.Distance(_vec2A, _vec2B);

    // ============================================================
    // Vec3 Operations
    // ============================================================

    [Benchmark]
    public Fixed64Vec3 Fixed64Vec3_Add() => _fixedVec3A + _fixedVec3B;

    [Benchmark]
    public Vector3 Vector3_Add() => _vec3A + _vec3B;

    [Benchmark]
    public Fixed64 Fixed64Vec3_Dot() => Fixed64Vec3.Dot(_fixedVec3A, _fixedVec3B);

    [Benchmark]
    public float Vector3_Dot() => Vector3.Dot(_vec3A, _vec3B);

    [Benchmark]
    public Fixed64Vec3 Fixed64Vec3_Cross() => Fixed64Vec3.Cross(_fixedVec3A, _fixedVec3B);

    [Benchmark]
    public Vector3 Vector3_Cross() => Vector3.Cross(_vec3A, _vec3B);

    [Benchmark]
    public Fixed64 Fixed64Vec3_Length() => _fixedVec3A.Length();

    [Benchmark]
    public float Vector3_Length() => _vec3A.Length();

    [Benchmark]
    public Fixed64Vec3 Fixed64Vec3_Normalize() => _fixedVec3A.Normalized();

    [Benchmark]
    public Vector3 Vector3_Normalize() => Vector3.Normalize(_vec3A);

    [Benchmark]
    public Fixed64Vec3 Fixed64Vec3_Lerp() => Fixed64Vec3.Lerp(_fixedVec3A, _fixedVec3B, Fixed64.FromFloat(0.5f));

    [Benchmark]
    public Vector3 Vector3_Lerp() => Vector3.Lerp(_vec3A, _vec3B, 0.5f);

    [Benchmark]
    public Fixed64 Fixed64Vec3_Angle() => Fixed64Vec3.Angle(_fixedVec3A, _fixedVec3B);
}
