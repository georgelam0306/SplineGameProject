using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 4D vector using Fixed32 components for deterministic math and data serialization.
/// </summary>
public readonly struct Fixed32Vec4 : IEquatable<Fixed32Vec4>
{
    public readonly Fixed32 X;
    public readonly Fixed32 Y;
    public readonly Fixed32 Z;
    public readonly Fixed32 W;

    public static readonly Fixed32Vec4 Zero = new(Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero);
    public static readonly Fixed32Vec4 One = new(Fixed32.OneValue, Fixed32.OneValue, Fixed32.OneValue, Fixed32.OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec4(Fixed32 x, Fixed32 y, Fixed32 z, Fixed32 w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec4 FromInt(int x, int y, int z, int w)
    {
        return new Fixed32Vec4(
            Fixed32.FromInt(x),
            Fixed32.FromInt(y),
            Fixed32.FromInt(z),
            Fixed32.FromInt(w));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec4 FromFloat(float x, float y, float z, float w)
    {
        return new Fixed32Vec4(
            Fixed32.FromFloat(x),
            Fixed32.FromFloat(y),
            Fixed32.FromFloat(z),
            Fixed32.FromFloat(w));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32Vec4 left, Fixed32Vec4 right)
    {
        return left.X == right.X &&
               left.Y == right.Y &&
               left.Z == right.Z &&
               left.W == right.W;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32Vec4 left, Fixed32Vec4 right)
    {
        return left.X != right.X ||
               left.Y != right.Y ||
               left.Z != right.Z ||
               left.W != right.W;
    }

    public bool Equals(Fixed32Vec4 other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32Vec4 other && Equals(other);
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

