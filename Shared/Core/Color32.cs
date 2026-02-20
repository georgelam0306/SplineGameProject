using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Core;

/// <summary>
/// A 32-bit RGBA color suitable for serialization in game data.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Color32 : IEquatable<Color32>
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte A;

    public static readonly Color32 White = new(255, 255, 255, 255);
    public static readonly Color32 Black = new(0, 0, 0, 255);
    public static readonly Color32 Gray = new(128, 128, 128, 255);
    public static readonly Color32 Red = new(255, 0, 0, 255);
    public static readonly Color32 Green = new(0, 255, 0, 255);
    public static readonly Color32 Blue = new(0, 0, 255, 255);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color32(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Color32 a, Color32 b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Color32 a, Color32 b)
    {
        return a.R != b.R || a.G != b.G || a.B != b.B || a.A != b.A;
    }

    public bool Equals(Color32 other)
    {
        return R == other.R && G == other.G && B == other.B && A == other.A;
    }

    public override bool Equals(object? obj)
    {
        return obj is Color32 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(R, G, B, A);
    }

    public override string ToString()
    {
        return $"#{R:X2}{G:X2}{B:X2}{A:X2}";
    }
}

/// <summary>
/// JSON-compatible DTO for Color32 deserialization.
/// </summary>
public sealed class Color32Dto
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte A { get; set; }
}
