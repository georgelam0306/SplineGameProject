using System.Numerics;
using BenchmarkDotNet.Attributes;
using FixedMath;

namespace FixedMath.Benchmarks;

[MemoryDiagnoser]
public class QuaternionMatrixBenchmarks
{
    private Fixed64Quaternion _fixedQuatA;
    private Fixed64Quaternion _fixedQuatB;
    private Fixed64Vec3 _fixedVec3;
    private Quaternion _quatA;
    private Quaternion _quatB;
    private Vector3 _vec3;
    private Fixed64Mat3x3 _fixed3x3;
    private Fixed64Mat4x4 _fixed4x4A;
    private Fixed64Mat4x4 _fixed4x4B;
    private Matrix4x4 _mat4x4A;
    private Matrix4x4 _mat4x4B;

    [GlobalSetup]
    public void Setup()
    {
        _fixedQuatA = Fixed64Quaternion.FromAxisAngle(Fixed64Vec3.UnitY, Fixed64.FromFloat(0.5f));
        _fixedQuatB = Fixed64Quaternion.FromAxisAngle(Fixed64Vec3.UnitX, Fixed64.FromFloat(0.3f));
        _fixedVec3 = Fixed64Vec3.FromFloat(1f, 2f, 3f);
        _quatA = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        _quatB = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);
        _vec3 = new Vector3(1f, 2f, 3f);
        _fixed3x3 = Fixed64Mat3x3.CreateFromQuaternion(_fixedQuatA);
        _fixed4x4A = Fixed64Mat4x4.CreateTRS(
            Fixed64Vec3.FromFloat(10f, 20f, 30f),
            _fixedQuatA,
            Fixed64Vec3.One
        );
        _fixed4x4B = Fixed64Mat4x4.CreateTRS(
            Fixed64Vec3.FromFloat(5f, 10f, 15f),
            _fixedQuatB,
            Fixed64Vec3.FromFloat(2f, 2f, 2f)
        );
        _mat4x4A = Matrix4x4.CreateTranslation(10f, 20f, 30f) * Matrix4x4.CreateFromQuaternion(_quatA);
        _mat4x4B = Matrix4x4.CreateTranslation(5f, 10f, 15f) * Matrix4x4.CreateFromQuaternion(_quatB) * Matrix4x4.CreateScale(2f);
    }

    // ============================================================
    // Quaternion Operations
    // ============================================================

    [Benchmark]
    public Fixed64Quaternion FixedQuat_Multiply() => _fixedQuatA * _fixedQuatB;

    [Benchmark]
    public Quaternion Quat_Multiply() => _quatA * _quatB;

    [Benchmark]
    public Fixed64Vec3 FixedQuat_RotateVector() => _fixedQuatA * _fixedVec3;

    [Benchmark]
    public Vector3 Quat_RotateVector() => Vector3.Transform(_vec3, _quatA);

    [Benchmark]
    public Fixed64Quaternion FixedQuat_Normalize() => _fixedQuatA.Normalized();

    [Benchmark]
    public Quaternion Quat_Normalize() => Quaternion.Normalize(_quatA);

    [Benchmark]
    public Fixed64Quaternion FixedQuat_Slerp() => Fixed64Quaternion.Slerp(_fixedQuatA, _fixedQuatB, Fixed64.FromFloat(0.5f));

    [Benchmark]
    public Quaternion Quat_Slerp() => Quaternion.Slerp(_quatA, _quatB, 0.5f);

    [Benchmark]
    public Fixed64Quaternion FixedQuat_FromAxisAngle() => Fixed64Quaternion.FromAxisAngle(Fixed64Vec3.UnitY, Fixed64.FromFloat(1.0f));

    [Benchmark]
    public Quaternion Quat_FromAxisAngle() => Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.0f);

    [Benchmark]
    public Fixed64Vec3 FixedQuat_ToEuler() => _fixedQuatA.ToEuler();

    // ============================================================
    // 3x3 Matrix Operations
    // ============================================================

    [Benchmark]
    public Fixed64Mat3x3 Fixed64Mat3x3_CreateRotationY() => Fixed64Mat3x3.CreateRotationY(Fixed64.FromFloat(1.0f));

    [Benchmark]
    public Fixed64Vec3 Fixed64Mat3x3_Transform() => _fixed3x3 * _fixedVec3;

    [Benchmark]
    public Fixed64Mat3x3 Fixed64Mat3x3_Inverse() => _fixed3x3.Inverse();

    [Benchmark]
    public Fixed64Quaternion Fixed64Mat3x3_ToQuaternion() => _fixed3x3.ToQuaternion();

    // ============================================================
    // 4x4 Matrix Operations
    // ============================================================

    [Benchmark]
    public Fixed64Mat4x4 Fixed64Mat4x4_Multiply() => _fixed4x4A * _fixed4x4B;

    [Benchmark]
    public Matrix4x4 Mat4x4_Multiply() => _mat4x4A * _mat4x4B;

    [Benchmark]
    public Fixed64Vec3 Fixed64Mat4x4_TransformPoint() => _fixed4x4A.TransformPoint(_fixedVec3);

    [Benchmark]
    public Vector3 Mat4x4_TransformPoint() => Vector3.Transform(_vec3, _mat4x4A);

    [Benchmark]
    public Fixed64Mat4x4 Fixed64Mat4x4_Inverse() => _fixed4x4A.Inverse();

    [Benchmark]
    public Matrix4x4 Mat4x4_Inverse()
    {
        Matrix4x4.Invert(_mat4x4A, out var result);
        return result;
    }

    [Benchmark]
    public Fixed64Mat4x4 Fixed64Mat4x4_CreateTRS() => Fixed64Mat4x4.CreateTRS(
        Fixed64Vec3.FromFloat(10f, 20f, 30f),
        _fixedQuatA,
        Fixed64Vec3.One
    );

    [Benchmark]
    public void Fixed64Mat4x4_Decompose()
    {
        _fixed4x4A.Decompose(out _, out _, out _);
    }
}
