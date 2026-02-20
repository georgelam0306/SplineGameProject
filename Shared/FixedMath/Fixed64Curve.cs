using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A keyframe for animation curves.
/// </summary>
public readonly struct Fixed64Keyframe
{
    public readonly Fixed64 Time;
    public readonly Fixed64 Value;
    public readonly Fixed64 InTangent;
    public readonly Fixed64 OutTangent;

    public Fixed64Keyframe(Fixed64 time, Fixed64 value)
    {
        Time = time;
        Value = value;
        InTangent = Fixed64.Zero;
        OutTangent = Fixed64.Zero;
    }

    public Fixed64Keyframe(Fixed64 time, Fixed64 value, Fixed64 inTangent, Fixed64 outTangent)
    {
        Time = time;
        Value = value;
        InTangent = inTangent;
        OutTangent = outTangent;
    }
}

/// <summary>
/// An animation curve using Fixed64 for deterministic interpolation.
/// </summary>
public readonly struct Fixed64Curve
{
    private readonly Fixed64Keyframe[] _keyframes;

    public static readonly Fixed64Curve Linear = new(
        new Fixed64Keyframe(Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue, Fixed64.OneValue),
        new Fixed64Keyframe(Fixed64.OneValue, Fixed64.OneValue, Fixed64.OneValue, Fixed64.OneValue)
    );

    public static readonly Fixed64Curve EaseInOut = new(
        new Fixed64Keyframe(Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero),
        new Fixed64Keyframe(Fixed64.OneValue, Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero)
    );

    public static readonly Fixed64Curve EaseIn = new(
        new Fixed64Keyframe(Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero),
        new Fixed64Keyframe(Fixed64.OneValue, Fixed64.OneValue, Fixed64.FromInt(2), Fixed64.FromInt(2))
    );

    public static readonly Fixed64Curve EaseOut = new(
        new Fixed64Keyframe(Fixed64.Zero, Fixed64.Zero, Fixed64.FromInt(2), Fixed64.FromInt(2)),
        new Fixed64Keyframe(Fixed64.OneValue, Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero)
    );

    public Fixed64Curve(params Fixed64Keyframe[] keyframes)
    {
        if (keyframes.Length == 0)
        {
            _keyframes = new[] { new Fixed64Keyframe(Fixed64.Zero, Fixed64.Zero) };
        }
        else
        {
            _keyframes = keyframes;
        }
    }

    /// <summary>
    /// Returns the number of keyframes.
    /// </summary>
    public int KeyframeCount => _keyframes?.Length ?? 0;

    /// <summary>
    /// Gets a keyframe by index.
    /// </summary>
    public Fixed64Keyframe GetKeyframe(int index)
    {
        if (_keyframes == null || index < 0 || index >= _keyframes.Length)
        {
            return new Fixed64Keyframe(Fixed64.Zero, Fixed64.Zero);
        }
        return _keyframes[index];
    }

    /// <summary>
    /// Evaluates the curve at the given time.
    /// </summary>
    public Fixed64 Evaluate(Fixed64 time)
    {
        if (_keyframes == null || _keyframes.Length == 0)
        {
            return Fixed64.Zero;
        }

        if (_keyframes.Length == 1)
        {
            return _keyframes[0].Value;
        }

        // Find the segment
        if (time <= _keyframes[0].Time)
        {
            return _keyframes[0].Value;
        }

        if (time >= _keyframes[^1].Time)
        {
            return _keyframes[^1].Value;
        }

        int segmentIndex = 0;
        for (int i = 0; i < _keyframes.Length - 1; i++)
        {
            if (time >= _keyframes[i].Time && time < _keyframes[i + 1].Time)
            {
                segmentIndex = i;
                break;
            }
        }

        Fixed64Keyframe k0 = _keyframes[segmentIndex];
        Fixed64Keyframe k1 = _keyframes[segmentIndex + 1];

        Fixed64 duration = k1.Time - k0.Time;
        if (duration.Raw == 0)
        {
            return k0.Value;
        }

        Fixed64 t = (time - k0.Time) / duration;

        // Hermite interpolation
        return HermiteInterpolate(k0.Value, k0.OutTangent * duration, k1.Value, k1.InTangent * duration, t);
    }

    /// <summary>
    /// Hermite interpolation between two values with tangents.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 HermiteInterpolate(Fixed64 p0, Fixed64 m0, Fixed64 p1, Fixed64 m1, Fixed64 t)
    {
        Fixed64 t2 = t * t;
        Fixed64 t3 = t2 * t;

        // Hermite basis functions
        Fixed64 h00 = 2 * t3 - 3 * t2 + Fixed64.OneValue;
        Fixed64 h10 = t3 - 2 * t2 + t;
        Fixed64 h01 = -2 * t3 + 3 * t2;
        Fixed64 h11 = t3 - t2;

        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }

    /// <summary>
    /// Creates a linear curve from 0 to 1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Curve CreateLinear()
    {
        return Linear;
    }

    /// <summary>
    /// Creates a constant curve with the given value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Curve CreateConstant(Fixed64 value)
    {
        return new Fixed64Curve(new Fixed64Keyframe(Fixed64.Zero, value));
    }

    /// <summary>
    /// Creates a curve from two values (start and end at times 0 and 1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Curve CreateFromValues(Fixed64 startValue, Fixed64 endValue)
    {
        Fixed64 tangent = endValue - startValue;
        return new Fixed64Curve(
            new Fixed64Keyframe(Fixed64.Zero, startValue, tangent, tangent),
            new Fixed64Keyframe(Fixed64.OneValue, endValue, tangent, tangent)
        );
    }
}
