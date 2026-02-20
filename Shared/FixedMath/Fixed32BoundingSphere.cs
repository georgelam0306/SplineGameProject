using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A bounding sphere using Fixed32 components for deterministic collision detection.
/// </summary>
public readonly struct Fixed32BoundingSphere : IEquatable<Fixed32BoundingSphere>
{
    public readonly Fixed32Vec3 Center;
    public readonly Fixed32 Radius;

    public static readonly Fixed32BoundingSphere Empty = new(Fixed32Vec3.Zero, Fixed32.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingSphere(Fixed32Vec3 center, Fixed32 radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>
    /// Creates a bounding sphere from a bounding box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32BoundingSphere FromFixed32BoundingBox(Fixed32BoundingBox box)
    {
        Fixed32Vec3 center = box.Center;
        Fixed32 radius = Fixed32Vec3.Distance(center, box.Max);
        return new Fixed32BoundingSphere(center, radius);
    }

    /// <summary>
    /// Creates a bounding sphere that encloses all the given points.
    /// Uses a simple algorithm (not necessarily minimum enclosing sphere).
    /// </summary>
    public static Fixed32BoundingSphere FromPoints(ReadOnlySpan<Fixed32Vec3> points)
    {
        if (points.Length == 0)
        {
            return Empty;
        }

        // Find bounding box center
        Fixed32Vec3 min = points[0];
        Fixed32Vec3 max = points[0];

        for (int i = 1; i < points.Length; i++)
        {
            min = Fixed32Vec3.Min(min, points[i]);
            max = Fixed32Vec3.Max(max, points[i]);
        }

        Fixed32Vec3 center = (min + max) / 2;

        // Find maximum distance from center
        Fixed32 maxDistSq = Fixed32.Zero;
        for (int i = 0; i < points.Length; i++)
        {
            Fixed32 distSq = Fixed32Vec3.DistanceSquared(center, points[i]);
            if (distSq > maxDistSq)
            {
                maxDistSq = distSq;
            }
        }

        return new Fixed32BoundingSphere(center, Fixed32.Sqrt(maxDistSq));
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the diameter of the sphere.
    /// </summary>
    public Fixed32 Diameter => Radius * 2;

    /// <summary>
    /// Returns the volume of the sphere.
    /// </summary>
    public Fixed32 Volume
    {
        get
        {
            // V = (4/3) * π * r³
            Fixed32 r3 = Radius * Radius * Radius;
            return Fixed32.FromFloat(4.18879f) * r3; // 4π/3 ≈ 4.18879
        }
    }

    /// <summary>
    /// Returns the surface area of the sphere.
    /// </summary>
    public Fixed32 SurfaceArea
    {
        get
        {
            // A = 4 * π * r²
            Fixed32 r2 = Radius * Radius;
            return Fixed32.FromFloat(12.56637f) * r2; // 4π ≈ 12.56637
        }
    }

    // ============================================================
    // Containment Tests
    // ============================================================

    /// <summary>
    /// Checks if this sphere contains a point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed32Vec3 point)
    {
        Fixed32 distSq = Fixed32Vec3.DistanceSquared(Center, point);
        return distSq <= Radius * Radius;
    }

    /// <summary>
    /// Checks if this sphere fully contains another sphere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Fixed32BoundingSphere other)
    {
        Fixed32 dist = Fixed32Vec3.Distance(Center, other.Center);
        return dist + other.Radius <= Radius;
    }

    // ============================================================
    // Intersection Tests
    // ============================================================

    /// <summary>
    /// Checks if this sphere intersects with another sphere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Fixed32BoundingSphere other)
    {
        Fixed32 distSq = Fixed32Vec3.DistanceSquared(Center, other.Center);
        Fixed32 radiusSum = Radius + other.Radius;
        return distSq <= radiusSum * radiusSum;
    }

    /// <summary>
    /// Checks if this sphere intersects with a bounding box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Fixed32BoundingBox box)
    {
        Fixed32Vec3 closest = box.ClosestPoint(Center);
        Fixed32 distSq = Fixed32Vec3.DistanceSquared(closest, Center);
        return distSq <= Radius * Radius;
    }

    /// <summary>
    /// Performs a ray-sphere intersection test.
    /// Returns true if the ray intersects and outputs the distance to intersection.
    /// </summary>
    public bool IntersectsRay(Fixed32Vec3 origin, Fixed32Vec3 direction, out Fixed32 distance)
    {
        Fixed32Vec3 oc = origin - Center;
        Fixed32 a = Fixed32Vec3.Dot(direction, direction);
        Fixed32 b = 2 * Fixed32Vec3.Dot(oc, direction);
        Fixed32 c = Fixed32Vec3.Dot(oc, oc) - Radius * Radius;
        Fixed32 discriminant = b * b - 4 * a * c;

        if (discriminant < Fixed32.Zero)
        {
            distance = Fixed32.Zero;
            return false;
        }

        Fixed32 sqrtD = Fixed32.Sqrt(discriminant);
        Fixed32 t1 = (-b - sqrtD) / (2 * a);
        Fixed32 t2 = (-b + sqrtD) / (2 * a);

        if (t1 >= Fixed32.Zero)
        {
            distance = t1;
            return true;
        }
        else if (t2 >= Fixed32.Zero)
        {
            distance = t2;
            return true;
        }

        distance = Fixed32.Zero;
        return false;
    }

    // ============================================================
    // Distance and Closest Point
    // ============================================================

    /// <summary>
    /// Returns the closest point on the sphere surface to the given point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec3 ClosestPoint(Fixed32Vec3 point)
    {
        Fixed32Vec3 direction = point - Center;
        Fixed32 distSq = direction.LengthSquared();

        if (distSq.Raw == 0)
        {
            return Center + new Fixed32Vec3(Radius, Fixed32.Zero, Fixed32.Zero);
        }

        return Center + direction.Normalized() * Radius;
    }

    /// <summary>
    /// Returns the distance from a point to the sphere surface (negative if inside).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 SignedDistance(Fixed32Vec3 point)
    {
        return Fixed32Vec3.Distance(Center, point) - Radius;
    }

    /// <summary>
    /// Returns the distance from a point to the sphere surface (0 if inside).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Distance(Fixed32Vec3 point)
    {
        Fixed32 signedDist = SignedDistance(point);
        return signedDist > Fixed32.Zero ? signedDist : Fixed32.Zero;
    }

    // ============================================================
    // Transformation and Combination
    // ============================================================

    /// <summary>
    /// Returns a new sphere with an expanded radius.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingSphere Expand(Fixed32 amount)
    {
        return new Fixed32BoundingSphere(Center, Radius + amount);
    }

    /// <summary>
    /// Returns a new sphere that includes the given point.
    /// </summary>
    public Fixed32BoundingSphere Encapsulate(Fixed32Vec3 point)
    {
        Fixed32Vec3 direction = point - Center;
        Fixed32 dist = direction.Length();

        if (dist <= Radius)
        {
            return this;
        }

        Fixed32 newRadius = (Radius + dist) / 2;
        Fixed32Vec3 newCenter = Center + direction.Normalized() * (newRadius - Radius);
        return new Fixed32BoundingSphere(newCenter, newRadius);
    }

    /// <summary>
    /// Returns a new sphere that includes another sphere.
    /// </summary>
    public Fixed32BoundingSphere Encapsulate(Fixed32BoundingSphere other)
    {
        Fixed32Vec3 direction = other.Center - Center;
        Fixed32 dist = direction.Length();

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
        Fixed32 newRadius = (Radius + dist + other.Radius) / 2;
        Fixed32Vec3 newCenter = Center + direction.Normalized() * (newRadius - Radius);
        return new Fixed32BoundingSphere(newCenter, newRadius);
    }

    /// <summary>
    /// Returns a translated sphere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32BoundingSphere Translate(Fixed32Vec3 offset)
    {
        return new Fixed32BoundingSphere(Center + offset, Radius);
    }

    /// <summary>
    /// Returns a sphere transformed by a matrix (uses maximum scale for radius).
    /// </summary>
    public Fixed32BoundingSphere Transform(Fixed32Mat4x4 matrix)
    {
        Fixed32Vec3 newCenter = matrix.TransformPoint(Center);
        Fixed32Vec3 scale = matrix.Scale;
        Fixed32 maxScale = Fixed32.Max(Fixed32.Max(scale.X, scale.Y), scale.Z);
        return new Fixed32BoundingSphere(newCenter, Radius * maxScale);
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32BoundingSphere a, Fixed32BoundingSphere b)
    {
        return a.Center == b.Center && a.Radius == b.Radius;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32BoundingSphere a, Fixed32BoundingSphere b)
    {
        return !(a == b);
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed32BoundingSphere other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32BoundingSphere other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Center, Radius);
    }

    public override string ToString()
    {
        return $"Fixed32BoundingSphere(Center: {Center}, Radius: {Radius})";
    }
}
