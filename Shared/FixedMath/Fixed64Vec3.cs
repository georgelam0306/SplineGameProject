using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 3D vector using Fixed64 components for deterministic math.
/// </summary>
public readonly struct Fixed64Vec3 : IEquatable<Fixed64Vec3>
{
    public readonly Fixed64 X;
    public readonly Fixed64 Y;
    public readonly Fixed64 Z;

    public static readonly Fixed64Vec3 Zero = new(Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
    public static readonly Fixed64Vec3 One = new(Fixed64.OneValue, Fixed64.OneValue, Fixed64.OneValue);
    public static readonly Fixed64Vec3 UnitX = new(Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero);
    public static readonly Fixed64Vec3 UnitY = new(Fixed64.Zero, Fixed64.OneValue, Fixed64.Zero);
    public static readonly Fixed64Vec3 UnitZ = new(Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue);
    public static readonly Fixed64Vec3 Up = UnitY;
    public static readonly Fixed64Vec3 Down = new(Fixed64.Zero, -Fixed64.OneValue, Fixed64.Zero);
    public static readonly Fixed64Vec3 Right = UnitX;
    public static readonly Fixed64Vec3 Left = new(-Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero);
    public static readonly Fixed64Vec3 Forward = UnitZ;
    public static readonly Fixed64Vec3 Back = new(Fixed64.Zero, Fixed64.Zero, -Fixed64.OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3(Fixed64 x, Fixed64 y, Fixed64 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3(Fixed64Vec2 xy, Fixed64 z)
    {
        X = xy.X;
        Y = xy.Y;
        Z = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 FromInt(int x, int y, int z)
    {
        return new Fixed64Vec3(Fixed64.FromInt(x), Fixed64.FromInt(y), Fixed64.FromInt(z));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 FromFloat(float x, float y, float z)
    {
        return new Fixed64Vec3(Fixed64.FromFloat(x), Fixed64.FromFloat(y), Fixed64.FromFloat(z));
    }

    // ============================================================
    // Arithmetic Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator +(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return new Fixed64Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator -(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return new Fixed64Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator -(Fixed64Vec3 a)
    {
        return new Fixed64Vec3(-a.X, -a.Y, -a.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator *(Fixed64Vec3 a, Fixed64 scalar)
    {
        return new Fixed64Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator *(Fixed64 scalar, Fixed64Vec3 a)
    {
        return new Fixed64Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator *(Fixed64Vec3 a, int scalar)
    {
        return new Fixed64Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator *(int scalar, Fixed64Vec3 a)
    {
        return new Fixed64Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator /(Fixed64Vec3 a, Fixed64 scalar)
    {
        return new Fixed64Vec3(a.X / scalar, a.Y / scalar, a.Z / scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator /(Fixed64Vec3 a, int scalar)
    {
        return new Fixed64Vec3(a.X / scalar, a.Y / scalar, a.Z / scalar);
    }

    // ============================================================
    // Component-wise Operations
    // ============================================================

    /// <summary>
    /// Component-wise multiplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Scale(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return new Fixed64Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    }

    // ============================================================
    // Comparison Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return a.X != b.X || a.Y != b.Y || a.Z != b.Z;
    }

    // ============================================================
    // Vector Operations
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Dot(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Cross(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return new Fixed64Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 LengthSquared()
    {
        return X * X + Y * Y + Z * Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Length()
    {
        return Fixed64.Sqrt(LengthSquared());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3 Normalized()
    {
        Fixed64 lengthSq = LengthSquared();
        if (lengthSq.Raw == 0)
        {
            return Zero;
        }

        Fixed64 length = Fixed64.Sqrt(lengthSq);
        if (length.Raw == 0)
        {
            return Zero;
        }

        return new Fixed64Vec3(X / length, Y / length, Z / length);
    }

    /// <summary>
    /// Normalizes the vector and outputs the length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3 NormalizedWithLength(out Fixed64 length)
    {
        Fixed64 lengthSq = LengthSquared();
        if (lengthSq.Raw == 0)
        {
            length = Fixed64.Zero;
            return Zero;
        }

        length = Fixed64.Sqrt(lengthSq);
        if (length.Raw == 0)
        {
            return Zero;
        }

        return new Fixed64Vec3(X / length, Y / length, Z / length);
    }

    // ============================================================
    // Distance Functions
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Distance(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return (b - a).Length();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 DistanceSquared(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return (b - a).LengthSquared();
    }

    // ============================================================
    // Interpolation Functions
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Lerp(Fixed64Vec3 a, Fixed64Vec3 b, Fixed64 t)
    {
        return new Fixed64Vec3(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t
        );
    }

    /// <summary>
    /// Unclamped linear interpolation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 LerpUnclamped(Fixed64Vec3 a, Fixed64Vec3 b, Fixed64 t)
    {
        return Lerp(a, b, t);
    }

    /// <summary>
    /// Spherical linear interpolation between two vectors.
    /// </summary>
    public static Fixed64Vec3 Slerp(Fixed64Vec3 a, Fixed64Vec3 b, Fixed64 t)
    {
        Fixed64 dot = Fixed64.Clamp(Dot(a.Normalized(), b.Normalized()), -Fixed64.OneValue, Fixed64.OneValue);
        Fixed64 theta = Fixed64.Acos(dot) * t;

        Fixed64Vec3 relativeVec = (b - a * dot).Normalized();
        Fixed64.SinCosLUT(theta, out Fixed64 sin, out Fixed64 cos);

        return a * cos + relativeVec * sin;
    }

    /// <summary>
    /// Moves a vector towards a target.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 MoveTowards(Fixed64Vec3 current, Fixed64Vec3 target, Fixed64 maxDistanceDelta)
    {
        Fixed64Vec3 diff = target - current;
        Fixed64 distSq = diff.LengthSquared();

        if (distSq.Raw == 0 || (maxDistanceDelta.Raw >= 0 && distSq <= maxDistanceDelta * maxDistanceDelta))
        {
            return target;
        }

        Fixed64 dist = Fixed64.Sqrt(distSq);
        return current + diff / dist * maxDistanceDelta;
    }

    // ============================================================
    // Min/Max/Clamp Functions
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Min(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return new Fixed64Vec3(
            Fixed64.Min(a.X, b.X),
            Fixed64.Min(a.Y, b.Y),
            Fixed64.Min(a.Z, b.Z)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Max(Fixed64Vec3 a, Fixed64Vec3 b)
    {
        return new Fixed64Vec3(
            Fixed64.Max(a.X, b.X),
            Fixed64.Max(a.Y, b.Y),
            Fixed64.Max(a.Z, b.Z)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Clamp(Fixed64Vec3 value, Fixed64Vec3 min, Fixed64Vec3 max)
    {
        return new Fixed64Vec3(
            Fixed64.Clamp(value.X, min.X, max.X),
            Fixed64.Clamp(value.Y, min.Y, max.Y),
            Fixed64.Clamp(value.Z, min.Z, max.Z)
        );
    }

    /// <summary>
    /// Clamps the magnitude of a vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 ClampMagnitude(Fixed64Vec3 vector, Fixed64 maxLength)
    {
        Fixed64 lengthSq = vector.LengthSquared();
        Fixed64 maxLengthSq = maxLength * maxLength;

        if (lengthSq > maxLengthSq)
        {
            Fixed64 length = Fixed64.Sqrt(lengthSq);
            return vector / length * maxLength;
        }

        return vector;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Abs(Fixed64Vec3 value)
    {
        return new Fixed64Vec3(Fixed64.Abs(value.X), Fixed64.Abs(value.Y), Fixed64.Abs(value.Z));
    }

    // ============================================================
    // Reflection and Projection
    // ============================================================

    /// <summary>
    /// Reflects a vector off a surface with the given normal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Reflect(Fixed64Vec3 direction, Fixed64Vec3 normal)
    {
        Fixed64 dotProduct = Dot(direction, normal);
        return direction - normal * dotProduct * 2;
    }

    /// <summary>
    /// Projects a vector onto another vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 Project(Fixed64Vec3 vector, Fixed64Vec3 onNormal)
    {
        Fixed64 sqrMag = Dot(onNormal, onNormal);
        if (sqrMag.Raw == 0)
        {
            return Zero;
        }

        Fixed64 dot = Dot(vector, onNormal);
        return onNormal * dot / sqrMag;
    }

    /// <summary>
    /// Projects a vector onto a plane defined by a normal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 ProjectOnPlane(Fixed64Vec3 vector, Fixed64Vec3 planeNormal)
    {
        return vector - Project(vector, planeNormal);
    }

    // ============================================================
    // Angle Functions
    // ============================================================

    /// <summary>
    /// Returns the angle in radians between two vectors.
    /// </summary>
    public static Fixed64 Angle(Fixed64Vec3 from, Fixed64Vec3 to)
    {
        Fixed64 denominator = Fixed64.Sqrt(from.LengthSquared() * to.LengthSquared());
        if (denominator.Raw == 0)
        {
            return Fixed64.Zero;
        }

        Fixed64 dot = Fixed64.Clamp(Dot(from, to) / denominator, -Fixed64.OneValue, Fixed64.OneValue);
        return Fixed64.Acos(dot);
    }

    /// <summary>
    /// Returns the signed angle in radians between two vectors around an axis.
    /// </summary>
    public static Fixed64 SignedAngle(Fixed64Vec3 from, Fixed64Vec3 to, Fixed64Vec3 axis)
    {
        Fixed64 unsignedAngle = Angle(from, to);
        Fixed64Vec3 cross = Cross(from, to);
        Fixed64 sign = Fixed64.Sign(Dot(axis, cross));
        return unsignedAngle * sign;
    }

    // ============================================================
    // Rotation Functions
    // ============================================================

    /// <summary>
    /// Rotates a vector around an axis by the given angle in radians.
    /// </summary>
    public static Fixed64Vec3 RotateAround(Fixed64Vec3 point, Fixed64Vec3 axis, Fixed64 angle)
    {
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        Fixed64Vec3 u = axis.Normalized();

        // Rodrigues' rotation formula
        return point * cos + Cross(u, point) * sin + u * Dot(u, point) * (Fixed64.OneValue - cos);
    }

    // ============================================================
    // Swizzle Properties
    // ============================================================

    public Fixed64Vec2 XY => new Fixed64Vec2(X, Y);
    public Fixed64Vec2 XZ => new Fixed64Vec2(X, Z);
    public Fixed64Vec2 YZ => new Fixed64Vec2(Y, Z);

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed64Vec3 other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64Vec3 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Z);
    }

    public override string ToString()
    {
        return $"({X}, {Y}, {Z})";
    }

    // ============================================================
    // Conversion Functions
    // ============================================================

    public (float x, float y, float z) ToFloat()
    {
        return (X.ToFloat(), Y.ToFloat(), Z.ToFloat());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public System.Numerics.Vector3 ToVector3()
    {
        return new System.Numerics.Vector3(X.ToFloat(), Y.ToFloat(), Z.ToFloat());
    }
}
