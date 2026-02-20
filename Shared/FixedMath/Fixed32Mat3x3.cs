using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 3x3 matrix using Fixed32 components for deterministic rotation and scale transforms.
/// Elements are stored in row-major order.
/// </summary>
public readonly struct Fixed32Mat3x3 : IEquatable<Fixed32Mat3x3>
{
    // Row 0
    public readonly Fixed32 M00, M01, M02;
    // Row 1
    public readonly Fixed32 M10, M11, M12;
    // Row 2
    public readonly Fixed32 M20, M21, M22;

    public static readonly Fixed32Mat3x3 Identity = new(
        Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero,
        Fixed32.Zero, Fixed32.OneValue, Fixed32.Zero,
        Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
    );

    public static readonly Fixed32Mat3x3 Zero = new(
        Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
        Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
        Fixed32.Zero, Fixed32.Zero, Fixed32.Zero
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Mat3x3(
        Fixed32 m00, Fixed32 m01, Fixed32 m02,
        Fixed32 m10, Fixed32 m11, Fixed32 m12,
        Fixed32 m20, Fixed32 m21, Fixed32 m22)
    {
        M00 = m00; M01 = m01; M02 = m02;
        M10 = m10; M11 = m11; M12 = m12;
        M20 = m20; M21 = m21; M22 = m22;
    }

    /// <summary>
    /// Creates a matrix from three row vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Mat3x3(Fixed32Vec3 row0, Fixed32Vec3 row1, Fixed32Vec3 row2)
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
    public static Fixed32Mat3x3 CreateScale(Fixed32 scale)
    {
        return new Fixed32Mat3x3(
            scale, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, scale, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, scale
        );
    }

    /// <summary>
    /// Creates a non-uniform scale matrix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat3x3 CreateScale(Fixed32 scaleX, Fixed32 scaleY, Fixed32 scaleZ)
    {
        return new Fixed32Mat3x3(
            scaleX, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, scaleY, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, scaleZ
        );
    }

    /// <summary>
    /// Creates a scale matrix from a vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat3x3 CreateScale(Fixed32Vec3 scale)
    {
        return CreateScale(scale.X, scale.Y, scale.Z);
    }

    /// <summary>
    /// Creates a rotation matrix around the X axis.
    /// </summary>
    public static Fixed32Mat3x3 CreateRotationX(Fixed32 angle)
    {
        Fixed32.SinCosLUT(angle, out Fixed32 sin, out Fixed32 cos);
        return new Fixed32Mat3x3(
            Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, cos, -sin,
            Fixed32.Zero, sin, cos
        );
    }

    /// <summary>
    /// Creates a rotation matrix around the Y axis.
    /// </summary>
    public static Fixed32Mat3x3 CreateRotationY(Fixed32 angle)
    {
        Fixed32.SinCosLUT(angle, out Fixed32 sin, out Fixed32 cos);
        return new Fixed32Mat3x3(
            cos, Fixed32.Zero, sin,
            Fixed32.Zero, Fixed32.OneValue, Fixed32.Zero,
            -sin, Fixed32.Zero, cos
        );
    }

    /// <summary>
    /// Creates a rotation matrix around the Z axis.
    /// </summary>
    public static Fixed32Mat3x3 CreateRotationZ(Fixed32 angle)
    {
        Fixed32.SinCosLUT(angle, out Fixed32 sin, out Fixed32 cos);
        return new Fixed32Mat3x3(
            cos, -sin, Fixed32.Zero,
            sin, cos, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    /// <summary>
    /// Creates a rotation matrix around an arbitrary axis.
    /// </summary>
    public static Fixed32Mat3x3 CreateFromAxisAngle(Fixed32Vec3 axis, Fixed32 angle)
    {
        Fixed32Vec3 n = axis.Normalized();
        Fixed32.SinCosLUT(angle, out Fixed32 s, out Fixed32 c);
        Fixed32 t = Fixed32.OneValue - c;

        return new Fixed32Mat3x3(
            t * n.X * n.X + c, t * n.X * n.Y - s * n.Z, t * n.X * n.Z + s * n.Y,
            t * n.X * n.Y + s * n.Z, t * n.Y * n.Y + c, t * n.Y * n.Z - s * n.X,
            t * n.X * n.Z - s * n.Y, t * n.Y * n.Z + s * n.X, t * n.Z * n.Z + c
        );
    }

    /// <summary>
    /// Creates a rotation matrix from a quaternion.
    /// </summary>
    public static Fixed32Mat3x3 CreateFromQuaternion(Fixed32Quaternion q)
    {
        Fixed32 xx = q.X * q.X;
        Fixed32 yy = q.Y * q.Y;
        Fixed32 zz = q.Z * q.Z;
        Fixed32 xy = q.X * q.Y;
        Fixed32 xz = q.X * q.Z;
        Fixed32 yz = q.Y * q.Z;
        Fixed32 wx = q.W * q.X;
        Fixed32 wy = q.W * q.Y;
        Fixed32 wz = q.W * q.Z;

        return new Fixed32Mat3x3(
            Fixed32.OneValue - 2 * (yy + zz), 2 * (xy - wz), 2 * (xz + wy),
            2 * (xy + wz), Fixed32.OneValue - 2 * (xx + zz), 2 * (yz - wx),
            2 * (xz - wy), 2 * (yz + wx), Fixed32.OneValue - 2 * (xx + yy)
        );
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat3x3 operator +(Fixed32Mat3x3 a, Fixed32Mat3x3 b)
    {
        return new Fixed32Mat3x3(
            a.M00 + b.M00, a.M01 + b.M01, a.M02 + b.M02,
            a.M10 + b.M10, a.M11 + b.M11, a.M12 + b.M12,
            a.M20 + b.M20, a.M21 + b.M21, a.M22 + b.M22
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat3x3 operator -(Fixed32Mat3x3 a, Fixed32Mat3x3 b)
    {
        return new Fixed32Mat3x3(
            a.M00 - b.M00, a.M01 - b.M01, a.M02 - b.M02,
            a.M10 - b.M10, a.M11 - b.M11, a.M12 - b.M12,
            a.M20 - b.M20, a.M21 - b.M21, a.M22 - b.M22
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat3x3 operator -(Fixed32Mat3x3 m)
    {
        return new Fixed32Mat3x3(
            -m.M00, -m.M01, -m.M02,
            -m.M10, -m.M11, -m.M12,
            -m.M20, -m.M21, -m.M22
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat3x3 operator *(Fixed32Mat3x3 a, Fixed32Mat3x3 b)
    {
        return new Fixed32Mat3x3(
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
    public static Fixed32Mat3x3 operator *(Fixed32Mat3x3 m, Fixed32 scalar)
    {
        return new Fixed32Mat3x3(
            m.M00 * scalar, m.M01 * scalar, m.M02 * scalar,
            m.M10 * scalar, m.M11 * scalar, m.M12 * scalar,
            m.M20 * scalar, m.M21 * scalar, m.M22 * scalar
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat3x3 operator *(Fixed32 scalar, Fixed32Mat3x3 m)
    {
        return m * scalar;
    }

    /// <summary>
    /// Transforms a vector by this matrix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator *(Fixed32Mat3x3 m, Fixed32Vec3 v)
    {
        return new Fixed32Vec3(
            m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
            m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
            m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32Mat3x3 a, Fixed32Mat3x3 b)
    {
        return a.M00 == b.M00 && a.M01 == b.M01 && a.M02 == b.M02 &&
               a.M10 == b.M10 && a.M11 == b.M11 && a.M12 == b.M12 &&
               a.M20 == b.M20 && a.M21 == b.M21 && a.M22 == b.M22;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32Mat3x3 a, Fixed32Mat3x3 b)
    {
        return !(a == b);
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the transpose of this matrix.
    /// </summary>
    public Fixed32Mat3x3 Transposed => new Fixed32Mat3x3(
        M00, M10, M20,
        M01, M11, M21,
        M02, M12, M22
    );

    /// <summary>
    /// Returns the determinant of this matrix.
    /// </summary>
    public Fixed32 Determinant =>
        M00 * (M11 * M22 - M12 * M21) -
        M01 * (M10 * M22 - M12 * M20) +
        M02 * (M10 * M21 - M11 * M20);

    /// <summary>
    /// Returns the trace of this matrix.
    /// </summary>
    public Fixed32 Trace => M00 + M11 + M22;

    /// <summary>
    /// Gets a row as a vector.
    /// </summary>
    public Fixed32Vec3 GetRow(int index)
    {
        return index switch
        {
            0 => new Fixed32Vec3(M00, M01, M02),
            1 => new Fixed32Vec3(M10, M11, M12),
            2 => new Fixed32Vec3(M20, M21, M22),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    /// <summary>
    /// Gets a column as a vector.
    /// </summary>
    public Fixed32Vec3 GetColumn(int index)
    {
        return index switch
        {
            0 => new Fixed32Vec3(M00, M10, M20),
            1 => new Fixed32Vec3(M01, M11, M21),
            2 => new Fixed32Vec3(M02, M12, M22),
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
    public Fixed32Vec3 Transform(Fixed32Vec3 v)
    {
        return this * v;
    }

    /// <summary>
    /// Returns the inverse of this matrix, or Identity if singular.
    /// </summary>
    public Fixed32Mat3x3 Inverse()
    {
        Fixed32 det = Determinant;
        if (det.Raw == 0)
        {
            return Identity;
        }

        Fixed32 invDet = Fixed32.OneValue / det;

        return new Fixed32Mat3x3(
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
    public bool TryInverse(out Fixed32Mat3x3 result)
    {
        Fixed32 det = Determinant;
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
    public Fixed32Quaternion ToQuaternion()
    {
        Fixed32 trace = Trace;

        if (trace > Fixed32.Zero)
        {
            Fixed32 s = Fixed32.Sqrt(trace + Fixed32.OneValue) * 2;
            return new Fixed32Quaternion(
                (M12 - M21) / s,
                (M20 - M02) / s,
                (M01 - M10) / s,
                s / 4
            );
        }
        else if (M00 > M11 && M00 > M22)
        {
            Fixed32 s = Fixed32.Sqrt(Fixed32.OneValue + M00 - M11 - M22) * 2;
            return new Fixed32Quaternion(
                s / 4,
                (M01 + M10) / s,
                (M02 + M20) / s,
                (M12 - M21) / s
            );
        }
        else if (M11 > M22)
        {
            Fixed32 s = Fixed32.Sqrt(Fixed32.OneValue + M11 - M00 - M22) * 2;
            return new Fixed32Quaternion(
                (M01 + M10) / s,
                s / 4,
                (M12 + M21) / s,
                (M20 - M02) / s
            );
        }
        else
        {
            Fixed32 s = Fixed32.Sqrt(Fixed32.OneValue + M22 - M00 - M11) * 2;
            return new Fixed32Quaternion(
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
    public static Fixed32Mat3x3 Lerp(Fixed32Mat3x3 a, Fixed32Mat3x3 b, Fixed32 t)
    {
        Fixed32 oneMinusT = Fixed32.OneValue - t;
        return new Fixed32Mat3x3(
            a.M00 * oneMinusT + b.M00 * t, a.M01 * oneMinusT + b.M01 * t, a.M02 * oneMinusT + b.M02 * t,
            a.M10 * oneMinusT + b.M10 * t, a.M11 * oneMinusT + b.M11 * t, a.M12 * oneMinusT + b.M12 * t,
            a.M20 * oneMinusT + b.M20 * t, a.M21 * oneMinusT + b.M21 * t, a.M22 * oneMinusT + b.M22 * t
        );
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed32Mat3x3 other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32Mat3x3 other && Equals(other);
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
