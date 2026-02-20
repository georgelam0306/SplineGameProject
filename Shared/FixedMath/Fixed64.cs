using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

public readonly struct Fixed64 : IEquatable<Fixed64>, IComparable<Fixed64>
{
    private readonly long _raw;

    public const int FractionalBits = 16;
    public const long One = 1L << FractionalBits;
    public const long Half = One >> 1;

    public static readonly Fixed64 Zero = new(0);
    public static readonly Fixed64 OneValue = new(One);
    public static readonly Fixed64 MinValue = new(long.MinValue);
    public static readonly Fixed64 MaxValue = new(long.MaxValue);

    public long Raw => _raw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64(long raw)
    {
        _raw = raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 FromRaw(long raw)
    {
        return new Fixed64(raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 FromInt(int value)
    {
        return new Fixed64((long)value << FractionalBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 FromInt(long value)
    {
        return new Fixed64(value << FractionalBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 FromFloat(float value)
    {
        return new Fixed64((long)(value * One));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 FromDouble(double value)
    {
        return new Fixed64((long)(value * One));
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
        return (int)(_raw >> FractionalBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToLong()
    {
        return _raw >> FractionalBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator +(Fixed64 a, Fixed64 b)
    {
        long rawA = a._raw;
        long rawB = b._raw;
        long result = rawA + rawB;

        if (((rawA ^ result) & (rawB ^ result)) < 0)
        {
            return rawA > 0 ? MaxValue : MinValue;
        }

        return new Fixed64(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator -(Fixed64 a, Fixed64 b)
    {
        long rawA = a._raw;
        long rawB = b._raw;
        long result = rawA - rawB;

        if (((rawA ^ rawB) & (rawA ^ result)) < 0)
        {
            return rawA > 0 ? MaxValue : MinValue;
        }

        return new Fixed64(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator -(Fixed64 a)
    {
        return new Fixed64(-a._raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator *(Fixed64 a, Fixed64 b)
    {
        long rawA = a._raw;
        long rawB = b._raw;

        if (rawA == 0 || rawB == 0)
        {
            return Zero;
        }

        bool negative = (rawA < 0) != (rawB < 0);
        long absA = rawA == long.MinValue ? long.MaxValue : (rawA < 0 ? -rawA : rawA);
        long absB = rawB == long.MinValue ? long.MaxValue : (rawB < 0 ? -rawB : rawB);

        if (absA > long.MaxValue / absB)
        {
            return negative ? MinValue : MaxValue;
        }

        return new Fixed64((rawA * rawB) >> FractionalBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator *(Fixed64 a, int b)
    {
        if (a._raw == 0 || b == 0)
        {
            return Zero;
        }

        bool negative = (a._raw < 0) != (b < 0);
        long absA = a._raw == long.MinValue ? long.MaxValue : (a._raw < 0 ? -a._raw : a._raw);
        long absB = b == int.MinValue ? int.MaxValue : (b < 0 ? -b : b);

        if (absA > long.MaxValue / absB)
        {
            return negative ? MinValue : MaxValue;
        }

        return new Fixed64(a._raw * b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator *(int a, Fixed64 b)
    {
        if (a == 0 || b._raw == 0)
        {
            return Zero;
        }

        bool negative = (a < 0) != (b._raw < 0);
        long absA = a == int.MinValue ? int.MaxValue : (a < 0 ? -a : a);
        long absB = b._raw == long.MinValue ? long.MaxValue : (b._raw < 0 ? -b._raw : b._raw);

        if (absB > long.MaxValue / absA)
        {
            return negative ? MinValue : MaxValue;
        }

        return new Fixed64(a * b._raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator /(Fixed64 a, Fixed64 b)
    {
        if (b._raw == 0)
        {
            return Zero;
        }

        long maxSafeValue = long.MaxValue >> FractionalBits;
        if (a._raw > maxSafeValue || a._raw < -maxSafeValue)
        {
            long quotient = a._raw / b._raw;
            long remainder = a._raw % b._raw;

            if (quotient > maxSafeValue)
            {
                return (a._raw < 0) != (b._raw < 0) ? MinValue : MaxValue;
            }
            if (quotient < -maxSafeValue)
            {
                return (a._raw < 0) != (b._raw < 0) ? MinValue : MaxValue;
            }

            long fractionalPart = (remainder << FractionalBits) / b._raw;
            return new Fixed64((quotient << FractionalBits) + fractionalPart);
        }

        return new Fixed64((a._raw << FractionalBits) / b._raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator /(Fixed64 a, int b)
    {
        if (b == 0)
        {
            return Zero;
        }
        return new Fixed64(a._raw / b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator %(Fixed64 a, Fixed64 b)
    {
        if (b._raw == 0)
        {
            return Zero;
        }
        return new Fixed64(a._raw % b._raw);
    }

    // ============================================================
    // Fast unchecked arithmetic - use when overflow is impossible
    // ============================================================

    /// <summary>
    /// Fast addition without overflow checking. Use when values are bounded.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 AddFast(Fixed64 a, Fixed64 b)
    {
        return new Fixed64(a._raw + b._raw);
    }

    /// <summary>
    /// Fast subtraction without overflow checking. Use when values are bounded.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 SubFast(Fixed64 a, Fixed64 b)
    {
        return new Fixed64(a._raw - b._raw);
    }

    /// <summary>
    /// Fast multiplication without overflow checking. Use when product is known to fit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 MulFast(Fixed64 a, Fixed64 b)
    {
        return new Fixed64((a._raw * b._raw) >> FractionalBits);
    }

    /// <summary>
    /// Fast multiplication by integer without overflow checking.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 MulFast(Fixed64 a, int b)
    {
        return new Fixed64(a._raw * b);
    }

    /// <summary>
    /// Fast division without zero or overflow checking.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 DivFast(Fixed64 a, Fixed64 b)
    {
        return new Fixed64((a._raw << FractionalBits) / b._raw);
    }

    /// <summary>
    /// Fast division by integer without zero checking.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 DivFast(Fixed64 a, int b)
    {
        return new Fixed64(a._raw / b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator >>(Fixed64 a, int shift)
    {
        return new Fixed64(a._raw >> shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 operator <<(Fixed64 a, int shift)
    {
        return new Fixed64(a._raw << shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64 a, Fixed64 b)
    {
        return a._raw == b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64 a, Fixed64 b)
    {
        return a._raw != b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(Fixed64 a, Fixed64 b)
    {
        return a._raw < b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(Fixed64 a, Fixed64 b)
    {
        return a._raw > b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(Fixed64 a, Fixed64 b)
    {
        return a._raw <= b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(Fixed64 a, Fixed64 b)
    {
        return a._raw >= b._raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Abs(Fixed64 value)
    {
        long mask = value._raw >> 63;
        return new Fixed64((value._raw + mask) ^ mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Floor(Fixed64 value)
    {
        return new Fixed64(value._raw & ~(One - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Ceiling(Fixed64 value)
    {
        long fractionalPart = value._raw & (One - 1);
        if (fractionalPart == 0)
        {
            return value;
        }
        return new Fixed64((value._raw & ~(One - 1)) + One);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Round(Fixed64 value)
    {
        return new Fixed64((value._raw + Half) & ~(One - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Min(Fixed64 a, Fixed64 b)
    {
        return a._raw < b._raw ? a : b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Max(Fixed64 a, Fixed64 b)
    {
        return a._raw > b._raw ? a : b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Clamp(Fixed64 value, Fixed64 min, Fixed64 max)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Sqrt(Fixed64 value)
    {
        if (value._raw <= 0)
        {
            return Zero;
        }

        long maxSafeValue = long.MaxValue >> FractionalBits;
        if (value._raw >= maxSafeValue)
        {
            long sqrtRaw = IntegerSqrt(value._raw);
            return new Fixed64(sqrtRaw << (FractionalBits / 2));
        }

        long num = value._raw << FractionalBits;
        long result = 0;
        long bit = 1L << 62;

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

        return new Fixed64(result);
    }

    /// <summary>
    /// Computes 1/sqrt(value). Useful for normalization without separate sqrt and divide.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 InverseSqrt(Fixed64 value)
    {
        if (value._raw <= 0)
        {
            return Zero;
        }
        Fixed64 sqrt = Sqrt(value);
        if (sqrt._raw == 0)
        {
            return Zero;
        }
        return OneValue / sqrt;
    }

    private static long IntegerSqrt(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        long result = 0;
        long bit = 1L << 62;

        while (bit > value)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (value >= result + bit)
            {
                value -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }

        return result;
    }

    public static Fixed64 DistanceSquared(Fixed64 x1, Fixed64 y1, Fixed64 x2, Fixed64 y2)
    {
        Fixed64 deltaX = x2 - x1;
        Fixed64 deltaY = y2 - y1;
        return deltaX * deltaX + deltaY * deltaY;
    }

    public static Fixed64 Distance(Fixed64 x1, Fixed64 y1, Fixed64 x2, Fixed64 y2)
    {
        return Sqrt(DistanceSquared(x1, y1, x2, y2));
    }

    public static (Fixed64 x, Fixed64 y) Normalize(Fixed64 x, Fixed64 y)
    {
        Fixed64 lengthSquared = x * x + y * y;
        if (lengthSquared._raw == 0)
        {
            return (Zero, Zero);
        }

        Fixed64 length = Sqrt(lengthSquared);
        if (length._raw == 0)
        {
            return (Zero, Zero);
        }
        return (x / length, y / length);
    }

    public static readonly Fixed64 Pi = FromRaw(205887L);
    public static readonly Fixed64 TwoPi = FromRaw(411775L);
    public static readonly Fixed64 HalfPi = FromRaw(102944L);

    public static Fixed64 Sin(Fixed64 angle)
    {
        long rawAngle = angle._raw;
        long twoPiRaw = TwoPi._raw;
        long piRaw = Pi._raw;
        long halfPiRaw = HalfPi._raw;

        rawAngle = rawAngle % twoPiRaw;
        if (rawAngle < 0)
        {
            rawAngle += twoPiRaw;
        }

        bool negate = false;
        if (rawAngle > piRaw)
        {
            rawAngle -= piRaw;
            negate = true;
        }

        if (rawAngle > halfPiRaw)
        {
            rawAngle = piRaw - rawAngle;
        }

        Fixed64 normalizedAngle = FromRaw(rawAngle);
        Fixed64 angleSq = normalizedAngle * normalizedAngle;

        Fixed64 result = normalizedAngle;
        Fixed64 term = normalizedAngle * angleSq / FromInt(6);
        result = result - term;
        term = term * angleSq / FromInt(20);
        result = result + term;
        term = term * angleSq / FromInt(42);
        result = result - term;
        term = term * angleSq / FromInt(72);
        result = result + term;

        if (negate)
        {
            return -result;
        }
        return result;
    }

    public static Fixed64 Cos(Fixed64 angle)
    {
        return Sin(angle + HalfPi);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 SinLUT(Fixed64 angle)
    {
        return FixedMathLUT.Sin64(angle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 CosLUT(Fixed64 angle)
    {
        return FixedMathLUT.Cos64(angle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SinCosLUT(Fixed64 angle, out Fixed64 sin, out Fixed64 cos)
    {
        FixedMathLUT.SinCos64(angle, out sin, out cos);
    }

    /// <summary>
    /// Computes atan2(y, x) using polynomial approximation.
    /// Returns angle in radians from -Pi to Pi.
    /// </summary>
    public static Fixed64 Atan2(Fixed64 y, Fixed64 x)
    {
        // Handle zero cases
        if (x._raw == 0 && y._raw == 0) return Zero;

        // Handle cardinal directions for accuracy
        if (x._raw == 0)
        {
            return y._raw > 0 ? HalfPi : -HalfPi;
        }
        if (y._raw == 0)
        {
            return x._raw > 0 ? Zero : Pi;
        }

        Fixed64 absX = Abs(x);
        Fixed64 absY = Abs(y);

        // Use the smaller ratio for better accuracy
        bool swapped = absY > absX;
        Fixed64 ratio = swapped ? absX / absY : absY / absX;

        // Polynomial approximation for atan(x) where 0 <= x <= 1
        // atan(x) ≈ x - x³/3 + x⁵/5 - x⁷/7 (truncated Taylor series)
        Fixed64 ratioSq = ratio * ratio;
        Fixed64 result = ratio;
        Fixed64 term = ratio * ratioSq;
        result = result - term / FromInt(3);
        term = term * ratioSq;
        result = result + term / FromInt(5);
        term = term * ratioSq;
        result = result - term / FromInt(7);

        // Adjust for octant
        if (swapped)
        {
            result = HalfPi - result;
        }

        // Adjust for quadrant
        if (x._raw < 0)
        {
            result = Pi - result;
        }
        if (y._raw < 0)
        {
            result = -result;
        }

        return result;
    }

    /// <summary>
    /// Computes tan(angle) using sin/cos.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Tan(Fixed64 angle)
    {
        SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        if (cos._raw == 0) return sin._raw >= 0 ? MaxValue : MinValue;
        return sin / cos;
    }

    /// <summary>
    /// Normalize angle to range [-Pi, Pi].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 NormalizeAngle(Fixed64 angle)
    {
        long raw = angle._raw;
        long twoPiRaw = TwoPi._raw;
        long piRaw = Pi._raw;

        // Use modulo for efficiency
        raw = raw % twoPiRaw;

        // Adjust to [-Pi, Pi]
        if (raw > piRaw)
        {
            raw -= twoPiRaw;
        }
        else if (raw < -piRaw)
        {
            raw += twoPiRaw;
        }

        return FromRaw(raw);
    }

    /// <summary>
    /// Linear interpolation between two values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Lerp(Fixed64 a, Fixed64 b, Fixed64 t)
    {
        return a + (b - a) * t;
    }

    /// <summary>
    /// Compute the sign of a value: -1, 0, or 1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Sign(Fixed64 value)
    {
        if (value._raw > 0) return OneValue;
        if (value._raw < 0) return -OneValue;
        return Zero;
    }

    // ============================================================
    // Inverse Trigonometric Functions
    // ============================================================

    /// <summary>
    /// Computes arcsin(x). Input must be in range [-1, 1].
    /// Returns angle in radians from -Pi/2 to Pi/2.
    /// </summary>
    public static Fixed64 Asin(Fixed64 x)
    {
        // Clamp to valid range
        if (x._raw >= One) return HalfPi;
        if (x._raw <= -One) return -HalfPi;
        if (x._raw == 0) return Zero;

        // Use identity: asin(x) = atan2(x, sqrt(1 - x²))
        Fixed64 xSq = x * x;
        Fixed64 sqrtPart = Sqrt(OneValue - xSq);
        return Atan2(x, sqrtPart);
    }

    /// <summary>
    /// Computes arccos(x). Input must be in range [-1, 1].
    /// Returns angle in radians from 0 to Pi.
    /// </summary>
    public static Fixed64 Acos(Fixed64 x)
    {
        // Clamp to valid range
        if (x._raw >= One) return Zero;
        if (x._raw <= -One) return Pi;

        // Use identity: acos(x) = Pi/2 - asin(x)
        return HalfPi - Asin(x);
    }

    /// <summary>
    /// Computes arctan(x). Returns angle in radians from -Pi/2 to Pi/2.
    /// </summary>
    public static Fixed64 Atan(Fixed64 x)
    {
        return Atan2(x, OneValue);
    }

    // ============================================================
    // Power and Logarithmic Functions
    // ============================================================

    /// <summary>
    /// Computes x raised to the power of an integer exponent.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Pow(Fixed64 x, int exponent)
    {
        if (exponent == 0) return OneValue;
        if (exponent == 1) return x;
        if (x._raw == 0) return Zero;

        bool negative = exponent < 0;
        int exp = negative ? -exponent : exponent;

        // Fast exponentiation by squaring
        Fixed64 result = OneValue;
        Fixed64 @base = x;

        while (exp > 0)
        {
            if ((exp & 1) != 0)
            {
                result = result * @base;
            }
            @base = @base * @base;
            exp >>= 1;
        }

        return negative ? OneValue / result : result;
    }

    /// <summary>
    /// Computes x squared. Faster than Pow(x, 2).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Pow2(Fixed64 x)
    {
        return x * x;
    }

    /// <summary>
    /// Computes base-2 logarithm using iterative refinement.
    /// Returns 0 for non-positive values.
    /// </summary>
    public static Fixed64 Log2(Fixed64 x)
    {
        if (x._raw <= 0) return Zero;

        // Find integer part: how many times we can divide by 2
        long raw = x._raw;
        int intPart = 0;

        // Normalize to range [1, 2)
        while (raw >= (One << 1))
        {
            raw >>= 1;
            intPart++;
        }
        while (raw < One)
        {
            raw <<= 1;
            intPart--;
        }

        // Now raw is in [One, 2*One), compute fractional part
        // Using polynomial approximation for log2(1+f) where f is in [0, 1)
        Fixed64 f = FromRaw(raw - One); // f = x - 1, in [0, 1)

        // Polynomial: log2(1+f) ≈ f * (1.4427 - 0.7213*f + 0.4808*f² - ...)
        // Simplified: log2(1+f) ≈ f * 1.4427 / (1 + 0.5*f)
        Fixed64 log2e = FromRaw(94548); // ~1.4427
        Fixed64 half = FromRaw(Half);
        Fixed64 fracPart = f * log2e / (OneValue + half * f);

        return FromInt(intPart) + fracPart;
    }

    /// <summary>
    /// Computes natural logarithm (base e).
    /// Returns 0 for non-positive values.
    /// </summary>
    public static Fixed64 Ln(Fixed64 x)
    {
        // ln(x) = log2(x) / log2(e) = log2(x) * ln(2)
        Fixed64 ln2 = FromRaw(45426); // ~0.6931
        return Log2(x) * ln2;
    }

    /// <summary>
    /// Computes e^x using Taylor series.
    /// </summary>
    public static Fixed64 Exp(Fixed64 x)
    {
        if (x._raw == 0) return OneValue;

        // Clamp to prevent overflow
        Fixed64 maxExp = FromInt(10);
        Fixed64 minExp = FromInt(-10);
        if (x > maxExp) x = maxExp;
        if (x < minExp) x = minExp;

        // Use range reduction: e^x = 2^(x * log2(e)) = 2^k * 2^f
        Fixed64 log2e = FromRaw(94548); // ~1.4427
        Fixed64 xLog2 = x * log2e;

        int k = xLog2.ToInt();
        Fixed64 f = xLog2 - FromInt(k);

        // 2^f using polynomial for f in [-0.5, 0.5]
        // Shift f to be in [-0.5, 0.5]
        if (f > FromRaw(Half))
        {
            f = f - OneValue;
            k++;
        }
        else if (f < FromRaw(-Half))
        {
            f = f + OneValue;
            k--;
        }

        // 2^f ≈ 1 + f*ln(2) + (f*ln(2))²/2 + ...
        Fixed64 ln2 = FromRaw(45426);
        Fixed64 fln2 = f * ln2;
        Fixed64 result = OneValue + fln2 + fln2 * fln2 / 2 + fln2 * fln2 * fln2 / 6;

        // Apply 2^k
        if (k >= 0)
        {
            return result << k;
        }
        else
        {
            return result >> (-k);
        }
    }

    // ============================================================
    // Interpolation Functions
    // ============================================================

    /// <summary>
    /// Smooth hermite interpolation between 0 and 1 when edge0 < x < edge1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 SmoothStep(Fixed64 edge0, Fixed64 edge1, Fixed64 x)
    {
        // Clamp x to [0, 1] range
        Fixed64 t = Clamp((x - edge0) / (edge1 - edge0), Zero, OneValue);
        // Smooth curve: 3t² - 2t³
        return t * t * (FromInt(3) - FromInt(2) * t);
    }

    /// <summary>
    /// Cubic interpolation between four values.
    /// t should be in [0, 1] for interpolation between v1 and v2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 CubicInterpolate(Fixed64 v0, Fixed64 v1, Fixed64 v2, Fixed64 v3, Fixed64 t)
    {
        Fixed64 t2 = t * t;
        Fixed64 t3 = t2 * t;

        // Catmull-Rom spline formula
        Fixed64 a0 = v3 - v2 - v0 + v1;
        Fixed64 a1 = v0 - v1 - a0;
        Fixed64 a2 = v2 - v0;
        Fixed64 a3 = v1;

        return a0 * t3 + a1 * t2 + a2 * t + a3;
    }

    /// <summary>
    /// Moves a value towards a target by a maximum delta.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 MoveTowards(Fixed64 current, Fixed64 target, Fixed64 maxDelta)
    {
        Fixed64 diff = target - current;
        if (Abs(diff) <= maxDelta)
        {
            return target;
        }
        return current + Sign(diff) * maxDelta;
    }

    /// <summary>
    /// Inverse lerp - finds t such that Lerp(a, b, t) = value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 InverseLerp(Fixed64 a, Fixed64 b, Fixed64 value)
    {
        if (a._raw == b._raw) return Zero;
        return (value - a) / (b - a);
    }

    /// <summary>
    /// Remaps a value from one range to another.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Remap(Fixed64 value, Fixed64 fromMin, Fixed64 fromMax, Fixed64 toMin, Fixed64 toMax)
    {
        Fixed64 t = InverseLerp(fromMin, fromMax, value);
        return Lerp(toMin, toMax, t);
    }

    // ============================================================
    // Angle Conversion Utilities
    // ============================================================

    /// <summary>Degrees to radians conversion factor (Pi/180).</summary>
    public static readonly Fixed64 Deg2Rad = FromRaw(1144); // ~0.01745

    /// <summary>Radians to degrees conversion factor (180/Pi).</summary>
    public static readonly Fixed64 Rad2Deg = FromRaw(3754936); // ~57.2958

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 DegreesToRadians(Fixed64 degrees)
    {
        return degrees * Deg2Rad;
    }

    /// <summary>
    /// Converts radians to degrees.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 RadiansToDegrees(Fixed64 radians)
    {
        return radians * Rad2Deg;
    }

    /// <summary>
    /// Returns the smallest difference between two angles (in radians).
    /// Result is in range [-Pi, Pi].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 DeltaAngle(Fixed64 current, Fixed64 target)
    {
        Fixed64 diff = NormalizeAngle(target - current);
        return diff;
    }

    /// <summary>
    /// Linearly interpolates between two angles (in radians), taking the shortest path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 LerpAngle(Fixed64 a, Fixed64 b, Fixed64 t)
    {
        Fixed64 delta = DeltaAngle(a, b);
        return NormalizeAngle(a + delta * t);
    }

    /// <summary>
    /// Moves an angle towards a target by a maximum delta (in radians).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 MoveTowardsAngle(Fixed64 current, Fixed64 target, Fixed64 maxDelta)
    {
        Fixed64 delta = DeltaAngle(current, target);
        if (Abs(delta) <= maxDelta)
        {
            return target;
        }
        return NormalizeAngle(current + Sign(delta) * maxDelta);
    }

    public bool Equals(Fixed64 other)
    {
        return _raw == other._raw;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _raw.GetHashCode();
    }

    public int CompareTo(Fixed64 other)
    {
        return _raw.CompareTo(other._raw);
    }

    public override string ToString()
    {
        return ToDouble().ToString("F4");
    }

    public static implicit operator Fixed64(int value)
    {
        return FromInt(value);
    }

    public static explicit operator int(Fixed64 value)
    {
        return value.ToInt();
    }

    public static explicit operator float(Fixed64 value)
    {
        return value.ToFloat();
    }

    public static explicit operator double(Fixed64 value)
    {
        return value.ToDouble();
    }
}

