using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A keyframe for animation curves.
/// </summary>
public readonly struct Fixed32Keyframe
{
    public readonly Fixed32 Time;
    public readonly Fixed32 Value;
    public readonly Fixed32 InTangent;
    public readonly Fixed32 OutTangent;

    public Fixed32Keyframe(Fixed32 time, Fixed32 value)
    {
        Time = time;
        Value = value;
        InTangent = Fixed32.Zero;
        OutTangent = Fixed32.Zero;
    }

    public Fixed32Keyframe(Fixed32 time, Fixed32 value, Fixed32 inTangent, Fixed32 outTangent)
    {
        Time = time;
        Value = value;
        InTangent = inTangent;
        OutTangent = outTangent;
    }
}

/// <summary>
/// An animation curve using Fixed32 for deterministic interpolation.
/// </summary>
public readonly struct Fixed32Curve
{
    private readonly Fixed32Keyframe[] _keyframes;

    public static readonly Fixed32Curve Linear = new(
        new Fixed32Keyframe(Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue, Fixed32.OneValue),
        new Fixed32Keyframe(Fixed32.OneValue, Fixed32.OneValue, Fixed32.OneValue, Fixed32.OneValue)
    );

    public static readonly Fixed32Curve EaseInOut = new(
        new Fixed32Keyframe(Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero),
        new Fixed32Keyframe(Fixed32.OneValue, Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero)
    );

    public static readonly Fixed32Curve EaseIn = new(
        new Fixed32Keyframe(Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero),
        new Fixed32Keyframe(Fixed32.OneValue, Fixed32.OneValue, Fixed32.FromInt(2), Fixed32.FromInt(2))
    );

    public static readonly Fixed32Curve EaseOut = new(
        new Fixed32Keyframe(Fixed32.Zero, Fixed32.Zero, Fixed32.FromInt(2), Fixed32.FromInt(2)),
        new Fixed32Keyframe(Fixed32.OneValue, Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero)
    );

    public Fixed32Curve(params Fixed32Keyframe[] keyframes)
    {
        if (keyframes.Length == 0)
        {
            _keyframes = new[] { new Fixed32Keyframe(Fixed32.Zero, Fixed32.Zero) };
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
    public Fixed32Keyframe GetKeyframe(int index)
    {
        if (_keyframes == null || index < 0 || index >= _keyframes.Length)
        {
            return new Fixed32Keyframe(Fixed32.Zero, Fixed32.Zero);
        }
        return _keyframes[index];
    }

    /// <summary>
    /// Evaluates the curve at the given time.
    /// </summary>
    public Fixed32 Evaluate(Fixed32 time)
    {
        if (_keyframes == null || _keyframes.Length == 0)
        {
            return Fixed32.Zero;
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

        Fixed32Keyframe k0 = _keyframes[segmentIndex];
        Fixed32Keyframe k1 = _keyframes[segmentIndex + 1];

        Fixed32 duration = k1.Time - k0.Time;
        if (duration.Raw == 0)
        {
            return k0.Value;
        }

        Fixed32 t = (time - k0.Time) / duration;

        // Hermite interpolation
        return HermiteInterpolate(k0.Value, k0.OutTangent * duration, k1.Value, k1.InTangent * duration, t);
    }

    /// <summary>
    /// Hermite interpolation between two values with tangents.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed32 HermiteInterpolate(Fixed32 p0, Fixed32 m0, Fixed32 p1, Fixed32 m1, Fixed32 t)
    {
        Fixed32 t2 = t * t;
        Fixed32 t3 = t2 * t;

        // Hermite basis functions
        Fixed32 h00 = 2 * t3 - 3 * t2 + Fixed32.OneValue;
        Fixed32 h10 = t3 - 2 * t2 + t;
        Fixed32 h01 = -2 * t3 + 3 * t2;
        Fixed32 h11 = t3 - t2;

        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }

    /// <summary>
    /// Creates a linear curve from 0 to 1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Curve CreateLinear()
    {
        return Linear;
    }

    /// <summary>
    /// Creates a constant curve with the given value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Curve CreateConstant(Fixed32 value)
    {
        return new Fixed32Curve(new Fixed32Keyframe(Fixed32.Zero, value));
    }

    /// <summary>
    /// Creates a curve from two values (start and end at times 0 and 1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Curve CreateFromValues(Fixed32 startValue, Fixed32 endValue)
    {
        Fixed32 tangent = endValue - startValue;
        return new Fixed32Curve(
            new Fixed32Keyframe(Fixed32.Zero, startValue, tangent, tangent),
            new Fixed32Keyframe(Fixed32.OneValue, endValue, tangent, tangent)
        );
    }
}
