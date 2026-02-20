using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

public readonly struct Fixed32Vec2 : IEquatable<Fixed32Vec2>
{
    public readonly Fixed32 X;
    public readonly Fixed32 Y;

    public static readonly Fixed32Vec2 Zero = new(Fixed32.Zero, Fixed32.Zero);
    public static readonly Fixed32Vec2 One = new(Fixed32.OneValue, Fixed32.OneValue);
    public static readonly Fixed32Vec2 UnitX = new(Fixed32.OneValue, Fixed32.Zero);
    public static readonly Fixed32Vec2 UnitY = new(Fixed32.Zero, Fixed32.OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec2(Fixed32 x, Fixed32 y)
    {
        X = x;
        Y = y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 FromInt(int x, int y)
    {
        return new Fixed32Vec2(Fixed32.FromInt(x), Fixed32.FromInt(y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 FromFloat(float x, float y)
    {
        return new Fixed32Vec2(Fixed32.FromFloat(x), Fixed32.FromFloat(y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator +(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return new Fixed32Vec2(a.X + b.X, a.Y + b.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator -(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return new Fixed32Vec2(a.X - b.X, a.Y - b.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator -(Fixed32Vec2 a)
    {
        return new Fixed32Vec2(-a.X, -a.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator *(Fixed32Vec2 a, Fixed32 scalar)
    {
        return new Fixed32Vec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator *(Fixed32 scalar, Fixed32Vec2 a)
    {
        return new Fixed32Vec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator *(Fixed32Vec2 a, int scalar)
    {
        return new Fixed32Vec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator *(int scalar, Fixed32Vec2 a)
    {
        return new Fixed32Vec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator /(Fixed32Vec2 a, Fixed32 scalar)
    {
        return new Fixed32Vec2(a.X / scalar, a.Y / scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 operator /(Fixed32Vec2 a, int scalar)
    {
        return new Fixed32Vec2(a.X / scalar, a.Y / scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return a.X == b.X && a.Y == b.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return a.X != b.X || a.Y != b.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Dot(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return a.X * b.X + a.Y * b.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Cross(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 LengthSquared()
    {
        return X * X + Y * Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Length()
    {
        return Fixed32.Sqrt(LengthSquared());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec2 Normalized()
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

        return new Fixed32Vec2(X / length, Y / length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Distance(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return (b - a).Length();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 DistanceSquared(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return (b - a).LengthSquared();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 Lerp(Fixed32Vec2 a, Fixed32Vec2 b, Fixed32 t)
    {
        return new Fixed32Vec2(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 Min(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return new Fixed32Vec2(
            Fixed32.Min(a.X, b.X),
            Fixed32.Min(a.Y, b.Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 Max(Fixed32Vec2 a, Fixed32Vec2 b)
    {
        return new Fixed32Vec2(
            Fixed32.Max(a.X, b.X),
            Fixed32.Max(a.Y, b.Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 Clamp(Fixed32Vec2 value, Fixed32Vec2 min, Fixed32Vec2 max)
    {
        return new Fixed32Vec2(
            Fixed32.Clamp(value.X, min.X, max.X),
            Fixed32.Clamp(value.Y, min.Y, max.Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 Abs(Fixed32Vec2 value)
    {
        return new Fixed32Vec2(Fixed32.Abs(value.X), Fixed32.Abs(value.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 Rotate(Fixed32Vec2 point, Fixed32 angleCos, Fixed32 angleSin)
    {
        return new Fixed32Vec2(
            point.X * angleCos - point.Y * angleSin,
            point.X * angleSin + point.Y * angleCos
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 RotateAround(Fixed32Vec2 point, Fixed32Vec2 pivot, Fixed32 angleCos, Fixed32 angleSin)
    {
        Fixed32Vec2 translated = point - pivot;
        Fixed32Vec2 rotated = Rotate(translated, angleCos, angleSin);
        return rotated + pivot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 Perpendicular(Fixed32Vec2 value)
    {
        return new Fixed32Vec2(-value.Y, value.X);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 Reflect(Fixed32Vec2 direction, Fixed32Vec2 normal)
    {
        Fixed32 dotProduct = Dot(direction, normal);
        return direction - normal * dotProduct * 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 FromFixed64Vec2(Fixed64Vec2 value)
    {
        return new Fixed32Vec2(
            Fixed32.FromFixed64(value.X),
            Fixed32.FromFixed64(value.Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec2 ToFixed64Vec2()
    {
        return new Fixed64Vec2(X.ToFixed64(), Y.ToFixed64());
    }

    public bool Equals(Fixed32Vec2 other)
    {
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32Vec2 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }

    public (float x, float y) ToFloat()
    {
        return (X.ToFloat(), Y.ToFloat());
    }
}

