using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 4x4 matrix using Fixed64 components for deterministic transforms.
/// Elements are stored in row-major order.
/// </summary>
public readonly struct Fixed64Mat4x4 : IEquatable<Fixed64Mat4x4>
{
    // Row 0
    public readonly Fixed64 M00, M01, M02, M03;
    // Row 1
    public readonly Fixed64 M10, M11, M12, M13;
    // Row 2
    public readonly Fixed64 M20, M21, M22, M23;
    // Row 3
    public readonly Fixed64 M30, M31, M32, M33;

    public static readonly Fixed64Mat4x4 Identity = new(
        Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
        Fixed64.Zero, Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero,
        Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue, Fixed64.Zero,
        Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
    );

    public static readonly Fixed64Mat4x4 Zero = new(
        Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
        Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
        Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
        Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Mat4x4(
        Fixed64 m00, Fixed64 m01, Fixed64 m02, Fixed64 m03,
        Fixed64 m10, Fixed64 m11, Fixed64 m12, Fixed64 m13,
        Fixed64 m20, Fixed64 m21, Fixed64 m22, Fixed64 m23,
        Fixed64 m30, Fixed64 m31, Fixed64 m32, Fixed64 m33)
    {
        M00 = m00; M01 = m01; M02 = m02; M03 = m03;
        M10 = m10; M11 = m11; M12 = m12; M13 = m13;
        M20 = m20; M21 = m21; M22 = m22; M23 = m23;
        M30 = m30; M31 = m31; M32 = m32; M33 = m33;
    }

    /// <summary>
    /// Creates a 4x4 matrix from a 3x3 rotation/scale matrix.
    /// </summary>
    public Fixed64Mat4x4(Fixed64Mat3x3 m)
    {
        M00 = m.M00; M01 = m.M01; M02 = m.M02; M03 = Fixed64.Zero;
        M10 = m.M10; M11 = m.M11; M12 = m.M12; M13 = Fixed64.Zero;
        M20 = m.M20; M21 = m.M21; M22 = m.M22; M23 = Fixed64.Zero;
        M30 = Fixed64.Zero; M31 = Fixed64.Zero; M32 = Fixed64.Zero; M33 = Fixed64.OneValue;
    }

    // ============================================================
    // Factory Methods - Translation
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat4x4 CreateTranslation(Fixed64 x, Fixed64 y, Fixed64 z)
    {
        return new Fixed64Mat4x4(
            Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero, x,
            Fixed64.Zero, Fixed64.OneValue, Fixed64.Zero, y,
            Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue, z,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat4x4 CreateTranslation(Fixed64Vec3 position)
    {
        return CreateTranslation(position.X, position.Y, position.Z);
    }

    // ============================================================
    // Factory Methods - Scale
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat4x4 CreateScale(Fixed64 scale)
    {
        return new Fixed64Mat4x4(
            scale, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, scale, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, scale, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat4x4 CreateScale(Fixed64 scaleX, Fixed64 scaleY, Fixed64 scaleZ)
    {
        return new Fixed64Mat4x4(
            scaleX, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, scaleY, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, scaleZ, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat4x4 CreateScale(Fixed64Vec3 scale)
    {
        return CreateScale(scale.X, scale.Y, scale.Z);
    }

    // ============================================================
    // Factory Methods - Rotation
    // ============================================================

    public static Fixed64Mat4x4 CreateRotationX(Fixed64 angle)
    {
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Mat4x4(
            Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, cos, -sin, Fixed64.Zero,
            Fixed64.Zero, sin, cos, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    public static Fixed64Mat4x4 CreateRotationY(Fixed64 angle)
    {
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Mat4x4(
            cos, Fixed64.Zero, sin, Fixed64.Zero,
            Fixed64.Zero, Fixed64.OneValue, Fixed64.Zero, Fixed64.Zero,
            -sin, Fixed64.Zero, cos, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    public static Fixed64Mat4x4 CreateRotationZ(Fixed64 angle)
    {
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Mat4x4(
            cos, -sin, Fixed64.Zero, Fixed64.Zero,
            sin, cos, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    public static Fixed64Mat4x4 CreateFromAxisAngle(Fixed64Vec3 axis, Fixed64 angle)
    {
        return new Fixed64Mat4x4(Fixed64Mat3x3.CreateFromAxisAngle(axis, angle));
    }

    public static Fixed64Mat4x4 CreateFromQuaternion(Fixed64Quaternion q)
    {
        return new Fixed64Mat4x4(Fixed64Mat3x3.CreateFromQuaternion(q));
    }

    // ============================================================
    // Factory Methods - Combined Transforms
    // ============================================================

    /// <summary>
    /// Creates a TRS (Translation, Rotation, Scale) matrix.
    /// </summary>
    public static Fixed64Mat4x4 CreateTRS(Fixed64Vec3 translation, Fixed64Quaternion rotation, Fixed64Vec3 scale)
    {
        Fixed64Mat3x3 rotMatrix = Fixed64Mat3x3.CreateFromQuaternion(rotation);

        return new Fixed64Mat4x4(
            rotMatrix.M00 * scale.X, rotMatrix.M01 * scale.Y, rotMatrix.M02 * scale.Z, translation.X,
            rotMatrix.M10 * scale.X, rotMatrix.M11 * scale.Y, rotMatrix.M12 * scale.Z, translation.Y,
            rotMatrix.M20 * scale.X, rotMatrix.M21 * scale.Y, rotMatrix.M22 * scale.Z, translation.Z,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    // ============================================================
    // Factory Methods - View/Projection
    // ============================================================

    /// <summary>
    /// Creates a look-at view matrix.
    /// </summary>
    public static Fixed64Mat4x4 CreateLookAt(Fixed64Vec3 cameraPosition, Fixed64Vec3 cameraTarget, Fixed64Vec3 cameraUpVector)
    {
        Fixed64Vec3 zAxis = (cameraPosition - cameraTarget).Normalized();
        Fixed64Vec3 xAxis = Fixed64Vec3.Cross(cameraUpVector, zAxis).Normalized();
        Fixed64Vec3 yAxis = Fixed64Vec3.Cross(zAxis, xAxis);

        return new Fixed64Mat4x4(
            xAxis.X, xAxis.Y, xAxis.Z, -Fixed64Vec3.Dot(xAxis, cameraPosition),
            yAxis.X, yAxis.Y, yAxis.Z, -Fixed64Vec3.Dot(yAxis, cameraPosition),
            zAxis.X, zAxis.Y, zAxis.Z, -Fixed64Vec3.Dot(zAxis, cameraPosition),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    /// <summary>
    /// Creates a perspective projection matrix.
    /// </summary>
    public static Fixed64Mat4x4 CreatePerspective(Fixed64 fovY, Fixed64 aspectRatio, Fixed64 nearPlane, Fixed64 farPlane)
    {
        Fixed64 yScale = Fixed64.OneValue / Fixed64.Tan(fovY / 2);
        Fixed64 xScale = yScale / aspectRatio;
        Fixed64 range = farPlane - nearPlane;

        return new Fixed64Mat4x4(
            xScale, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, yScale, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, -(farPlane + nearPlane) / range, -(2 * farPlane * nearPlane) / range,
            Fixed64.Zero, Fixed64.Zero, -Fixed64.OneValue, Fixed64.Zero
        );
    }

    /// <summary>
    /// Creates an orthographic projection matrix.
    /// </summary>
    public static Fixed64Mat4x4 CreateOrthographic(Fixed64 width, Fixed64 height, Fixed64 nearPlane, Fixed64 farPlane)
    {
        Fixed64 range = farPlane - nearPlane;

        return new Fixed64Mat4x4(
            2 / width, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, 2 / height, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, -2 / range, -(farPlane + nearPlane) / range,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    /// <summary>
    /// Creates an orthographic off-center projection matrix.
    /// </summary>
    public static Fixed64Mat4x4 CreateOrthographicOffCenter(Fixed64 left, Fixed64 right, Fixed64 bottom, Fixed64 top, Fixed64 nearPlane, Fixed64 farPlane)
    {
        Fixed64 invWidth = Fixed64.OneValue / (right - left);
        Fixed64 invHeight = Fixed64.OneValue / (top - bottom);
        Fixed64 invDepth = Fixed64.OneValue / (farPlane - nearPlane);

        return new Fixed64Mat4x4(
            2 * invWidth, Fixed64.Zero, Fixed64.Zero, -(right + left) * invWidth,
            Fixed64.Zero, 2 * invHeight, Fixed64.Zero, -(top + bottom) * invHeight,
            Fixed64.Zero, Fixed64.Zero, -2 * invDepth, -(farPlane + nearPlane) * invDepth,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue
        );
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat4x4 operator +(Fixed64Mat4x4 a, Fixed64Mat4x4 b)
    {
        return new Fixed64Mat4x4(
            a.M00 + b.M00, a.M01 + b.M01, a.M02 + b.M02, a.M03 + b.M03,
            a.M10 + b.M10, a.M11 + b.M11, a.M12 + b.M12, a.M13 + b.M13,
            a.M20 + b.M20, a.M21 + b.M21, a.M22 + b.M22, a.M23 + b.M23,
            a.M30 + b.M30, a.M31 + b.M31, a.M32 + b.M32, a.M33 + b.M33
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat4x4 operator -(Fixed64Mat4x4 a, Fixed64Mat4x4 b)
    {
        return new Fixed64Mat4x4(
            a.M00 - b.M00, a.M01 - b.M01, a.M02 - b.M02, a.M03 - b.M03,
            a.M10 - b.M10, a.M11 - b.M11, a.M12 - b.M12, a.M13 - b.M13,
            a.M20 - b.M20, a.M21 - b.M21, a.M22 - b.M22, a.M23 - b.M23,
            a.M30 - b.M30, a.M31 - b.M31, a.M32 - b.M32, a.M33 - b.M33
        );
    }

    public static Fixed64Mat4x4 operator *(Fixed64Mat4x4 a, Fixed64Mat4x4 b)
    {
        return new Fixed64Mat4x4(
            a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20 + a.M03 * b.M30,
            a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21 + a.M03 * b.M31,
            a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22 + a.M03 * b.M32,
            a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03 * b.M33,

            a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20 + a.M13 * b.M30,
            a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
            a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
            a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,

            a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20 + a.M23 * b.M30,
            a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
            a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
            a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,

            a.M30 * b.M00 + a.M31 * b.M10 + a.M32 * b.M20 + a.M33 * b.M30,
            a.M30 * b.M01 + a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
            a.M30 * b.M02 + a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
            a.M30 * b.M03 + a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Mat4x4 operator *(Fixed64Mat4x4 m, Fixed64 scalar)
    {
        return new Fixed64Mat4x4(
            m.M00 * scalar, m.M01 * scalar, m.M02 * scalar, m.M03 * scalar,
            m.M10 * scalar, m.M11 * scalar, m.M12 * scalar, m.M13 * scalar,
            m.M20 * scalar, m.M21 * scalar, m.M22 * scalar, m.M23 * scalar,
            m.M30 * scalar, m.M31 * scalar, m.M32 * scalar, m.M33 * scalar
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64Mat4x4 a, Fixed64Mat4x4 b)
    {
        return a.M00 == b.M00 && a.M01 == b.M01 && a.M02 == b.M02 && a.M03 == b.M03 &&
               a.M10 == b.M10 && a.M11 == b.M11 && a.M12 == b.M12 && a.M13 == b.M13 &&
               a.M20 == b.M20 && a.M21 == b.M21 && a.M22 == b.M22 && a.M23 == b.M23 &&
               a.M30 == b.M30 && a.M31 == b.M31 && a.M32 == b.M32 && a.M33 == b.M33;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64Mat4x4 a, Fixed64Mat4x4 b)
    {
        return !(a == b);
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the transpose of this matrix.
    /// </summary>
    public Fixed64Mat4x4 Transposed => new Fixed64Mat4x4(
        M00, M10, M20, M30,
        M01, M11, M21, M31,
        M02, M12, M22, M32,
        M03, M13, M23, M33
    );

    /// <summary>
    /// Gets the translation component of this matrix.
    /// </summary>
    public Fixed64Vec3 Translation => new Fixed64Vec3(M03, M13, M23);

    /// <summary>
    /// Gets the upper-left 3x3 rotation/scale portion of this matrix.
    /// </summary>
    public Fixed64Mat3x3 RotationScale => new Fixed64Mat3x3(
        M00, M01, M02,
        M10, M11, M12,
        M20, M21, M22
    );

    /// <summary>
    /// Gets the scale from this matrix (assuming no shear).
    /// </summary>
    public Fixed64Vec3 Scale => new Fixed64Vec3(
        new Fixed64Vec3(M00, M10, M20).Length(),
        new Fixed64Vec3(M01, M11, M21).Length(),
        new Fixed64Vec3(M02, M12, M22).Length()
    );

    /// <summary>
    /// Returns the determinant of this matrix.
    /// </summary>
    public Fixed64 Determinant
    {
        get
        {
            Fixed64 a = M00, b = M01, c = M02, d = M03;
            Fixed64 e = M10, f = M11, g = M12, h = M13;
            Fixed64 i = M20, j = M21, k = M22, l = M23;
            Fixed64 m = M30, n = M31, o = M32, p = M33;

            Fixed64 kp_lo = k * p - l * o;
            Fixed64 jp_ln = j * p - l * n;
            Fixed64 jo_kn = j * o - k * n;
            Fixed64 ip_lm = i * p - l * m;
            Fixed64 io_km = i * o - k * m;
            Fixed64 in_jm = i * n - j * m;

            return a * (f * kp_lo - g * jp_ln + h * jo_kn) -
                   b * (e * kp_lo - g * ip_lm + h * io_km) +
                   c * (e * jp_ln - f * ip_lm + h * in_jm) -
                   d * (e * jo_kn - f * io_km + g * in_jm);
        }
    }

    // ============================================================
    // Transform Methods
    // ============================================================

    /// <summary>
    /// Transforms a 3D point (applies full transform including translation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3 TransformPoint(Fixed64Vec3 point)
    {
        return new Fixed64Vec3(
            M00 * point.X + M01 * point.Y + M02 * point.Z + M03,
            M10 * point.X + M11 * point.Y + M12 * point.Z + M13,
            M20 * point.X + M21 * point.Y + M22 * point.Z + M23
        );
    }

    /// <summary>
    /// Transforms a 3D direction (applies rotation/scale only, no translation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Vec3 TransformDirection(Fixed64Vec3 direction)
    {
        return new Fixed64Vec3(
            M00 * direction.X + M01 * direction.Y + M02 * direction.Z,
            M10 * direction.X + M11 * direction.Y + M12 * direction.Z,
            M20 * direction.X + M21 * direction.Y + M22 * direction.Z
        );
    }

    /// <summary>
    /// Transforms a 3D normal (applies inverse transpose of rotation/scale).
    /// </summary>
    public Fixed64Vec3 TransformNormal(Fixed64Vec3 normal)
    {
        Fixed64Mat3x3 invTranspose = RotationScale.Inverse().Transposed;
        return (invTranspose * normal).Normalized();
    }

    // ============================================================
    // Inverse
    // ============================================================

    /// <summary>
    /// Returns the inverse of this matrix, or Identity if singular.
    /// </summary>
    public Fixed64Mat4x4 Inverse()
    {
        Fixed64 a = M00, b = M01, c = M02, d = M03;
        Fixed64 e = M10, f = M11, g = M12, h = M13;
        Fixed64 i = M20, j = M21, k = M22, l = M23;
        Fixed64 m = M30, n = M31, o = M32, p = M33;

        Fixed64 kp_lo = k * p - l * o;
        Fixed64 jp_ln = j * p - l * n;
        Fixed64 jo_kn = j * o - k * n;
        Fixed64 ip_lm = i * p - l * m;
        Fixed64 io_km = i * o - k * m;
        Fixed64 in_jm = i * n - j * m;

        Fixed64 a11 = f * kp_lo - g * jp_ln + h * jo_kn;
        Fixed64 a12 = -(e * kp_lo - g * ip_lm + h * io_km);
        Fixed64 a13 = e * jp_ln - f * ip_lm + h * in_jm;
        Fixed64 a14 = -(e * jo_kn - f * io_km + g * in_jm);

        Fixed64 det = a * a11 + b * a12 + c * a13 + d * a14;
        if (det.Raw == 0)
        {
            return Identity;
        }

        Fixed64 invDet = Fixed64.OneValue / det;

        Fixed64 gp_ho = g * p - h * o;
        Fixed64 fp_hn = f * p - h * n;
        Fixed64 fo_gn = f * o - g * n;
        Fixed64 ep_hm = e * p - h * m;
        Fixed64 eo_gm = e * o - g * m;
        Fixed64 en_fm = e * n - f * m;
        Fixed64 gl_hk = g * l - h * k;
        Fixed64 fl_hj = f * l - h * j;
        Fixed64 fk_gj = f * k - g * j;
        Fixed64 el_hi = e * l - h * i;
        Fixed64 ek_gi = e * k - g * i;
        Fixed64 ej_fi = e * j - f * i;

        return new Fixed64Mat4x4(
            a11 * invDet,
            -(b * kp_lo - c * jp_ln + d * jo_kn) * invDet,
            (b * gp_ho - c * fp_hn + d * fo_gn) * invDet,
            -(b * gl_hk - c * fl_hj + d * fk_gj) * invDet,

            a12 * invDet,
            (a * kp_lo - c * ip_lm + d * io_km) * invDet,
            -(a * gp_ho - c * ep_hm + d * eo_gm) * invDet,
            (a * gl_hk - c * el_hi + d * ek_gi) * invDet,

            a13 * invDet,
            -(a * jp_ln - b * ip_lm + d * in_jm) * invDet,
            (a * fp_hn - b * ep_hm + d * en_fm) * invDet,
            -(a * fl_hj - b * el_hi + d * ej_fi) * invDet,

            a14 * invDet,
            (a * jo_kn - b * io_km + c * in_jm) * invDet,
            -(a * fo_gn - b * eo_gm + c * en_fm) * invDet,
            (a * fk_gj - b * ek_gi + c * ej_fi) * invDet
        );
    }

    /// <summary>
    /// Attempts to invert the matrix.
    /// </summary>
    public bool TryInverse(out Fixed64Mat4x4 result)
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

    // ============================================================
    // Decomposition
    // ============================================================

    /// <summary>
    /// Decomposes this matrix into translation, rotation, and scale.
    /// Returns false if decomposition fails (e.g., for matrices with shear).
    /// </summary>
    public bool Decompose(out Fixed64Vec3 translation, out Fixed64Quaternion rotation, out Fixed64Vec3 scale)
    {
        translation = Translation;

        Fixed64Vec3 col0 = new Fixed64Vec3(M00, M10, M20);
        Fixed64Vec3 col1 = new Fixed64Vec3(M01, M11, M21);
        Fixed64Vec3 col2 = new Fixed64Vec3(M02, M12, M22);

        scale = new Fixed64Vec3(col0.Length(), col1.Length(), col2.Length());

        // Check for zero scale
        if (scale.X.Raw == 0 || scale.Y.Raw == 0 || scale.Z.Raw == 0)
        {
            rotation = Fixed64Quaternion.Identity;
            return false;
        }

        // Normalize columns to extract rotation
        Fixed64Mat3x3 rotMatrix = new Fixed64Mat3x3(
            col0.X / scale.X, col1.X / scale.Y, col2.X / scale.Z,
            col0.Y / scale.X, col1.Y / scale.Y, col2.Y / scale.Z,
            col0.Z / scale.X, col1.Z / scale.Y, col2.Z / scale.Z
        );

        rotation = rotMatrix.ToQuaternion();
        return true;
    }

    // ============================================================
    // Interpolation
    // ============================================================

    /// <summary>
    /// Linearly interpolates between two matrices.
    /// </summary>
    public static Fixed64Mat4x4 Lerp(Fixed64Mat4x4 a, Fixed64Mat4x4 b, Fixed64 t)
    {
        Fixed64 oneMinusT = Fixed64.OneValue - t;
        return new Fixed64Mat4x4(
            a.M00 * oneMinusT + b.M00 * t, a.M01 * oneMinusT + b.M01 * t, a.M02 * oneMinusT + b.M02 * t, a.M03 * oneMinusT + b.M03 * t,
            a.M10 * oneMinusT + b.M10 * t, a.M11 * oneMinusT + b.M11 * t, a.M12 * oneMinusT + b.M12 * t, a.M13 * oneMinusT + b.M13 * t,
            a.M20 * oneMinusT + b.M20 * t, a.M21 * oneMinusT + b.M21 * t, a.M22 * oneMinusT + b.M22 * t, a.M23 * oneMinusT + b.M23 * t,
            a.M30 * oneMinusT + b.M30 * t, a.M31 * oneMinusT + b.M31 * t, a.M32 * oneMinusT + b.M32 * t, a.M33 * oneMinusT + b.M33 * t
        );
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed64Mat4x4 other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64Mat4x4 other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(M00); hash.Add(M01); hash.Add(M02); hash.Add(M03);
        hash.Add(M10); hash.Add(M11); hash.Add(M12); hash.Add(M13);
        hash.Add(M20); hash.Add(M21); hash.Add(M22); hash.Add(M23);
        hash.Add(M30); hash.Add(M31); hash.Add(M32); hash.Add(M33);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return $"[({M00}, {M01}, {M02}, {M03}), ({M10}, {M11}, {M12}, {M13}), ({M20}, {M21}, {M22}, {M23}), ({M30}, {M31}, {M32}, {M33})]";
    }

    // ============================================================
    // Conversion
    // ============================================================

    public System.Numerics.Matrix4x4 ToMatrix4x4()
    {
        return new System.Numerics.Matrix4x4(
            M00.ToFloat(), M01.ToFloat(), M02.ToFloat(), M03.ToFloat(),
            M10.ToFloat(), M11.ToFloat(), M12.ToFloat(), M13.ToFloat(),
            M20.ToFloat(), M21.ToFloat(), M22.ToFloat(), M23.ToFloat(),
            M30.ToFloat(), M31.ToFloat(), M32.ToFloat(), M33.ToFloat()
        );
    }
}
