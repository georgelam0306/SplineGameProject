using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A bounding sphere using Fixed64 components for deterministic collision detection.
/// </summary>
public readonly struct Fixed64BoundingSphere : IEquatable<Fixed64BoundingSphere>
{
    public readonly Fixed64Vec3 Center;
    public readonly Fixed64 Radius;

    public static readonly Fixed64BoundingSphere Empty = new(Fixed64Vec3.Zero, Fixed64.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingSphere(Fixed64Vec3 center, Fixed64 radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>
    /// Creates a bounding sphere from a bounding box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64BoundingSphere FromFixed64BoundingBox(Fixed64BoundingBox box)
    {
        Fixed64Vec3 center = box.Center;
        Fixed64 radius = Fixed64Vec3.Distance(center, box.Max);
        return new Fixed64BoundingSphere(center, radius);
    }

    /// <summary>
    /// Creates a bounding sphere that encloses all the given points.
    /// Uses a simple algorithm (not necessarily minimum enclosing sphere).
    /// </summary>
    public static Fixed64BoundingSphere FromPoints(ReadOnlySpan<Fixed64Vec3> points)
    {
        if (points.Length == 0)
        {
            return Empty;
        }

        // Find bounding box center
        Fixed64Vec3 min = points[0];
        Fixed64Vec3 max = points[0];

        for (int i = 1; i < points.Length; i++)
        {
            min = Fixed64Vec3.Min(min, points[i]);
            max = Fixed64Vec3.Max(max, points[i]);
        }

        Fixed64Vec3 center = (min + max) / 2;

        // Find maximum distance from center
        Fixed64 maxDistSq = Fixed64.Zero;
        for (int i = 0; i < points.Length; i++)
        {
            Fixed64 distSq = Fixed64Vec3.DistanceSquared(center, points[i]);
            if (distSq > maxDistSq)
            {
                maxDistSq = distSq;
            }
        }

        return new Fixed64BoundingSphere(center, Fixed64.Sqrt(maxDistSq));
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the diameter of the sphere.
    /// </summary>
    public Fixed64 Diameter => Radius * 2;

    /// <summary>
    /// Returns the volume of the sphere.
    /// </summary>
    public Fixed64 Volume
    {
        get
        {
            // V = (4/3) * π * r³
            Fixed64 r3 = Radius * Radius * Radius;
            return Fixed64.FromFloat(4.18879f) * r3; // 4π/3 ≈ 4.18879
        }
    }

    /// <summary>
    /// Returns the surface area of the sphere.
    /// </summary>
    public Fixed64 SurfaceArea
    {
        get
        {
            // A = 4 * π * r²
            Fixed64 r2 = Radius * Radius;
            return Fixed64.FromFloat(12.56637f) * r2; // 4π ≈ 12.56637
        }
    }

    // ============================================================
    // Containment Tests
    // ============================================================

    /// <summary>
    /// Checks if this sphere contains a point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed64Vec3 point)
    {
        Fixed64 distSq = Fixed64Vec3.DistanceSquared(Center, point);
        return distSq <= Radius * Radius;
    }

    /// <summary>
    /// Checks if this sphere fully contains another sphere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed64BoundingSphere other)
    {
        Fixed64 dist = Fixed64Vec3.Distance(Center, other.Center);
        return dist + other.Radius <= Radius;
    }

    // ============================================================
    // Intersection Tests
    // ============================================================

    /// <summary>
    /// Checks if this sphere intersects with another sphere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Fixed64BoundingSphere other)
    {
        Fixed64 distSq = Fixed64Vec3.DistanceSquared(Center, other.Center);
        Fixed64 radiusSum = Radius + other.Radius;
        return distSq <= radiusSum * radiusSum;
    }

    /// <summary>
    /// Checks if this sphere intersects with a bounding box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Fixed64BoundingBox box)
    {
        Fixed64Vec3 closest = box.ClosestPoint(Center);
        Fixed64 distSq = Fixed64Vec3.DistanceSquared(closest, Center);
        return distSq <= Radius * Radius;
    }

    /// <summary>
    /// Performs a ray-sphere intersection test.
    /// Returns true if the ray intersects and outputs the distance to intersection.
    /// </summary>
    public bool IntersectsRay(Fixed64Vec3 origin, Fixed64Vec3 direction, out Fixed64 distance)
    {
        Fixed64Vec3 oc = origin - Center;
        Fixed64 a = Fixed64Vec3.Dot(direction, direction);
        Fixed64 b = 2 * Fixed64Vec3.Dot(oc, direction);
        Fixed64 c = Fixed64Vec3.Dot(oc, oc) - Radius * Radius;
        Fixed64 discriminant = b * b - 4 * a * c;

        if (discriminant < Fixed64.Zero)
        {
            distance = Fixed64.Zero;
            return false;
        }

        Fixed64 sqrtD = Fixed64.Sqrt(discriminant);
        Fixed64 t1 = (-b - sqrtD) / (2 * a);
        Fixed64 t2 = (-b + sqrtD) / (2 * a);

        if (t1 >= Fixed64.Zero)
        {
            distance = t1;
            return true;
        }
        else if (t2 >= Fixed64.Zero)
        {
            distance = t2;
            return true;
        }

        distance = Fixed64.Zero;
        return false;
    }

    // ============================================================
    // Distance and Closest Point
    // ============================================================

    /// <summary>
    /// Returns the closest point on the sphere surface to the given point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3 ClosestPoint(Fixed64Vec3 point)
    {
        Fixed64Vec3 direction = point - Center;
        Fixed64 distSq = direction.LengthSquared();

        if (distSq.Raw == 0)
        {
            return Center + new Fixed64Vec3(Radius, Fixed64.Zero, Fixed64.Zero);
        }

        return Center + direction.Normalized() * Radius;
    }

    /// <summary>
    /// Returns the distance from a point to the sphere surface (negative if inside).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 SignedDistance(Fixed64Vec3 point)
    {
        return Fixed64Vec3.Distance(Center, point) - Radius;
    }

    /// <summary>
    /// Returns the distance from a point to the sphere surface (0 if inside).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Distance(Fixed64Vec3 point)
    {
        Fixed64 signedDist = SignedDistance(point);
        return signedDist > Fixed64.Zero ? signedDist : Fixed64.Zero;
    }

    // ============================================================
    // Transformation and Combination
    // ============================================================

    /// <summary>
    /// Returns a new sphere with an expanded radius.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingSphere Expand(Fixed64 amount)
    {
        return new Fixed64BoundingSphere(Center, Radius + amount);
    }

    /// <summary>
    /// Returns a new sphere that includes the given point.
    /// </summary>
    public Fixed64BoundingSphere Encapsulate(Fixed64Vec3 point)
    {
        Fixed64Vec3 direction = point - Center;
        Fixed64 dist = direction.Length();

        if (dist <= Radius)
        {
            return this;
        }

        Fixed64 newRadius = (Radius + dist) / 2;
        Fixed64Vec3 newCenter = Center + direction.Normalized() * (newRadius - Radius);
        return new Fixed64BoundingSphere(newCenter, newRadius);
    }

    /// <summary>
    /// Returns a new sphere that includes another sphere.
    /// </summary>
    public Fixed64BoundingSphere Encapsulate(Fixed64BoundingSphere other)
    {
        Fixed64Vec3 direction = other.Center - Center;
        Fixed64 dist = direction.Length();

        // This sphere already contains the other
        if (dist + other.Radius <= Radius)
        {
            return this;
        }

        // Other sphere already contains this
        if (dist + Radius <= other.Radius)
        {
            return other;
        }

        // Need to create a new sphere
        Fixed64 newRadius = (Radius + dist + other.Radius) / 2;
        Fixed64Vec3 newCenter = Center + direction.Normalized() * (newRadius - Radius);
        return new Fixed64BoundingSphere(newCenter, newRadius);
    }

    /// <summary>
    /// Returns a translated sphere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64BoundingSphere Translate(Fixed64Vec3 offset)
    {
        return new Fixed64BoundingSphere(Center + offset, Radius);
    }

    /// <summary>
    /// Returns a sphere transformed by a matrix (uses maximum scale for radius).
    /// </summary>
    public Fixed64BoundingSphere Transform(Fixed64Mat4x4 matrix)
    {
        Fixed64Vec3 newCenter = matrix.TransformPoint(Center);
        Fixed64Vec3 scale = matrix.Scale;
        Fixed64 maxScale = Fixed64.Max(Fixed64.Max(scale.X, scale.Y), scale.Z);
        return new Fixed64BoundingSphere(newCenter, Radius * maxScale);
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64BoundingSphere a, Fixed64BoundingSphere b)
    {
        return a.Center == b.Center && a.Radius == b.Radius;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64BoundingSphere a, Fixed64BoundingSphere b)
    {
        return !(a == b);
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed64BoundingSphere other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64BoundingSphere other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Center, Radius);
    }

    public override string ToString()
    {
        return $"Fixed64BoundingSphere(Center: {Center}, Radius: {Radius})";
    }
}
