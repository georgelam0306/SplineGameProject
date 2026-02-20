using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 3D vector using Fixed32 components for deterministic math.
/// </summary>
public readonly struct Fixed32Vec3 : IEquatable<Fixed32Vec3>
{
    public readonly Fixed32 X;
    public readonly Fixed32 Y;
    public readonly Fixed32 Z;

    public static readonly Fixed32Vec3 Zero = new(Fixed32.Zero, Fixed32.Zero, Fixed32.Zero);
    public static readonly Fixed32Vec3 One = new(Fixed32.OneValue, Fixed32.OneValue, Fixed32.OneValue);
    public static readonly Fixed32Vec3 UnitX = new(Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero);
    public static readonly Fixed32Vec3 UnitY = new(Fixed32.Zero, Fixed32.OneValue, Fixed32.Zero);
    public static readonly Fixed32Vec3 UnitZ = new(Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue);
    public static readonly Fixed32Vec3 Up = UnitY;
    public static readonly Fixed32Vec3 Down = new(Fixed32.Zero, -Fixed32.OneValue, Fixed32.Zero);
    public static readonly Fixed32Vec3 Right = UnitX;
    public static readonly Fixed32Vec3 Left = new(-Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero);
    public static readonly Fixed32Vec3 Forward = UnitZ;
    public static readonly Fixed32Vec3 Back = new(Fixed32.Zero, Fixed32.Zero, -Fixed32.OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec3(Fixed32 x, Fixed32 y, Fixed32 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec3(Fixed32Vec2 xy, Fixed32 z)
    {
        X = xy.X;
        Y = xy.Y;
        Z = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 FromInt(int x, int y, int z)
    {
        return new Fixed32Vec3(Fixed32.FromInt(x), Fixed32.FromInt(y), Fixed32.FromInt(z));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 FromFloat(float x, float y, float z)
    {
        return new Fixed32Vec3(Fixed32.FromFloat(x), Fixed32.FromFloat(y), Fixed32.FromFloat(z));
    }

    // ============================================================
    // Arithmetic Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator +(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return new Fixed32Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator -(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return new Fixed32Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator -(Fixed32Vec3 a)
    {
        return new Fixed32Vec3(-a.X, -a.Y, -a.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator *(Fixed32Vec3 a, Fixed32 scalar)
    {
        return new Fixed32Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator *(Fixed32 scalar, Fixed32Vec3 a)
    {
        return new Fixed32Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator *(Fixed32Vec3 a, int scalar)
    {
        return new Fixed32Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator *(int scalar, Fixed32Vec3 a)
    {
        return new Fixed32Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator /(Fixed32Vec3 a, Fixed32 scalar)
    {
        return new Fixed32Vec3(a.X / scalar, a.Y / scalar, a.Z / scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator /(Fixed32Vec3 a, int scalar)
    {
        return new Fixed32Vec3(a.X / scalar, a.Y / scalar, a.Z / scalar);
    }

    // ============================================================
    // Component-wise Operations
    // ============================================================

    /// <summary>
    /// Component-wise multiplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Scale(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return new Fixed32Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    }

    // ============================================================
    // Comparison Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return a.X != b.X || a.Y != b.Y || a.Z != b.Z;
    }

    // ============================================================
    // Vector Operations
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Dot(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Cross(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return new Fixed32Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 LengthSquared()
    {
        return X * X + Y * Y + Z * Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Length()
    {
        return Fixed32.Sqrt(LengthSquared());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec3 Normalized()
    {
        Fixed32 lengthSq = LengthSquared();
        if (lengthSq.Raw == 0)
        {
            return Zero;
        }

        Fixed32 length = Fixed32.Sqrt(lengthSq);
        if (length.Raw == 0)
        {
            return Zero;
        }

        return new Fixed32Vec3(X / length, Y / length, Z / length);
    }

    /// <summary>
    /// Normalizes the vector and outputs the length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec3 NormalizedWithLength(out Fixed32 length)
    {
        Fixed32 lengthSq = LengthSquared();
        if (lengthSq.Raw == 0)
        {
            length = Fixed32.Zero;
            return Zero;
        }

        length = Fixed32.Sqrt(lengthSq);
        if (length.Raw == 0)
        {
            return Zero;
        }

        return new Fixed32Vec3(X / length, Y / length, Z / length);
    }

    // ============================================================
    // Distance Functions
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Distance(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return (b - a).Length();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 DistanceSquared(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return (b - a).LengthSquared();
    }

    // ============================================================
    // Interpolation Functions
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Lerp(Fixed32Vec3 a, Fixed32Vec3 b, Fixed32 t)
    {
        return new Fixed32Vec3(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t
        );
    }

    /// <summary>
    /// Unclamped linear interpolation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 LerpUnclamped(Fixed32Vec3 a, Fixed32Vec3 b, Fixed32 t)
    {
        return Lerp(a, b, t);
    }

    /// <summary>
    /// Spherical linear interpolation between two vectors.
    /// </summary>
    public static Fixed32Vec3 Slerp(Fixed32Vec3 a, Fixed32Vec3 b, Fixed32 t)
    {
        Fixed32 dot = Fixed32.Clamp(Dot(a.Normalized(), b.Normalized()), -Fixed32.OneValue, Fixed32.OneValue);
        Fixed32 theta = Fixed32.Acos(dot) * t;

        Fixed32Vec3 relativeVec = (b - a * dot).Normalized();
        Fixed32.SinCosLUT(theta, out Fixed32 sin, out Fixed32 cos);

        return a * cos + relativeVec * sin;
    }

    /// <summary>
    /// Moves a vector towards a target.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 MoveTowards(Fixed32Vec3 current, Fixed32Vec3 target, Fixed32 maxDistanceDelta)
    {
        Fixed32Vec3 diff = target - current;
        Fixed32 distSq = diff.LengthSquared();

        if (distSq.Raw == 0 || (maxDistanceDelta.Raw >= 0 && distSq <= maxDistanceDelta * maxDistanceDelta))
        {
            return target;
        }

        Fixed32 dist = Fixed32.Sqrt(distSq);
        return current + diff / dist * maxDistanceDelta;
    }

    // ============================================================
    // Min/Max/Clamp Functions
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Min(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return new Fixed32Vec3(
            Fixed32.Min(a.X, b.X),
            Fixed32.Min(a.Y, b.Y),
            Fixed32.Min(a.Z, b.Z)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Max(Fixed32Vec3 a, Fixed32Vec3 b)
    {
        return new Fixed32Vec3(
            Fixed32.Max(a.X, b.X),
            Fixed32.Max(a.Y, b.Y),
            Fixed32.Max(a.Z, b.Z)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Clamp(Fixed32Vec3 value, Fixed32Vec3 min, Fixed32Vec3 max)
    {
        return new Fixed32Vec3(
            Fixed32.Clamp(value.X, min.X, max.X),
            Fixed32.Clamp(value.Y, min.Y, max.Y),
            Fixed32.Clamp(value.Z, min.Z, max.Z)
        );
    }

    /// <summary>
    /// Clamps the magnitude of a vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 ClampMagnitude(Fixed32Vec3 vector, Fixed32 maxLength)
    {
        Fixed32 lengthSq = vector.LengthSquared();
        Fixed32 maxLengthSq = maxLength * maxLength;

        if (lengthSq > maxLengthSq)
        {
            Fixed32 length = Fixed32.Sqrt(lengthSq);
            return vector / length * maxLength;
        }

        return vector;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Abs(Fixed32Vec3 value)
    {
        return new Fixed32Vec3(Fixed32.Abs(value.X), Fixed32.Abs(value.Y), Fixed32.Abs(value.Z));
    }

    // ============================================================
    // Reflection and Projection
    // ============================================================

    /// <summary>
    /// Reflects a vector off a surface with the given normal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Reflect(Fixed32Vec3 direction, Fixed32Vec3 normal)
    {
        Fixed32 dotProduct = Dot(direction, normal);
        return direction - normal * dotProduct * 2;
    }

    /// <summary>
    /// Projects a vector onto another vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 Project(Fixed32Vec3 vector, Fixed32Vec3 onNormal)
    {
        Fixed32 sqrMag = Dot(onNormal, onNormal);
        if (sqrMag.Raw == 0)
        {
            return Zero;
        }

        Fixed32 dot = Dot(vector, onNormal);
        return onNormal * dot / sqrMag;
    }

    /// <summary>
    /// Projects a vector onto a plane defined by a normal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 ProjectOnPlane(Fixed32Vec3 vector, Fixed32Vec3 planeNormal)
    {
        return vector - Project(vector, planeNormal);
    }

    // ============================================================
    // Angle Functions
    // ============================================================

    /// <summary>
    /// Returns the angle in radians between two vectors.
    /// </summary>
    public static Fixed32 Angle(Fixed32Vec3 from, Fixed32Vec3 to)
    {
        Fixed32 denominator = Fixed32.Sqrt(from.LengthSquared() * to.LengthSquared());
        if (denominator.Raw == 0)
        {
            return Fixed32.Zero;
        }

        Fixed32 dot = Fixed32.Clamp(Dot(from, to) / denominator, -Fixed32.OneValue, Fixed32.OneValue);
        return Fixed32.Acos(dot);
    }

    /// <summary>
    /// Returns the signed angle in radians between two vectors around an axis.
    /// </summary>
    public static Fixed32 SignedAngle(Fixed32Vec3 from, Fixed32Vec3 to, Fixed32Vec3 axis)
    {
        Fixed32 unsignedAngle = Angle(from, to);
        Fixed32Vec3 cross = Cross(from, to);
        Fixed32 sign = Fixed32.Sign(Dot(axis, cross));
        return unsignedAngle * sign;
    }

    // ============================================================
    // Rotation Functions
    // ============================================================

    /// <summary>
    /// Rotates a vector around an axis by the given angle in radians.
    /// </summary>
    public static Fixed32Vec3 RotateAround(Fixed32Vec3 point, Fixed32Vec3 axis, Fixed32 angle)
    {
        Fixed32.SinCosLUT(angle, out Fixed32 sin, out Fixed32 cos);
        Fixed32Vec3 u = axis.Normalized();

        // Rodrigues' rotation formula
        return point * cos + Cross(u, point) * sin + u * Dot(u, point) * (Fixed32.OneValue - cos);
    }

    // ============================================================
    // Swizzle Properties
    // ============================================================

    public Fixed32Vec2 XY => new Fixed32Vec2(X, Y);
    public Fixed32Vec2 XZ => new Fixed32Vec2(X, Z);
    public Fixed32Vec2 YZ => new Fixed32Vec2(Y, Z);

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed32Vec3 other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32Vec3 other && Equals(other);
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
