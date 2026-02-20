using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// An axis-aligned bounding box (AABB) using Fixed32 components for deterministic collision detection.
/// </summary>
public readonly struct Fixed32BoundingBox : IEquatable<Fixed32BoundingBox>
{
    public readonly Fixed32Vec3 Min;
    public readonly Fixed32Vec3 Max;

    public static readonly Fixed32BoundingBox Empty = new(Fixed32Vec3.Zero, Fixed32Vec3.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingBox(Fixed32Vec3 min, Fixed32Vec3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Creates a bounding box from center and half-extents.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32BoundingBox FromCenterExtents(Fixed32Vec3 center, Fixed32Vec3 halfExtents)
    {
        return new Fixed32BoundingBox(center - halfExtents, center + halfExtents);
    }

    /// <summary>
    /// Creates a bounding box from center and size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32BoundingBox FromCenterSize(Fixed32Vec3 center, Fixed32Vec3 size)
    {
        Fixed32Vec3 halfExtents = size / 2;
        return new Fixed32BoundingBox(center - halfExtents, center + halfExtents);
    }

    /// <summary>
    /// Creates a bounding box that encloses all the given points.
    /// </summary>
    public static Fixed32BoundingBox FromPoints(ReadOnlySpan<Fixed32Vec3> points)
    {
        if (points.Length == 0)
        {
            return Empty;
        }

        Fixed32Vec3 min = points[0];
        Fixed32Vec3 max = points[0];

        for (int i = 1; i < points.Length; i++)
        {
            min = Fixed32Vec3.Min(min, points[i]);
            max = Fixed32Vec3.Max(max, points[i]);
        }

        return new Fixed32BoundingBox(min, max);
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the center of the bounding box.
    /// </summary>
    public Fixed32Vec3 Center => (Min + Max) / 2;

    /// <summary>
    /// Returns the size of the bounding box.
    /// </summary>
    public Fixed32Vec3 Size => Max - Min;

    /// <summary>
    /// Returns the half-extents of the bounding box.
    /// </summary>
    public Fixed32Vec3 Extents => (Max - Min) / 2;

    /// <summary>
    /// Returns true if this is a valid (non-inverted) bounding box.
    /// </summary>
    public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;

    /// <summary>
    /// Returns the volume of the bounding box.
    /// </summary>
    public Fixed32 Volume
    {
        get
        {
            Fixed32Vec3 size = Size;
            return size.X * size.Y * size.Z;
        }
    }

    /// <summary>
    /// Returns the surface area of the bounding box.
    /// </summary>
    public Fixed32 SurfaceArea
    {
        get
        {
            Fixed32Vec3 size = Size;
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
    public bool Contains(Fixed32Vec3 point)
    {
        return point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y &&
               point.Z >= Min.Z && point.Z <= Max.Z;
    }

    /// <summary>
    /// Checks if this box fully contains another box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed32BoundingBox other)
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
    public bool Intersects(Fixed32BoundingBox other)
    {
        return Min.X <= other.Max.X && Max.X >= other.Min.X &&
               Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
               Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    }

    /// <summary>
    /// Checks if this box intersects with a sphere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Fixed32BoundingSphere sphere)
    {
        Fixed32Vec3 closest = ClosestPoint(sphere.Center);
        Fixed32 distSq = Fixed32Vec3.DistanceSquared(closest, sphere.Center);
        return distSq <= sphere.Radius * sphere.Radius;
    }

    /// <summary>
    /// Performs a ray-box intersection test.
    /// Returns true if the ray intersects and outputs the distance to intersection.
    /// </summary>
    public bool IntersectsRay(Fixed32Vec3 origin, Fixed32Vec3 direction, out Fixed32 distance)
    {
        distance = Fixed32.Zero;

        Fixed32 tMin = Fixed32.MinValue;
        Fixed32 tMax = Fixed32.MaxValue;

        // X slab
        if (direction.X.Raw != 0)
        {
            Fixed32 invD = Fixed32.OneValue / direction.X;
            Fixed32 t0 = (Min.X - origin.X) * invD;
            Fixed32 t1 = (Max.X - origin.X) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = Fixed32.Max(tMin, t0);
            tMax = Fixed32.Min(tMax, t1);
            if (tMin > tMax) return false;
        }
        else if (origin.X < Min.X || origin.X > Max.X)
        {
            return false;
        }

        // Y slab
        if (direction.Y.Raw != 0)
        {
            Fixed32 invD = Fixed32.OneValue / direction.Y;
            Fixed32 t0 = (Min.Y - origin.Y) * invD;
            Fixed32 t1 = (Max.Y - origin.Y) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = Fixed32.Max(tMin, t0);
            tMax = Fixed32.Min(tMax, t1);
            if (tMin > tMax) return false;
        }
        else if (origin.Y < Min.Y || origin.Y > Max.Y)
        {
            return false;
        }

        // Z slab
        if (direction.Z.Raw != 0)
        {
            Fixed32 invD = Fixed32.OneValue / direction.Z;
            Fixed32 t0 = (Min.Z - origin.Z) * invD;
            Fixed32 t1 = (Max.Z - origin.Z) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = Fixed32.Max(tMin, t0);
            tMax = Fixed32.Min(tMax, t1);
            if (tMin > tMax) return false;
        }
        else if (origin.Z < Min.Z || origin.Z > Max.Z)
        {
            return false;
        }

        distance = tMin >= Fixed32.Zero ? tMin : tMax;
        return distance >= Fixed32.Zero;
    }

    // ============================================================
    // Distance and Closest Point
    // ============================================================

    /// <summary>
    /// Returns the closest point on or inside the box to the given point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec3 ClosestPoint(Fixed32Vec3 point)
    {
        return Fixed32Vec3.Clamp(point, Min, Max);
    }

    /// <summary>
    /// Returns the squared distance from a point to the box surface (0 if inside).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 DistanceSquared(Fixed32Vec3 point)
    {
        Fixed32Vec3 closest = ClosestPoint(point);
        return Fixed32Vec3.DistanceSquared(point, closest);
    }

    /// <summary>
    /// Returns the distance from a point to the box surface (0 if inside).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Distance(Fixed32Vec3 point)
    {
        return Fixed32.Sqrt(DistanceSquared(point));
    }

    // ============================================================
    // Transformation and Combination
    // ============================================================

    /// <summary>
    /// Returns a new bounding box expanded by the given amount in all directions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingBox Expand(Fixed32 amount)
    {
        Fixed32Vec3 expansion = new Fixed32Vec3(amount, amount, amount);
        return new Fixed32BoundingBox(Min - expansion, Max + expansion);
    }

    /// <summary>
    /// Returns a new bounding box expanded by the given amounts per axis.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingBox Expand(Fixed32Vec3 amount)
    {
        return new Fixed32BoundingBox(Min - amount, Max + amount);
    }

    /// <summary>
    /// Returns a new bounding box that includes the given point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingBox Encapsulate(Fixed32Vec3 point)
    {
        return new Fixed32BoundingBox(
            Fixed32Vec3.Min(Min, point),
            Fixed32Vec3.Max(Max, point)
        );
    }

    /// <summary>
    /// Returns a new bounding box that includes another box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingBox Encapsulate(Fixed32BoundingBox other)
    {
        return new Fixed32BoundingBox(
            Fixed32Vec3.Min(Min, other.Min),
            Fixed32Vec3.Max(Max, other.Max)
        );
    }

    /// <summary>
    /// Returns the union of two bounding boxes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32BoundingBox Union(Fixed32BoundingBox a, Fixed32BoundingBox b)
    {
        return a.Encapsulate(b);
    }

    /// <summary>
    /// Returns the intersection of two bounding boxes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32BoundingBox Intersection(Fixed32BoundingBox a, Fixed32BoundingBox b)
    {
        return new Fixed32BoundingBox(
            Fixed32Vec3.Max(a.Min, b.Min),
            Fixed32Vec3.Min(a.Max, b.Max)
        );
    }

    /// <summary>
    /// Transforms the bounding box by a matrix, returning a new AABB that contains the result.
    /// </summary>
    public Fixed32BoundingBox Transform(Fixed32Mat4x4 matrix)
    {
        // Transform all 8 corners and find new bounds
        Fixed32Vec3 center = Center;
        Fixed32Vec3 extents = Extents;

        // Compute the new center
        Fixed32Vec3 newCenter = matrix.TransformPoint(center);

        // Compute the new extents using the absolute values of the rotation/scale matrix
        Fixed32Vec3 newExtentsX = Fixed32Vec3.Abs(new Fixed32Vec3(matrix.M00, matrix.M10, matrix.M20)) * extents.X;
        Fixed32Vec3 newExtentsY = Fixed32Vec3.Abs(new Fixed32Vec3(matrix.M01, matrix.M11, matrix.M21)) * extents.Y;
        Fixed32Vec3 newExtentsZ = Fixed32Vec3.Abs(new Fixed32Vec3(matrix.M02, matrix.M12, matrix.M22)) * extents.Z;
        Fixed32Vec3 newExtents = newExtentsX + newExtentsY + newExtentsZ;

        return new Fixed32BoundingBox(newCenter - newExtents, newCenter + newExtents);
    }

    /// <summary>
    /// Returns a translated bounding box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingBox Translate(Fixed32Vec3 offset)
    {
        return new Fixed32BoundingBox(Min + offset, Max + offset);
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32BoundingBox a, Fixed32BoundingBox b)
    {
        return a.Min == b.Min && a.Max == b.Max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32BoundingBox a, Fixed32BoundingBox b)
    {
        return !(a == b);
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed32BoundingBox other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32BoundingBox other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Min, Max);
    }

    public override string ToString()
    {
        return $"Fixed32BoundingBox(Min: {Min}, Max: {Max})";
    }
}
