using System;
using System.Runtime.CompilerServices;

namespace Core;

public readonly struct IntVec2 : IEquatable<IntVec2>
{
    public readonly int X;
    public readonly int Y;

    public static readonly IntVec2 Zero = new(0, 0);
    public static readonly IntVec2 One = new(1, 1);
    public static readonly IntVec2 UnitX = new(1, 0);
    public static readonly IntVec2 UnitY = new(0, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IntVec2(int x, int y)
    {
        X = x;
        Y = y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 operator +(IntVec2 a, IntVec2 b)
    {
        return new IntVec2(a.X + b.X, a.Y + b.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 operator -(IntVec2 a, IntVec2 b)
    {
        return new IntVec2(a.X - b.X, a.Y - b.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 operator -(IntVec2 a)
    {
        return new IntVec2(-a.X, -a.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 operator *(IntVec2 a, int scalar)
    {
        return new IntVec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 operator *(int scalar, IntVec2 a)
    {
        return new IntVec2(a.X * scalar, a.Y * scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 operator /(IntVec2 a, int scalar)
    {
        return new IntVec2(a.X / scalar, a.Y / scalar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(IntVec2 a, IntVec2 b)
    {
        return a.X == b.X && a.Y == b.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(IntVec2 a, IntVec2 b)
    {
        return a.X != b.X || a.Y != b.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ManhattanDistance(IntVec2 other)
    {
        return Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ManhattanDistance(IntVec2 a, IntVec2 b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ChebyshevDistance(IntVec2 other)
    {
        return Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ChebyshevDistance(IntVec2 a, IntVec2 b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 Min(IntVec2 a, IntVec2 b)
    {
        return new IntVec2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 Max(IntVec2 a, IntVec2 b)
    {
        return new IntVec2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 Clamp(IntVec2 value, IntVec2 min, IntVec2 max)
    {
        return new IntVec2(
            Math.Clamp(value.X, min.X, max.X),
            Math.Clamp(value.Y, min.Y, max.Y)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntVec2 Abs(IntVec2 value)
    {
        return new IntVec2(Math.Abs(value.X), Math.Abs(value.Y));
    }

    public bool Equals(IntVec2 other)
    {
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object? obj)
    {
        return obj is IntVec2 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec2 ToFixed64Vec2()
    {
        return Fixed64Vec2.FromInt(X, Y);
    }
}
