using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

public readonly struct Fixed64Vec2 : IEquatable<Fixed64Vec2>
{
    public readonly Fixed64 X;
    public readonly Fixed64 Y;

    public static readonly Fixed64Vec2 Zero = new(Fixed64.Zero, Fixed64.Zero);
    public static readonly Fixed64Vec2 One = new(Fixed64.OneValue, Fixed64.OneValue);
    public static readonly Fixed64Vec2 UnitX = new(Fixed64.OneValue, Fixed64.Zero);
    public static readonly Fixed64Vec2 UnitY = new(Fixed64.Zero, Fixed64.OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec2(Fixed64 x, Fixed64 y)
    {
        X = x;
        Y = y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 FromInt(int x, int y)
    {
        return new Fixed64Vec2(Fixed64.FromInt(x), Fixed64.FromInt(y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 FromFloat(float x, float y)
    {
        return new Fixed64Vec2(Fixed64.FromFloat(x), Fixed64.FromFloat(y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator +(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return new Fixed64Vec2(a.X + b.X, a.Y + b.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator -(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return new Fixed64Vec2(a.X - b.X, a.Y - b.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator -(Fixed64Vec2 a)
    {
        return new Fixed64Vec2(-a.X, -a.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator *(Fixed64Vec2 a, Fixed64 scalar)
    {
        return new Fixed64Vec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator *(Fixed64 scalar, Fixed64Vec2 a)
    {
        return new Fixed64Vec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator *(Fixed64Vec2 a, int scalar)
    {
        return new Fixed64Vec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator *(int scalar, Fixed64Vec2 a)
    {
        return new Fixed64Vec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator /(Fixed64Vec2 a, Fixed64 scalar)
    {
        return new Fixed64Vec2(a.X / scalar, a.Y / scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 operator /(Fixed64Vec2 a, int scalar)
    {
        return new Fixed64Vec2(a.X / scalar, a.Y / scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return a.X == b.X && a.Y == b.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return a.X != b.X || a.Y != b.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Dot(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return a.X * b.X + a.Y * b.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Cross(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 LengthSquared()
    {
        return X * X + Y * Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Length()
    {
        return Fixed64.Sqrt(LengthSquared());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec2 Normalized()
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

        return new Fixed64Vec2(X / length, Y / length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Distance(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return (b - a).Length();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 DistanceSquared(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return (b - a).LengthSquared();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 Lerp(Fixed64Vec2 a, Fixed64Vec2 b, Fixed64 t)
    {
        return new Fixed64Vec2(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 Min(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return new Fixed64Vec2(
            Fixed64.Min(a.X, b.X),
            Fixed64.Min(a.Y, b.Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 Max(Fixed64Vec2 a, Fixed64Vec2 b)
    {
        return new Fixed64Vec2(
            Fixed64.Max(a.X, b.X),
            Fixed64.Max(a.Y, b.Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 Clamp(Fixed64Vec2 value, Fixed64Vec2 min, Fixed64Vec2 max)
    {
        return new Fixed64Vec2(
            Fixed64.Clamp(value.X, min.X, max.X),
            Fixed64.Clamp(value.Y, min.Y, max.Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 Abs(Fixed64Vec2 value)
    {
        return new Fixed64Vec2(Fixed64.Abs(value.X), Fixed64.Abs(value.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 Rotate(Fixed64Vec2 point, Fixed64 angleCos, Fixed64 angleSin)
    {
        return new Fixed64Vec2(
            point.X * angleCos - point.Y * angleSin,
            point.X * angleSin + point.Y * angleCos
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 RotateAround(Fixed64Vec2 point, Fixed64Vec2 pivot, Fixed64 angleCos, Fixed64 angleSin)
    {
        Fixed64Vec2 translated = point - pivot;
        Fixed64Vec2 rotated = Rotate(translated, angleCos, angleSin);
        return rotated + pivot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 Perpendicular(Fixed64Vec2 value)
    {
        return new Fixed64Vec2(-value.Y, value.X);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 Reflect(Fixed64Vec2 direction, Fixed64Vec2 normal)
    {
        Fixed64 dotProduct = Dot(direction, normal);
        return direction - normal * dotProduct * 2;
    }

    public bool Equals(Fixed64Vec2 other)
    {
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64Vec2 other && Equals(other);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public System.Numerics.Vector2 ToVector2()
    {
        return new System.Numerics.Vector2(X.ToFloat(), Y.ToFloat());
    }
}

