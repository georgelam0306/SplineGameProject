using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 4D vector using Fixed64 components for deterministic math and data serialization.
/// </summary>
public readonly struct Fixed64Vec4 : IEquatable<Fixed64Vec4>
{
    public readonly Fixed64 X;
    public readonly Fixed64 Y;
    public readonly Fixed64 Z;
    public readonly Fixed64 W;

    public static readonly Fixed64Vec4 Zero = new(Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
    public static readonly Fixed64Vec4 One = new(Fixed64.OneValue, Fixed64.OneValue, Fixed64.OneValue, Fixed64.OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec4(Fixed64 x, Fixed64 y, Fixed64 z, Fixed64 w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec4 FromInt(int x, int y, int z, int w)
    {
        return new Fixed64Vec4(
            Fixed64.FromInt(x),
            Fixed64.FromInt(y),
            Fixed64.FromInt(z),
            Fixed64.FromInt(w));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec4 FromFloat(float x, float y, float z, float w)
    {
        return new Fixed64Vec4(
            Fixed64.FromFloat(x),
            Fixed64.FromFloat(y),
            Fixed64.FromFloat(z),
            Fixed64.FromFloat(w));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64Vec4 left, Fixed64Vec4 right)
    {
        return left.X == right.X &&
               left.Y == right.Y &&
               left.Z == right.Z &&
               left.W == right.W;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64Vec4 left, Fixed64Vec4 right)
    {
        return left.X != right.X ||
               left.Y != right.Y ||
               left.Z != right.Z ||
               left.W != right.W;
    }

    public bool Equals(Fixed64Vec4 other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64Vec4 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Z, W);
    }

    public override string ToString()
    {
        return $"({X}, {Y}, {Z}, {W})";
    }
}

