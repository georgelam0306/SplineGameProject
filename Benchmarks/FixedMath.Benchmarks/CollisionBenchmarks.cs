using BenchmarkDotNet.Attributes;
using FixedMath;

namespace FixedMath.Benchmarks;

[MemoryDiagnoser]
public class CollisionBenchmarks
{
    private Fixed64BoundingBox _boxA;
    private Fixed64BoundingBox _boxB;
    private Fixed64BoundingSphere _sphereA;
    private Fixed64BoundingSphere _sphereB;
    private Fixed64Vec3 _point;
    private Fixed64Vec3 _rayOrigin;
    private Fixed64Vec3 _rayDirection;

    [GlobalSetup]
    public void Setup()
    {
        _boxA = new Fixed64BoundingBox(
            Fixed64Vec3.FromFloat(-10f, -10f, -10f),
            Fixed64Vec3.FromFloat(10f, 10f, 10f)
        );
        _boxB = new Fixed64BoundingBox(
            Fixed64Vec3.FromFloat(5f, 5f, 5f),
            Fixed64Vec3.FromFloat(20f, 20f, 20f)
        );
        _sphereA = new Fixed64BoundingSphere(Fixed64Vec3.Zero, Fixed64.FromFloat(10f));
        _sphereB = new Fixed64BoundingSphere(Fixed64Vec3.FromFloat(15f, 0f, 0f), Fixed64.FromFloat(8f));
        _point = Fixed64Vec3.FromFloat(5f, 5f, 5f);
        _rayOrigin = Fixed64Vec3.FromFloat(-20f, 0f, 0f);
        _rayDirection = Fixed64Vec3.UnitX;
    }

    // ============================================================
    // Fixed64BoundingBox
    // ============================================================

    [Benchmark]
    public bool Fixed64BoundingBox_ContainsPoint() => _boxA.Contains(_point);

    [Benchmark]
    public bool Fixed64BoundingBox_ContainsBox() => _boxA.Contains(_boxB);

    [Benchmark]
    public bool Fixed64BoundingBox_Intersects() => _boxA.Intersects(_boxB);

    [Benchmark]
    public bool Fixed64BoundingBox_IntersectsSphere() => _boxA.Intersects(_sphereA);

    [Benchmark]
    public bool Fixed64BoundingBox_IntersectsRay()
    {
        return _boxA.IntersectsRay(_rayOrigin, _rayDirection, out _);
    }

    [Benchmark]
    public Fixed64Vec3 Fixed64BoundingBox_ClosestPoint() => _boxA.ClosestPoint(_point);

    [Benchmark]
    public Fixed64 Fixed64BoundingBox_Distance() => _boxA.Distance(_point);

    [Benchmark]
    public Fixed64BoundingBox Fixed64BoundingBox_Encapsulate() => _boxA.Encapsulate(_boxB);

    [Benchmark]
    public Fixed64BoundingBox Fixed64BoundingBox_Union() => Fixed64BoundingBox.Union(_boxA, _boxB);

    [Benchmark]
    public Fixed64BoundingBox Fixed64BoundingBox_Intersection() => Fixed64BoundingBox.Intersection(_boxA, _boxB);

    // ============================================================
    // Fixed64BoundingSphere
    // ============================================================

    [Benchmark]
    public bool Fixed64BoundingSphere_ContainsPoint() => _sphereA.Contains(_point);

    [Benchmark]
    public bool Fixed64BoundingSphere_ContainsSphere() => _sphereA.Contains(_sphereB);

    [Benchmark]
    public bool Fixed64BoundingSphere_Intersects() => _sphereA.Intersects(_sphereB);

    [Benchmark]
    public bool Fixed64BoundingSphere_IntersectsBox() => _sphereA.Intersects(_boxA);

    [Benchmark]
    public bool Fixed64BoundingSphere_IntersectsRay()
    {
        return _sphereA.IntersectsRay(_rayOrigin, _rayDirection, out _);
    }

    [Benchmark]
    public Fixed64Vec3 Fixed64BoundingSphere_ClosestPoint() => _sphereA.ClosestPoint(_point);

    [Benchmark]
    public Fixed64 Fixed64BoundingSphere_Distance() => _sphereA.Distance(_point);

    [Benchmark]
    public Fixed64BoundingSphere Fixed64BoundingSphere_Encapsulate() => _sphereA.Encapsulate(_sphereB);
}
