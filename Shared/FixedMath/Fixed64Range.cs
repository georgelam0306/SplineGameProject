using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 1D interval/range using Fixed64 for deterministic math.
/// </summary>
public readonly struct Fixed64Range : IEquatable<Fixed64Range>
{
    public readonly Fixed64 Min;
    public readonly Fixed64 Max;

    public static readonly Fixed64Range Zero = new(Fixed64.Zero, Fixed64.Zero);
    public static readonly Fixed64Range Unit = new(Fixed64.Zero, Fixed64.OneValue);
    public static readonly Fixed64Range Full = new(Fixed64.MinValue, Fixed64.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Range(Fixed64 min, Fixed64 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Creates a range from a center and half-extent.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Range FromCenterExtent(Fixed64 center, Fixed64 halfExtent)
    {
        return new Fixed64Range(center - halfExtent, center + halfExtent);
    }

    /// <summary>
    /// Creates a range from a center and size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Range FromCenterSize(Fixed64 center, Fixed64 size)
    {
        Fixed64 halfSize = size / 2;
        return new Fixed64Range(center - halfSize, center + halfSize);
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the center of the range.
    /// </summary>
    public Fixed64 Center => (Min + Max) / 2;

    /// <summary>
    /// Returns the size/length of the range.
    /// </summary>
    public Fixed64 Size => Max - Min;

    /// <summary>
    /// Returns the half-extent of the range.
    /// </summary>
    public Fixed64 Extent => (Max - Min) / 2;

    /// <summary>
    /// Returns true if this is a valid (non-inverted) range.
    /// </summary>
    public bool IsValid => Min <= Max;

    /// <summary>
    /// Returns true if the range has zero size.
    /// </summary>
    public bool IsEmpty => Min == Max;

    // ============================================================
    // Containment
    // ============================================================

    /// <summary>
    /// Checks if the range contains a value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed64 value)
    {
        return value >= Min && value <= Max;
    }

    /// <summary>
    /// Checks if the range fully contains another range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed64Range other)
    {
        return other.Min >= Min && other.Max <= Max;
    }

    // ============================================================
    // Intersection
    // ============================================================

    /// <summary>
    /// Checks if this range overlaps with another range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Overlaps(Fixed64Range other)
    {
        return Min <= other.Max && Max >= other.Min;
    }

    /// <summary>
    /// Returns the intersection of two ranges.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Range Intersection(Fixed64Range a, Fixed64Range b)
    {
        return new Fixed64Range(
            Fixed64.Max(a.Min, b.Min),
            Fixed64.Min(a.Max, b.Max)
        );
    }

    /// <summary>
    /// Returns the union of two ranges.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Range Union(Fixed64Range a, Fixed64Range b)
    {
        return new Fixed64Range(
            Fixed64.Min(a.Min, b.Min),
            Fixed64.Max(a.Max, b.Max)
        );
    }

    // ============================================================
    // Distance and Clamping
    // ============================================================

    /// <summary>
    /// Clamps a value to this range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Clamp(Fixed64 value)
    {
        return Fixed64.Clamp(value, Min, Max);
    }

    /// <summary>
    /// Returns the distance from a value to the nearest edge of the range.
    /// Returns 0 if the value is inside the range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Distance(Fixed64 value)
    {
        if (value < Min) return Min - value;
        if (value > Max) return value - Max;
        return Fixed64.Zero;
    }

    /// <summary>
    /// Returns the signed distance from a value to the range.
    /// Negative if inside, positive if outside.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 SignedDistance(Fixed64 value)
    {
        Fixed64 center = Center;
        Fixed64 extent = Extent;
        Fixed64 distFromCenter = Fixed64.Abs(value - center);
        return distFromCenter - extent;
    }

    // ============================================================
    // Transformation
    // ============================================================

    /// <summary>
    /// Returns a new range expanded by the given amount on both sides.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Range Expand(Fixed64 amount)
    {
        return new Fixed64Range(Min - amount, Max + amount);
    }

    /// <summary>
    /// Returns a new range shifted by the given offset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Range Translate(Fixed64 offset)
    {
        return new Fixed64Range(Min + offset, Max + offset);
    }

    /// <summary>
    /// Returns a new range scaled around its center.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Range Scale(Fixed64 factor)
    {
        Fixed64 center = Center;
        Fixed64 newExtent = Extent * factor;
        return new Fixed64Range(center - newExtent, center + newExtent);
    }

    /// <summary>
    /// Returns a new range that includes the given value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Range Encapsulate(Fixed64 value)
    {
        return new Fixed64Range(
            Fixed64.Min(Min, value),
            Fixed64.Max(Max, value)
        );
    }

    /// <summary>
    /// Returns a new range that includes another range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Range Encapsulate(Fixed64Range other)
    {
        return Union(this, other);
    }

    // ============================================================
    // Normalization
    // ============================================================

    /// <summary>
    /// Returns the normalized position of a value within the range [0, 1].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 InverseLerp(Fixed64 value)
    {
        Fixed64 size = Size;
        if (size.Raw == 0) return Fixed64.Zero;
        return (value - Min) / size;
    }

    /// <summary>
    /// Returns a value at the given normalized position within the range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Lerp(Fixed64 t)
    {
        return Min + Size * t;
    }

    /// <summary>
    /// Remaps a value from this range to another range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Remap(Fixed64 value, Fixed64Range targetRange)
    {
        Fixed64 t = InverseLerp(value);
        return targetRange.Lerp(t);
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64Range a, Fixed64Range b)
    {
        return a.Min == b.Min && a.Max == b.Max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64Range a, Fixed64Range b)
    {
        return !(a == b);
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed64Range other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64Range other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Min, Max);
    }

    public override string ToString()
    {
        return $"[{Min}, {Max}]";
    }
}
