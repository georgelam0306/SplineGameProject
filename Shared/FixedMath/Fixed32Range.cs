using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 1D interval/range using Fixed32 for deterministic math.
/// </summary>
public readonly struct Fixed32Range : IEquatable<Fixed32Range>
{
    public readonly Fixed32 Min;
    public readonly Fixed32 Max;

    public static readonly Fixed32Range Zero = new(Fixed32.Zero, Fixed32.Zero);
    public static readonly Fixed32Range Unit = new(Fixed32.Zero, Fixed32.OneValue);
    public static readonly Fixed32Range Full = new(Fixed32.MinValue, Fixed32.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Range(Fixed32 min, Fixed32 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Creates a range from a center and half-extent.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Range FromCenterExtent(Fixed32 center, Fixed32 halfExtent)
    {
        return new Fixed32Range(center - halfExtent, center + halfExtent);
    }

    /// <summary>
    /// Creates a range from a center and size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Range FromCenterSize(Fixed32 center, Fixed32 size)
    {
        Fixed32 halfSize = size / 2;
        return new Fixed32Range(center - halfSize, center + halfSize);
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the center of the range.
    /// </summary>
    public Fixed32 Center => (Min + Max) / 2;

    /// <summary>
    /// Returns the size/length of the range.
    /// </summary>
    public Fixed32 Size => Max - Min;

    /// <summary>
    /// Returns the half-extent of the range.
    /// </summary>
    public Fixed32 Extent => (Max - Min) / 2;

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
    public bool Contains(Fixed32 value)
    {
        return value >= Min && value <= Max;
    }

    /// <summary>
    /// Checks if the range fully contains another range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed32Range other)
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
    public bool Overlaps(Fixed32Range other)
    {
        return Min <= other.Max && Max >= other.Min;
    }

    /// <summary>
    /// Returns the intersection of two ranges.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Range Intersection(Fixed32Range a, Fixed32Range b)
    {
        return new Fixed32Range(
            Fixed32.Max(a.Min, b.Min),
            Fixed32.Min(a.Max, b.Max)
        );
    }

    /// <summary>
    /// Returns the union of two ranges.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Range Union(Fixed32Range a, Fixed32Range b)
    {
        return new Fixed32Range(
            Fixed32.Min(a.Min, b.Min),
            Fixed32.Max(a.Max, b.Max)
        );
    }

    // ============================================================
    // Distance and Clamping
    // ============================================================

    /// <summary>
    /// Clamps a value to this range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Clamp(Fixed32 value)
    {
        return Fixed32.Clamp(value, Min, Max);
    }

    /// <summary>
    /// Returns the distance from a value to the nearest edge of the range.
    /// Returns 0 if the value is inside the range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Distance(Fixed32 value)
    {
        if (value < Min) return Min - value;
        if (value > Max) return value - Max;
        return Fixed32.Zero;
    }

    /// <summary>
    /// Returns the signed distance from a value to the range.
    /// Negative if inside, positive if outside.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 SignedDistance(Fixed32 value)
    {
        Fixed32 center = Center;
        Fixed32 extent = Extent;
        Fixed32 distFromCenter = Fixed32.Abs(value - center);
        return distFromCenter - extent;
    }

    // ============================================================
    // Transformation
    // ============================================================

    /// <summary>
    /// Returns a new range expanded by the given amount on both sides.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Range Expand(Fixed32 amount)
    {
        return new Fixed32Range(Min - amount, Max + amount);
    }

    /// <summary>
    /// Returns a new range shifted by the given offset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Range Translate(Fixed32 offset)
    {
        return new Fixed32Range(Min + offset, Max + offset);
    }

    /// <summary>
    /// Returns a new range scaled around its center.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Range Scale(Fixed32 factor)
    {
        Fixed32 center = Center;
        Fixed32 newExtent = Extent * factor;
        return new Fixed32Range(center - newExtent, center + newExtent);
    }

    /// <summary>
    /// Returns a new range that includes the given value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Range Encapsulate(Fixed32 value)
    {
        return new Fixed32Range(
            Fixed32.Min(Min, value),
            Fixed32.Max(Max, value)
        );
    }

    /// <summary>
    /// Returns a new range that includes another range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Range Encapsulate(Fixed32Range other)
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
    public Fixed32 InverseLerp(Fixed32 value)
    {
        Fixed32 size = Size;
        if (size.Raw == 0) return Fixed32.Zero;
        return (value - Min) / size;
    }

    /// <summary>
    /// Returns a value at the given normalized position within the range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Lerp(Fixed32 t)
    {
        return Min + Size * t;
    }

    /// <summary>
    /// Remaps a value from this range to another range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Remap(Fixed32 value, Fixed32Range targetRange)
    {
        Fixed32 t = InverseLerp(value);
        return targetRange.Lerp(t);
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32Range a, Fixed32Range b)
    {
        return a.Min == b.Min && a.Max == b.Max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32Range a, Fixed32Range b)
    {
        return !(a == b);
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed32Range other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32Range other && Equals(other);
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
