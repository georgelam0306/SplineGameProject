using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// An axis-aligned bounding box (AABB) using Fixed64 components for deterministic collision detection.
/// </summary>
public readonly struct Fixed64BoundingBox : IEquatable<Fixed64BoundingBox>
{
    public readonly Fixed64Vec3 Min;
    public readonly Fixed64Vec3 Max;

    public static readonly Fixed64BoundingBox Empty = new(Fixed64Vec3.Zero, Fixed64Vec3.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingBox(Fixed64Vec3 min, Fixed64Vec3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Creates a bounding box from center and half-extents.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64BoundingBox FromCenterExtents(Fixed64Vec3 center, Fixed64Vec3 halfExtents)
    {
        return new Fixed64BoundingBox(center - halfExtents, center + halfExtents);
    }

    /// <summary>
    /// Creates a bounding box from center and size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64BoundingBox FromCenterSize(Fixed64Vec3 center, Fixed64Vec3 size)
    {
        Fixed64Vec3 halfExtents = size / 2;
        return new Fixed64BoundingBox(center - halfExtents, center + halfExtents);
    }

    /// <summary>
    /// Creates a bounding box that encloses all the given points.
    /// </summary>
    public static Fixed64BoundingBox FromPoints(ReadOnlySpan<Fixed64Vec3> points)
    {
        if (points.Length == 0)
        {
            return Empty;
        }

        Fixed64Vec3 min = points[0];
        Fixed64Vec3 max = points[0];

        for (int i = 1; i < points.Length; i++)
        {
            min = Fixed64Vec3.Min(min, points[i]);
            max = Fixed64Vec3.Max(max, points[i]);
        }

        return new Fixed64BoundingBox(min, max);
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the center of the bounding box.
    /// </summary>
    public Fixed64Vec3 Center => (Min + Max) / 2;

    /// <summary>
    /// Returns the size of the bounding box.
    /// </summary>
    public Fixed64Vec3 Size => Max - Min;

    /// <summary>
    /// Returns the half-extents of the bounding box.
    /// </summary>
    public Fixed64Vec3 Extents => (Max - Min) / 2;

    /// <summary>
    /// Returns true if this is a valid (non-inverted) bounding box.
    /// </summary>
    public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;

    /// <summary>
    /// Returns the volume of the bounding box.
    /// </summary>
    public Fixed64 Volume
    {
        get
        {
            Fixed64Vec3 size = Size;
            return size.X * size.Y * size.Z;
        }
    }

    /// <summary>
    /// Returns the surface area of the bounding box.
    /// </summary>
    public Fixed64 SurfaceArea
    {
        get
        {
            Fixed64Vec3 size = Size;
            return 2 * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
        }
    }

    // ============================================================
    // Containment Tests
    // ============================================================

    /// <summary>
    /// Checks if this box contains a point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed64Vec3 point)
    {
        return point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y &&
               point.Z >= Min.Z && point.Z <= Max.Z;
    }

    /// <summary>
    /// Checks if this box fully contains another box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed64BoundingBox other)
    {
        return other.Min.X >= Min.X && other.Max.X <= Max.X &&
               other.Min.Y >= Min.Y && other.Max.Y <= Max.Y &&
               other.Min.Z >= Min.Z && other.Max.Z <= Max.Z;
    }

    // ============================================================
    // Intersection Tests
    // ============================================================

    /// <summary>
    /// Checks if this box intersects with another box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Fixed64BoundingBox other)
    {
        return Min.X <= other.Max.X && Max.X >= other.Min.X &&
               Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
               Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    }

    /// <summary>
    /// Checks if this box intersects with a sphere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Fixed64BoundingSphere sphere)
    {
        Fixed64Vec3 closest = ClosestPoint(sphere.Center);
        Fixed64 distSq = Fixed64Vec3.DistanceSquared(closest, sphere.Center);
        return distSq <= sphere.Radius * sphere.Radius;
    }

    /// <summary>
    /// Performs a ray-box intersection test.
    /// Returns true if the ray intersects and outputs the distance to intersection.
    /// </summary>
    public bool IntersectsRay(Fixed64Vec3 origin, Fixed64Vec3 direction, out Fixed64 distance)
    {
        distance = Fixed64.Zero;

        Fixed64 tMin = Fixed64.MinValue;
        Fixed64 tMax = Fixed64.MaxValue;

        // X slab
        if (direction.X.Raw != 0)
        {
            Fixed64 invD = Fixed64.OneValue / direction.X;
            Fixed64 t0 = (Min.X - origin.X) * invD;
            Fixed64 t1 = (Max.X - origin.X) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = Fixed64.Max(tMin, t0);
            tMax = Fixed64.Min(tMax, t1);
            if (tMin > tMax) return false;
        }
        else if (origin.X < Min.X || origin.X > Max.X)
        {
            return false;
        }

        // Y slab
        if (direction.Y.Raw != 0)
        {
            Fixed64 invD = Fixed64.OneValue / direction.Y;
            Fixed64 t0 = (Min.Y - origin.Y) * invD;
            Fixed64 t1 = (Max.Y - origin.Y) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = Fixed64.Max(tMin, t0);
            tMax = Fixed64.Min(tMax, t1);
            if (tMin > tMax) return false;
        }
        else if (origin.Y < Min.Y || origin.Y > Max.Y)
        {
            return false;
        }

        // Z slab
        if (direction.Z.Raw != 0)
        {
            Fixed64 invD = Fixed64.OneValue / direction.Z;
            Fixed64 t0 = (Min.Z - origin.Z) * invD;
            Fixed64 t1 = (Max.Z - origin.Z) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = Fixed64.Max(tMin, t0);
            tMax = Fixed64.Min(tMax, t1);
            if (tMin > tMax) return false;
        }
        else if (origin.Z < Min.Z || origin.Z > Max.Z)
        {
            return false;
        }

        distance = tMin >= Fixed64.Zero ? tMin : tMax;
        return distance >= Fixed64.Zero;
    }

    // ============================================================
    // Distance and Closest Point
    // ============================================================

    /// <summary>
    /// Returns the closest point on or inside the box to the given point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3 ClosestPoint(Fixed64Vec3 point)
    {
        return Fixed64Vec3.Clamp(point, Min, Max);
    }

    /// <summary>
    /// Returns the squared distance from a point to the box surface (0 if inside).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 DistanceSquared(Fixed64Vec3 point)
    {
        Fixed64Vec3 closest = ClosestPoint(point);
        return Fixed64Vec3.DistanceSquared(point, closest);
    }

    /// <summary>
    /// Returns the distance from a point to the box surface (0 if inside).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Distance(Fixed64Vec3 point)
    {
        return Fixed64.Sqrt(DistanceSquared(point));
    }

    // ============================================================
    // Transformation and Combination
    // ============================================================

    /// <summary>
    /// Returns a new bounding box expanded by the given amount in all directions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingBox Expand(Fixed64 amount)
    {
        Fixed64Vec3 expansion = new Fixed64Vec3(amount, amount, amount);
        return new Fixed64BoundingBox(Min - expansion, Max + expansion);
    }

    /// <summary>
    /// Returns a new bounding box expanded by the given amounts per axis.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingBox Expand(Fixed64Vec3 amount)
    {
        return new Fixed64BoundingBox(Min - amount, Max + amount);
    }

    /// <summary>
    /// Returns a new bounding box that includes the given point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingBox Encapsulate(Fixed64Vec3 point)
    {
        return new Fixed64BoundingBox(
            Fixed64Vec3.Min(Min, point),
            Fixed64Vec3.Max(Max, point)
        );
    }

    /// <summary>
    /// Returns a new bounding box that includes another box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingBox Encapsulate(Fixed64BoundingBox other)
    {
        return new Fixed64BoundingBox(
            Fixed64Vec3.Min(Min, other.Min),
            Fixed64Vec3.Max(Max, other.Max)
        );
    }

    /// <summary>
    /// Returns the union of two bounding boxes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64BoundingBox Union(Fixed64BoundingBox a, Fixed64BoundingBox b)
    {
        return a.Encapsulate(b);
    }

    /// <summary>
    /// Returns the intersection of two bounding boxes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64BoundingBox Intersection(Fixed64BoundingBox a, Fixed64BoundingBox b)
    {
        return new Fixed64BoundingBox(
            Fixed64Vec3.Max(a.Min, b.Min),
            Fixed64Vec3.Min(a.Max, b.Max)
        );
    }

    /// <summary>
    /// Transforms the bounding box by a matrix, returning a new AABB that contains the result.
    /// </summary>
    public Fixed64BoundingBox Transform(Fixed64Mat4x4 matrix)
    {
        // Transform all 8 corners and find new bounds
        Fixed64Vec3 center = Center;
        Fixed64Vec3 extents = Extents;

        // Compute the new center
        Fixed64Vec3 newCenter = matrix.TransformPoint(center);

        // Compute the new extents using the absolute values of the rotation/scale matrix
        Fixed64Vec3 newExtentsX = Fixed64Vec3.Abs(new Fixed64Vec3(matrix.M00, matrix.M10, matrix.M20)) * extents.X;
        Fixed64Vec3 newExtentsY = Fixed64Vec3.Abs(new Fixed64Vec3(matrix.M01, matrix.M11, matrix.M21)) * extents.Y;
        Fixed64Vec3 newExtentsZ = Fixed64Vec3.Abs(new Fixed64Vec3(matrix.M02, matrix.M12, matrix.M22)) * extents.Z;
        Fixed64Vec3 newExtents = newExtentsX + newExtentsY + newExtentsZ;

        return new Fixed64BoundingBox(newCenter - newExtents, newCenter + newExtents);
    }

    /// <summary>
    /// Returns a translated bounding box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingBox Translate(Fixed64Vec3 offset)
    {
        return new Fixed64BoundingBox(Min + offset, Max + offset);
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64BoundingBox a, Fixed64BoundingBox b)
    {
        return a.Min == b.Min && a.Max == b.Max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64BoundingBox a, Fixed64BoundingBox b)
    {
        return !(a == b);
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed64BoundingBox other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64BoundingBox other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Min, Max);
    }

    public override string ToString()
    {
        return $"Fixed64BoundingBox(Min: {Min}, Max: {Max})";
    }
}
