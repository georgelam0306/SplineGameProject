using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

public static class FixedMathLUT
{
    public const int TableSize = 1024;
    private const int QuarterSize = TableSize / 4;
    private const double TwoPiDouble = 2.0 * Math.PI;
    private const double HalfPiDouble = Math.PI / 2.0;

    private static readonly Fixed64[] SinTable64 = new Fixed64[QuarterSize + 1];
    private static readonly Fixed32[] SinTable32 = new Fixed32[QuarterSize + 1];

    private static readonly long TwoPiRaw64 = Fixed64.TwoPi.Raw;
    private static readonly long PiRaw64 = Fixed64.Pi.Raw;
    private static readonly long HalfPiRaw64 = Fixed64.HalfPi.Raw;

    private static readonly int TwoPiRaw32 = Fixed32.TwoPi.Raw;
    private static readonly int PiRaw32 = Fixed32.Pi.Raw;
    private static readonly int HalfPiRaw32 = Fixed32.HalfPi.Raw;

    static FixedMathLUT()
    {
        for (int tableIndex = 0; tableIndex <= QuarterSize; tableIndex++)
        {
            double angle = (tableIndex / (double)TableSize) * TwoPiDouble;
            double sinValue = Math.Sin(angle);

            SinTable64[tableIndex] = Fixed64.FromDouble(sinValue);
            SinTable32[tableIndex] = Fixed32.FromDouble(sinValue);
        }
    }

    /// <summary>
    /// Internal helper to normalize angle and compute table index with interpolation factor.
    /// Returns the table index and sets negate flag and interpolation fraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NormalizeAndIndex64(long rawAngle, out bool negate, out long fraction)
    {
        // Normalize to [0, 2π)
        rawAngle = rawAngle % TwoPiRaw64;
        if (rawAngle < 0)
        {
            rawAngle += TwoPiRaw64;
        }

        // Reduce to [0, π) with negate flag
        negate = false;
        if (rawAngle >= PiRaw64)
        {
            rawAngle -= PiRaw64;
            negate = true;
        }

        // Reduce to [0, π/2] using symmetry
        if (rawAngle > HalfPiRaw64)
        {
            rawAngle = PiRaw64 - rawAngle;
        }

        // Compute table index and interpolation fraction
        long scaled = rawAngle * QuarterSize;
        int tableIndex = (int)(scaled / HalfPiRaw64);
        fraction = scaled % HalfPiRaw64; // Remainder for interpolation

        if (tableIndex >= QuarterSize)
        {
            tableIndex = QuarterSize;
            fraction = 0;
        }

        return tableIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Sin64(Fixed64 angle)
    {
        int tableIndex = NormalizeAndIndex64(angle.Raw, out bool negate, out long fraction);

        // Linear interpolation between table entries
        Fixed64 v0 = SinTable64[tableIndex];
        if (fraction != 0 && tableIndex < QuarterSize)
        {
            Fixed64 v1 = SinTable64[tableIndex + 1];
            // fraction / HalfPiRaw64 gives interpolation t in [0, 1)
            long diff = v1.Raw - v0.Raw;
            long interpolated = v0.Raw + (diff * fraction) / HalfPiRaw64;
            v0 = Fixed64.FromRaw(interpolated);
        }

        return negate ? -v0 : v0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Cos64(Fixed64 angle)
    {
        return Sin64(Fixed64.FromRaw(angle.Raw + HalfPiRaw64));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SinCos64(Fixed64 angle, out Fixed64 sin, out Fixed64 cos)
    {
        long rawAngle = angle.Raw;

        // Compute sin
        int sinIndex = NormalizeAndIndex64(rawAngle, out bool sinNegate, out long sinFraction);
        Fixed64 sinVal = SinTable64[sinIndex];
        if (sinFraction != 0 && sinIndex < QuarterSize)
        {
            Fixed64 v1 = SinTable64[sinIndex + 1];
            long diff = v1.Raw - sinVal.Raw;
            sinVal = Fixed64.FromRaw(sinVal.Raw + (diff * sinFraction) / HalfPiRaw64);
        }
        sin = sinNegate ? -sinVal : sinVal;

        // Compute cos using phase offset (shares normalized base angle concept)
        int cosIndex = NormalizeAndIndex64(rawAngle + HalfPiRaw64, out bool cosNegate, out long cosFraction);
        Fixed64 cosVal = SinTable64[cosIndex];
        if (cosFraction != 0 && cosIndex < QuarterSize)
        {
            Fixed64 v1 = SinTable64[cosIndex + 1];
            long diff = v1.Raw - cosVal.Raw;
            cosVal = Fixed64.FromRaw(cosVal.Raw + (diff * cosFraction) / HalfPiRaw64);
        }
        cos = cosNegate ? -cosVal : cosVal;
    }

    /// <summary>
    /// Internal helper to normalize angle and compute table index with interpolation factor for Fixed32.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NormalizeAndIndex32(int rawAngle, out bool negate, out int fraction)
    {
        // Normalize to [0, 2π)
        rawAngle = rawAngle % TwoPiRaw32;
        if (rawAngle < 0)
        {
            rawAngle += TwoPiRaw32;
        }

        // Reduce to [0, π) with negate flag
        negate = false;
        if (rawAngle >= PiRaw32)
        {
            rawAngle -= PiRaw32;
            negate = true;
        }

        // Reduce to [0, π/2] using symmetry
        if (rawAngle > HalfPiRaw32)
        {
            rawAngle = PiRaw32 - rawAngle;
        }

        // Compute table index and interpolation fraction
        int scaled = rawAngle * QuarterSize;
        int tableIndex = scaled / HalfPiRaw32;
        fraction = scaled % HalfPiRaw32;

        if (tableIndex >= QuarterSize)
        {
            tableIndex = QuarterSize;
            fraction = 0;
        }

        return tableIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Sin32(Fixed32 angle)
    {
        int tableIndex = NormalizeAndIndex32(angle.Raw, out bool negate, out int fraction);

        // Linear interpolation between table entries
        Fixed32 v0 = SinTable32[tableIndex];
        if (fraction != 0 && tableIndex < QuarterSize)
        {
            Fixed32 v1 = SinTable32[tableIndex + 1];
            int diff = v1.Raw - v0.Raw;
            int interpolated = v0.Raw + (diff * fraction) / HalfPiRaw32;
            v0 = Fixed32.FromRaw(interpolated);
        }

        return negate ? -v0 : v0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Cos32(Fixed32 angle)
    {
        return Sin32(Fixed32.FromRaw(angle.Raw + HalfPiRaw32));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SinCos32(Fixed32 angle, out Fixed32 sin, out Fixed32 cos)
    {
        int rawAngle = angle.Raw;

        // Compute sin
        int sinIndex = NormalizeAndIndex32(rawAngle, out bool sinNegate, out int sinFraction);
        Fixed32 sinVal = SinTable32[sinIndex];
        if (sinFraction != 0 && sinIndex < QuarterSize)
        {
            Fixed32 v1 = SinTable32[sinIndex + 1];
            int diff = v1.Raw - sinVal.Raw;
            sinVal = Fixed32.FromRaw(sinVal.Raw + (diff * sinFraction) / HalfPiRaw32);
        }
        sin = sinNegate ? -sinVal : sinVal;

        // Compute cos using phase offset
        int cosIndex = NormalizeAndIndex32(rawAngle + HalfPiRaw32, out bool cosNegate, out int cosFraction);
        Fixed32 cosVal = SinTable32[cosIndex];
        if (cosFraction != 0 && cosIndex < QuarterSize)
        {
            Fixed32 v1 = SinTable32[cosIndex + 1];
            int diff = v1.Raw - cosVal.Raw;
            cosVal = Fixed32.FromRaw(cosVal.Raw + (diff * cosFraction) / HalfPiRaw32);
        }
        cos = cosNegate ? -cosVal : cosVal;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 RotateVec64(Fixed64Vec2 point, Fixed64 angle)
    {
        SinCos64(angle, out Fixed64 sin, out Fixed64 cos);
        return Fixed64Vec2.Rotate(point, cos, sin);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 RotateVec32(Fixed32Vec2 point, Fixed32 angle)
    {
        SinCos32(angle, out Fixed32 sin, out Fixed32 cos);
        return Fixed32Vec2.Rotate(point, cos, sin);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 DirectionFromAngle64(Fixed64 angle)
    {
        SinCos64(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Vec2(cos, sin);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec2 DirectionFromAngle32(Fixed32 angle)
    {
        SinCos32(angle, out Fixed32 sin, out Fixed32 cos);
        return new Fixed32Vec2(cos, sin);
    }
}

