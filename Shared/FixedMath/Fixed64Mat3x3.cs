using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 3x3 matrix using Fixed64 components for deterministic rotation and scale transforms.
/// Elements are stored in row-major order.
/// </summary>
public readonly struct Fixed64Mat3x3 : IEquatable<Fixed64Mat3x3>
{
    // Row 0
    public readonly Fixed64 M00, M01, M02;
    // Row 1
    public readonly Fixed64 M10, M11, M12;
    // Row 2
    public readonly Fixed64 M20, M21, M22;

    public static readonly Fixed64Mat3x3 Identity = new(
        Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero,
        Fixed64.Zero, Fixed64.OneValue, Fixed64.Zero,
        Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
    );

    public static readonly Fixed64Mat3x3 Zero = new(
        Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
        Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
        Fixed64.Zero, Fixed64.Zero, Fixed64.Zero
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Mat3x3(
        Fixed64 m00, Fixed64 m01, Fixed64 m02,
        Fixed64 m10, Fixed64 m11, Fixed64 m12,
        Fixed64 m20, Fixed64 m21, Fixed64 m22)
    {
        M00 = m00; M01 = m01; M02 = m02;
        M10 = m10; M11 = m11; M12 = m12;
        M20 = m20; M21 = m21; M22 = m22;
    }

    /// <summary>
    /// Creates a matrix from three row vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Mat3x3(Fixed64Vec3 row0, Fixed64Vec3 row1, Fixed64Vec3 row2)
    {
        M00 = row0.X; M01 = row0.Y; M02 = row0.Z;
        M10 = row1.X; M11 = row1.Y; M12 = row1.Z;
        M20 = row2.X; M21 = row2.Y; M22 = row2.Z;
    }

    // ============================================================
    // Factory Methods
    // ============================================================

    /// <summary>
    /// Creates a uniform scale matrix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 CreateScale(Fixed64 scale)
    {
        return new Fixed64Mat3x3(
            scale, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, scale, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, scale
        );
    }

    /// <summary>
    /// Creates a non-uniform scale matrix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 CreateScale(Fixed64 scaleX, Fixed64 scaleY, Fixed64 scaleZ)
    {
        return new Fixed64Mat3x3(
            scaleX, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, scaleY, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, scaleZ
        );
    }

    /// <summary>
    /// Creates a scale matrix from a vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 CreateScale(Fixed64Vec3 scale)
    {
        return CreateScale(scale.X, scale.Y, scale.Z);
    }

    /// <summary>
    /// Creates a rotation matrix around the X axis.
    /// </summary>
    public static Fixed64Mat3x3 CreateRotationX(Fixed64 angle)
    {
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Mat3x3(
            Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, cos, -sin,
            Fixed64.Zero, sin, cos
        );
    }

    /// <summary>
    /// Creates a rotation matrix around the Y axis.
    /// </summary>
    public static Fixed64Mat3x3 CreateRotationY(Fixed64 angle)
    {
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Mat3x3(
            cos, Fixed64.Zero, sin,
            Fixed64.Zero, Fixed64.OneValue, Fixed64.Zero,
            -sin, Fixed64.Zero, cos
        );
    }

    /// <summary>
    /// Creates a rotation matrix around the Z axis.
    /// </summary>
    public static Fixed64Mat3x3 CreateRotationZ(Fixed64 angle)
    {
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Mat3x3(
            cos, -sin, Fixed64.Zero,
            sin, cos, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    /// <summary>
    /// Creates a rotation matrix around an arbitrary axis.
    /// </summary>
    public static Fixed64Mat3x3 CreateFromAxisAngle(Fixed64Vec3 axis, Fixed64 angle)
    {
        Fixed64Vec3 n = axis.Normalized();
        Fixed64.SinCosLUT(angle, out Fixed64 s, out Fixed64 c);
        Fixed64 t = Fixed64.OneValue - c;

        return new Fixed64Mat3x3(
            t * n.X * n.X + c, t * n.X * n.Y - s * n.Z, t * n.X * n.Z + s * n.Y,
            t * n.X * n.Y + s * n.Z, t * n.Y * n.Y + c, t * n.Y * n.Z - s * n.X,
            t * n.X * n.Z - s * n.Y, t * n.Y * n.Z + s * n.X, t * n.Z * n.Z + c
        );
    }

    /// <summary>
    /// Creates a rotation matrix from a quaternion.
    /// </summary>
    public static Fixed64Mat3x3 CreateFromQuaternion(Fixed64Quaternion q)
    {
        Fixed64 xx = q.X * q.X;
        Fixed64 yy = q.Y * q.Y;
        Fixed64 zz = q.Z * q.Z;
        Fixed64 xy = q.X * q.Y;
        Fixed64 xz = q.X * q.Z;
        Fixed64 yz = q.Y * q.Z;
        Fixed64 wx = q.W * q.X;
        Fixed64 wy = q.W * q.Y;
        Fixed64 wz = q.W * q.Z;

        return new Fixed64Mat3x3(
            Fixed64.OneValue - 2 * (yy + zz), 2 * (xy - wz), 2 * (xz + wy),
            2 * (xy + wz), Fixed64.OneValue - 2 * (xx + zz), 2 * (yz - wx),
            2 * (xz - wy), 2 * (yz + wx), Fixed64.OneValue - 2 * (xx + yy)
        );
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 operator +(Fixed64Mat3x3 a, Fixed64Mat3x3 b)
    {
        return new Fixed64Mat3x3(
            a.M00 + b.M00, a.M01 + b.M01, a.M02 + b.M02,
            a.M10 + b.M10, a.M11 + b.M11, a.M12 + b.M12,
            a.M20 + b.M20, a.M21 + b.M21, a.M22 + b.M22
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 operator -(Fixed64Mat3x3 a, Fixed64Mat3x3 b)
    {
        return new Fixed64Mat3x3(
            a.M00 - b.M00, a.M01 - b.M01, a.M02 - b.M02,
            a.M10 - b.M10, a.M11 - b.M11, a.M12 - b.M12,
            a.M20 - b.M20, a.M21 - b.M21, a.M22 - b.M22
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 operator -(Fixed64Mat3x3 m)
    {
        return new Fixed64Mat3x3(
            -m.M00, -m.M01, -m.M02,
            -m.M10, -m.M11, -m.M12,
            -m.M20, -m.M21, -m.M22
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 operator *(Fixed64Mat3x3 a, Fixed64Mat3x3 b)
    {
        return new Fixed64Mat3x3(
            a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
            a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
            a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,

            a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
            a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
            a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,

            a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
            a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
            a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 operator *(Fixed64Mat3x3 m, Fixed64 scalar)
    {
        return new Fixed64Mat3x3(
            m.M00 * scalar, m.M01 * scalar, m.M02 * scalar,
            m.M10 * scalar, m.M11 * scalar, m.M12 * scalar,
            m.M20 * scalar, m.M21 * scalar, m.M22 * scalar
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 operator *(Fixed64 scalar, Fixed64Mat3x3 m)
    {
        return m * scalar;
    }

    /// <summary>
    /// Transforms a vector by this matrix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec3 operator *(Fixed64Mat3x3 m, Fixed64Vec3 v)
    {
        return new Fixed64Vec3(
            m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
            m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
            m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64Mat3x3 a, Fixed64Mat3x3 b)
    {
        return a.M00 == b.M00 && a.M01 == b.M01 && a.M02 == b.M02 &&
               a.M10 == b.M10 && a.M11 == b.M11 && a.M12 == b.M12 &&
               a.M20 == b.M20 && a.M21 == b.M21 && a.M22 == b.M22;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64Mat3x3 a, Fixed64Mat3x3 b)
    {
        return !(a == b);
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the transpose of this matrix.
    /// </summary>
    public Fixed64Mat3x3 Transposed => new Fixed64Mat3x3(
        M00, M10, M20,
        M01, M11, M21,
        M02, M12, M22
    );

    /// <summary>
    /// Returns the determinant of this matrix.
    /// </summary>
    public Fixed64 Determinant =>
        M00 * (M11 * M22 - M12 * M21) -
        M01 * (M10 * M22 - M12 * M20) +
        M02 * (M10 * M21 - M11 * M20);

    /// <summary>
    /// Returns the trace of this matrix.
    /// </summary>
    public Fixed64 Trace => M00 + M11 + M22;

    /// <summary>
    /// Gets a row as a vector.
    /// </summary>
    public Fixed64Vec3 GetRow(int index)
    {
        return index switch
        {
            0 => new Fixed64Vec3(M00, M01, M02),
            1 => new Fixed64Vec3(M10, M11, M12),
            2 => new Fixed64Vec3(M20, M21, M22),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    /// <summary>
    /// Gets a column as a vector.
    /// </summary>
    public Fixed64Vec3 GetColumn(int index)
    {
        return index switch
        {
            0 => new Fixed64Vec3(M00, M10, M20),
            1 => new Fixed64Vec3(M01, M11, M21),
            2 => new Fixed64Vec3(M02, M12, M22),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    // ============================================================
    // Methods
    // ============================================================

    /// <summary>
    /// Transforms a vector by this matrix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3 Transform(Fixed64Vec3 v)
    {
        return this * v;
    }

    /// <summary>
    /// Returns the inverse of this matrix, or Identity if singular.
    /// </summary>
    public Fixed64Mat3x3 Inverse()
    {
        Fixed64 det = Determinant;
        if (det.Raw == 0)
        {
            return Identity;
        }

        Fixed64 invDet = Fixed64.OneValue / det;

        return new Fixed64Mat3x3(
            (M11 * M22 - M12 * M21) * invDet,
            (M02 * M21 - M01 * M22) * invDet,
            (M01 * M12 - M02 * M11) * invDet,

            (M12 * M20 - M10 * M22) * invDet,
            (M00 * M22 - M02 * M20) * invDet,
            (M02 * M10 - M00 * M12) * invDet,

            (M10 * M21 - M11 * M20) * invDet,
            (M01 * M20 - M00 * M21) * invDet,
            (M00 * M11 - M01 * M10) * invDet
        );
    }

    /// <summary>
    /// Attempts to invert the matrix.
    /// </summary>
    public bool TryInverse(out Fixed64Mat3x3 result)
    {
        Fixed64 det = Determinant;
        if (det.Raw == 0)
        {
            result = Identity;
            return false;
        }

        result = Inverse();
        return true;
    }

    /// <summary>
    /// Converts to a quaternion (assumes this is a valid rotation matrix).
    /// </summary>
    public Fixed64Quaternion ToQuaternion()
    {
        Fixed64 trace = Trace;

        if (trace > Fixed64.Zero)
        {
            Fixed64 s = Fixed64.Sqrt(trace + Fixed64.OneValue) * 2;
            return new Fixed64Quaternion(
                (M12 - M21) / s,
                (M20 - M02) / s,
                (M01 - M10) / s,
                s / 4
            );
        }
        else if (M00 > M11 && M00 > M22)
        {
            Fixed64 s = Fixed64.Sqrt(Fixed64.OneValue + M00 - M11 - M22) * 2;
            return new Fixed64Quaternion(
                s / 4,
                (M01 + M10) / s,
                (M02 + M20) / s,
                (M12 - M21) / s
            );
        }
        else if (M11 > M22)
        {
            Fixed64 s = Fixed64.Sqrt(Fixed64.OneValue + M11 - M00 - M22) * 2;
            return new Fixed64Quaternion(
                (M01 + M10) / s,
                s / 4,
                (M12 + M21) / s,
                (M20 - M02) / s
            );
        }
        else
        {
            Fixed64 s = Fixed64.Sqrt(Fixed64.OneValue + M22 - M00 - M11) * 2;
            return new Fixed64Quaternion(
                (M02 + M20) / s,
                (M12 + M21) / s,
                s / 4,
                (M01 - M10) / s
            );
        }
    }

    /// <summary>
    /// Linearly interpolates between two matrices.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat3x3 Lerp(Fixed64Mat3x3 a, Fixed64Mat3x3 b, Fixed64 t)
    {
        Fixed64 oneMinusT = Fixed64.OneValue - t;
        return new Fixed64Mat3x3(
            a.M00 * oneMinusT + b.M00 * t, a.M01 * oneMinusT + b.M01 * t, a.M02 * oneMinusT + b.M02 * t,
            a.M10 * oneMinusT + b.M10 * t, a.M11 * oneMinusT + b.M11 * t, a.M12 * oneMinusT + b.M12 * t,
            a.M20 * oneMinusT + b.M20 * t, a.M21 * oneMinusT + b.M21 * t, a.M22 * oneMinusT + b.M22 * t
        );
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed64Mat3x3 other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64Mat3x3 other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(M00); hash.Add(M01); hash.Add(M02);
        hash.Add(M10); hash.Add(M11); hash.Add(M12);
        hash.Add(M20); hash.Add(M21); hash.Add(M22);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return $"[({M00}, {M01}, {M02}), ({M10}, {M11}, {M12}), ({M20}, {M21}, {M22})]";
    }
}
