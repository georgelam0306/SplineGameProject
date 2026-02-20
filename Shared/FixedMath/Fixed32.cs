using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

public readonly struct Fixed32 : IEquatable<Fixed32>, IComparable<Fixed32>
{
    private readonly int _raw;

    public const int FractionalBits = 16;
    public const int One = 1 << FractionalBits;
    public const int Half = One >> 1;

    public static readonly Fixed32 Zero = new(0);
    public static readonly Fixed32 OneValue = new(One);
    public static readonly Fixed32 MinValue = new(int.MinValue);
    public static readonly Fixed32 MaxValue = new(int.MaxValue);

    public int Raw => _raw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed32(int raw)
    {
        _raw = raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 FromRaw(int raw)
    {
        return new Fixed32(raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 FromInt(int value)
    {
        return new Fixed32(value << FractionalBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 FromFloat(float value)
    {
        return new Fixed32((int)(value * One));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 FromDouble(double value)
    {
        return new Fixed32((int)(value * One));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ToFloat()
    {
        return _raw / (float)One;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble()
    {
        return _raw / (double)One;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToInt()
    {
        return _raw >> FractionalBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator +(Fixed32 a, Fixed32 b)
    {
        int rawA = a._raw;
        int rawB = b._raw;
        int result = rawA + rawB;

        if (((rawA ^ result) & (rawB ^ result)) < 0)
        {
            return rawA > 0 ? MaxValue : MinValue;
        }

        return new Fixed32(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator -(Fixed32 a, Fixed32 b)
    {
        int rawA = a._raw;
        int rawB = b._raw;
        int result = rawA - rawB;

        if (((rawA ^ rawB) & (rawA ^ result)) < 0)
        {
            return rawA > 0 ? MaxValue : MinValue;
        }

        return new Fixed32(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator -(Fixed32 a)
    {
        return new Fixed32(-a._raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator *(Fixed32 a, Fixed32 b)
    {
        int rawA = a._raw;
        int rawB = b._raw;

        if (rawA == 0 || rawB == 0)
        {
            return Zero;
        }

        long product = (long)rawA * rawB;
        return new Fixed32((int)(product >> FractionalBits));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator *(Fixed32 a, int b)
    {
        if (a._raw == 0 || b == 0)
        {
            return Zero;
        }

        long product = (long)a._raw * b;
        if (product > int.MaxValue)
        {
            return MaxValue;
        }
        if (product < int.MinValue)
        {
            return MinValue;
        }

        return new Fixed32((int)product);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator *(int a, Fixed32 b)
    {
        return b * a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator /(Fixed32 a, Fixed32 b)
    {
        if (b._raw == 0)
        {
            return Zero;
        }

        long dividend = (long)a._raw << FractionalBits;
        return new Fixed32((int)(dividend / b._raw));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator /(Fixed32 a, int b)
    {
        if (b == 0)
        {
            return Zero;
        }
        return new Fixed32(a._raw / b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator %(Fixed32 a, Fixed32 b)
    {
        if (b._raw == 0)
        {
            return Zero;
        }
        return new Fixed32(a._raw % b._raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator >>(Fixed32 a, int shift)
    {
        return new Fixed32(a._raw >> shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 operator <<(Fixed32 a, int shift)
    {
        return new Fixed32(a._raw << shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32 a, Fixed32 b)
    {
        return a._raw == b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32 a, Fixed32 b)
    {
        return a._raw != b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(Fixed32 a, Fixed32 b)
    {
        return a._raw < b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(Fixed32 a, Fixed32 b)
    {
        return a._raw > b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(Fixed32 a, Fixed32 b)
    {
        return a._raw <= b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(Fixed32 a, Fixed32 b)
    {
        return a._raw >= b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Abs(Fixed32 value)
    {
        int mask = value._raw >> 31;
        return new Fixed32((value._raw + mask) ^ mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Floor(Fixed32 value)
    {
        return new Fixed32(value._raw & ~(One - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Ceiling(Fixed32 value)
    {
        int fractionalPart = value._raw & (One - 1);
        if (fractionalPart == 0)
        {
            return value;
        }
        return new Fixed32((value._raw & ~(One - 1)) + One);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Round(Fixed32 value)
    {
        return new Fixed32((value._raw + Half) & ~(One - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Min(Fixed32 a, Fixed32 b)
    {
        return a._raw < b._raw ? a : b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Max(Fixed32 a, Fixed32 b)
    {
        return a._raw > b._raw ? a : b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Clamp(Fixed32 value, Fixed32 min, Fixed32 max)
    {
        if (value._raw < min._raw)
        {
            return min;
        }
        if (value._raw > max._raw)
        {
            return max;
        }
        return value;
    }

    public static Fixed32 Sqrt(Fixed32 value)
    {
        if (value._raw <= 0)
        {
            return Zero;
        }

        long num = (long)value._raw << FractionalBits;
        long result = 0;
        long bit = 1L << 30;

        while (bit > num)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (num >= result + bit)
            {
                num -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }

        return new Fixed32((int)result);
    }

    public static readonly Fixed32 Pi = FromRaw(205887);
    public static readonly Fixed32 TwoPi = FromRaw(411775);
    public static readonly Fixed32 HalfPi = FromRaw(102944);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 SinLUT(Fixed32 angle)
    {
        return FixedMathLUT.Sin32(angle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 CosLUT(Fixed32 angle)
    {
        return FixedMathLUT.Cos32(angle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SinCosLUT(Fixed32 angle, out Fixed32 sin, out Fixed32 cos)
    {
        FixedMathLUT.SinCos32(angle, out sin, out cos);
    }

    // ============================================================
    // Fast unchecked arithmetic - use when overflow is impossible
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 AddFast(Fixed32 a, Fixed32 b) => new Fixed32(a._raw + b._raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 SubFast(Fixed32 a, Fixed32 b) => new Fixed32(a._raw - b._raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 MulFast(Fixed32 a, Fixed32 b) => new Fixed32((int)((long)a._raw * b._raw >> FractionalBits));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 MulFast(Fixed32 a, int b) => new Fixed32(a._raw * b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 DivFast(Fixed32 a, Fixed32 b) => new Fixed32((int)(((long)a._raw << FractionalBits) / b._raw));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 DivFast(Fixed32 a, int b) => new Fixed32(a._raw / b);

    // ============================================================
    // Additional Math Functions
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Sign(Fixed32 value)
    {
        if (value._raw > 0) return OneValue;
        if (value._raw < 0) return -OneValue;
        return Zero;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Lerp(Fixed32 a, Fixed32 b, Fixed32 t) => a + (b - a) * t;

    public static Fixed32 Sin(Fixed32 angle)
    {
        int rawAngle = angle._raw;
        int twoPiRaw = TwoPi._raw;
        int piRaw = Pi._raw;
        int halfPiRaw = HalfPi._raw;

        rawAngle = rawAngle % twoPiRaw;
        if (rawAngle < 0) rawAngle += twoPiRaw;

        bool negate = false;
        if (rawAngle > piRaw) { rawAngle -= piRaw; negate = true; }
        if (rawAngle > halfPiRaw) rawAngle = piRaw - rawAngle;

        Fixed32 normalizedAngle = FromRaw(rawAngle);
        Fixed32 angleSq = normalizedAngle * normalizedAngle;
        Fixed32 result = normalizedAngle;
        Fixed32 term = normalizedAngle * angleSq / FromInt(6);
        result = result - term;
        term = term * angleSq / FromInt(20);
        result = result + term;
        term = term * angleSq / FromInt(42);
        result = result - term;

        return negate ? -result : result;
    }

    public static Fixed32 Cos(Fixed32 angle) => Sin(angle + HalfPi);

    public static Fixed32 Tan(Fixed32 angle)
    {
        SinCosLUT(angle, out Fixed32 sin, out Fixed32 cos);
        if (cos._raw == 0) return sin._raw >= 0 ? MaxValue : MinValue;
        return sin / cos;
    }

    public static Fixed32 Atan2(Fixed32 y, Fixed32 x)
    {
        if (x._raw == 0 && y._raw == 0) return Zero;
        if (x._raw == 0) return y._raw > 0 ? HalfPi : -HalfPi;
        if (y._raw == 0) return x._raw > 0 ? Zero : Pi;

        Fixed32 absX = Abs(x);
        Fixed32 absY = Abs(y);
        bool swapped = absY > absX;
        Fixed32 ratio = swapped ? absX / absY : absY / absX;

        Fixed32 ratioSq = ratio * ratio;
        Fixed32 result = ratio;
        Fixed32 term = ratio * ratioSq;
        result = result - term / FromInt(3);
        term = term * ratioSq;
        result = result + term / FromInt(5);
        term = term * ratioSq;
        result = result - term / FromInt(7);

        if (swapped) result = HalfPi - result;
        if (x._raw < 0) result = Pi - result;
        if (y._raw < 0) result = -result;

        return result;
    }

    public static Fixed32 Asin(Fixed32 x)
    {
        if (x._raw >= One) return HalfPi;
        if (x._raw <= -One) return -HalfPi;
        if (x._raw == 0) return Zero;
        return Atan2(x, Sqrt(OneValue - x * x));
    }

    public static Fixed32 Acos(Fixed32 x)
    {
        if (x._raw >= One) return Zero;
        if (x._raw <= -One) return Pi;
        return HalfPi - Asin(x);
    }

    public static Fixed32 Atan(Fixed32 x) => Atan2(x, OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 NormalizeAngle(Fixed32 angle)
    {
        int raw = angle._raw % TwoPi._raw;
        if (raw > Pi._raw) raw -= TwoPi._raw;
        else if (raw < -Pi._raw) raw += TwoPi._raw;
        return FromRaw(raw);
    }

    // ============================================================
    // Power and Logarithmic Functions
    // ============================================================

    public static Fixed32 Pow(Fixed32 x, int exponent)
    {
        if (exponent == 0) return OneValue;
        if (exponent == 1) return x;
        if (x._raw == 0) return Zero;

        bool negative = exponent < 0;
        int exp = negative ? -exponent : exponent;
        Fixed32 result = OneValue;
        Fixed32 @base = x;

        while (exp > 0)
        {
            if ((exp & 1) != 0) result = result * @base;
            @base = @base * @base;
            exp >>= 1;
        }

        return negative ? OneValue / result : result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Pow2(Fixed32 x) => x * x;

    public static Fixed32 Log2(Fixed32 x)
    {
        if (x._raw <= 0) return Zero;

        int raw = x._raw;
        int intPart = 0;

        while (raw >= (One << 1)) { raw >>= 1; intPart++; }
        while (raw < One) { raw <<= 1; intPart--; }

        Fixed32 f = FromRaw(raw - One);
        Fixed32 log2e = FromRaw(94548);
        Fixed32 half = FromRaw(Half);
        Fixed32 fracPart = f * log2e / (OneValue + half * f);

        return FromInt(intPart) + fracPart;
    }

    public static Fixed32 Ln(Fixed32 x)
    {
        Fixed32 ln2 = FromRaw(45426);
        return Log2(x) * ln2;
    }

    public static Fixed32 Exp(Fixed32 x)
    {
        if (x._raw == 0) return OneValue;

        Fixed32 maxExp = FromInt(10);
        Fixed32 minExp = FromInt(-10);
        if (x > maxExp) x = maxExp;
        if (x < minExp) x = minExp;

        Fixed32 log2e = FromRaw(94548);
        Fixed32 xLog2 = x * log2e;
        int k = xLog2.ToInt();
        Fixed32 f = xLog2 - FromInt(k);

        if (f > FromRaw(Half)) { f = f - OneValue; k++; }
        else if (f < FromRaw(-Half)) { f = f + OneValue; k--; }

        Fixed32 ln2 = FromRaw(45426);
        Fixed32 fln2 = f * ln2;
        Fixed32 result = OneValue + fln2 + fln2 * fln2 / 2 + fln2 * fln2 * fln2 / 6;

        return k >= 0 ? result << k : result >> (-k);
    }

    // ============================================================
    // Interpolation Functions
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 SmoothStep(Fixed32 edge0, Fixed32 edge1, Fixed32 x)
    {
        Fixed32 t = Clamp((x - edge0) / (edge1 - edge0), Zero, OneValue);
        return t * t * (FromInt(3) - FromInt(2) * t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 CubicInterpolate(Fixed32 v0, Fixed32 v1, Fixed32 v2, Fixed32 v3, Fixed32 t)
    {
        Fixed32 t2 = t * t;
        Fixed32 t3 = t2 * t;
        Fixed32 a0 = v3 - v2 - v0 + v1;
        Fixed32 a1 = v0 - v1 - a0;
        Fixed32 a2 = v2 - v0;
        return a0 * t3 + a1 * t2 + a2 * t + v1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 MoveTowards(Fixed32 current, Fixed32 target, Fixed32 maxDelta)
    {
        Fixed32 diff = target - current;
        if (Abs(diff) <= maxDelta) return target;
        return current + Sign(diff) * maxDelta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 InverseLerp(Fixed32 a, Fixed32 b, Fixed32 value)
    {
        if (a._raw == b._raw) return Zero;
        return (value - a) / (b - a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Remap(Fixed32 value, Fixed32 fromMin, Fixed32 fromMax, Fixed32 toMin, Fixed32 toMax)
    {
        return Lerp(toMin, toMax, InverseLerp(fromMin, fromMax, value));
    }

    // ============================================================
    // Angle Conversion Utilities
    // ============================================================

    public static readonly Fixed32 Deg2Rad = FromRaw(1144);
    public static readonly Fixed32 Rad2Deg = FromRaw(3754936);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 DegreesToRadians(Fixed32 degrees) => degrees * Deg2Rad;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 RadiansToDegrees(Fixed32 radians) => radians * Rad2Deg;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 DeltaAngle(Fixed32 current, Fixed32 target) => NormalizeAngle(target - current);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 LerpAngle(Fixed32 a, Fixed32 b, Fixed32 t) => NormalizeAngle(a + DeltaAngle(a, b) * t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 MoveTowardsAngle(Fixed32 current, Fixed32 target, Fixed32 maxDelta)
    {
        Fixed32 delta = DeltaAngle(current, target);
        if (Abs(delta) <= maxDelta) return target;
        return NormalizeAngle(current + Sign(delta) * maxDelta);
    }

    // ============================================================
    // Distance Functions
    // ============================================================

    public static Fixed32 DistanceSquared(Fixed32 x1, Fixed32 y1, Fixed32 x2, Fixed32 y2)
    {
        Fixed32 dx = x2 - x1;
        Fixed32 dy = y2 - y1;
        return dx * dx + dy * dy;
    }

    public static Fixed32 Distance(Fixed32 x1, Fixed32 y1, Fixed32 x2, Fixed32 y2) => Sqrt(DistanceSquared(x1, y1, x2, y2));

    public static (Fixed32 x, Fixed32 y) Normalize(Fixed32 x, Fixed32 y)
    {
        Fixed32 lenSq = x * x + y * y;
        if (lenSq._raw == 0) return (Zero, Zero);
        Fixed32 len = Sqrt(lenSq);
        if (len._raw == 0) return (Zero, Zero);
        return (x / len, y / len);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 FromFixed64(Fixed64 value)
    {
        return new Fixed32((int)value.Raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 ToFixed64()
    {
        return Fixed64.FromRaw(_raw);
    }

    public bool Equals(Fixed32 other)
    {
        return _raw == other._raw;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _raw.GetHashCode();
    }

    public int CompareTo(Fixed32 other)
    {
        return _raw.CompareTo(other._raw);
    }

    public override string ToString()
    {
        return ToDouble().ToString("F4");
    }

    public static implicit operator Fixed32(int value)
    {
        return FromInt(value);
    }

    public static explicit operator int(Fixed32 value)
    {
        return value.ToInt();
    }

    public static explicit operator float(Fixed32 value)
    {
        return value.ToFloat();
    }

    public static explicit operator double(Fixed32 value)
    {
        return value.ToDouble();
    }
}

