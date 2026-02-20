using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A 4x4 matrix using Fixed32 components for deterministic transforms.
/// Elements are stored in row-major order.
/// </summary>
public readonly struct Fixed32Mat4x4 : IEquatable<Fixed32Mat4x4>
{
    // Row 0
    public readonly Fixed32 M00, M01, M02, M03;
    // Row 1
    public readonly Fixed32 M10, M11, M12, M13;
    // Row 2
    public readonly Fixed32 M20, M21, M22, M23;
    // Row 3
    public readonly Fixed32 M30, M31, M32, M33;

    public static readonly Fixed32Mat4x4 Identity = new(
        Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
        Fixed32.Zero, Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero,
        Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue, Fixed32.Zero,
        Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
    );

    public static readonly Fixed32Mat4x4 Zero = new(
        Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
        Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
        Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
        Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Mat4x4(
        Fixed32 m00, Fixed32 m01, Fixed32 m02, Fixed32 m03,
        Fixed32 m10, Fixed32 m11, Fixed32 m12, Fixed32 m13,
        Fixed32 m20, Fixed32 m21, Fixed32 m22, Fixed32 m23,
        Fixed32 m30, Fixed32 m31, Fixed32 m32, Fixed32 m33)
    {
        M00 = m00; M01 = m01; M02 = m02; M03 = m03;
        M10 = m10; M11 = m11; M12 = m12; M13 = m13;
        M20 = m20; M21 = m21; M22 = m22; M23 = m23;
        M30 = m30; M31 = m31; M32 = m32; M33 = m33;
    }

    /// <summary>
    /// Creates a 4x4 matrix from a 3x3 rotation/scale matrix.
    /// </summary>
    public Fixed32Mat4x4(Fixed32Mat3x3 m)
    {
        M00 = m.M00; M01 = m.M01; M02 = m.M02; M03 = Fixed32.Zero;
        M10 = m.M10; M11 = m.M11; M12 = m.M12; M13 = Fixed32.Zero;
        M20 = m.M20; M21 = m.M21; M22 = m.M22; M23 = Fixed32.Zero;
        M30 = Fixed32.Zero; M31 = Fixed32.Zero; M32 = Fixed32.Zero; M33 = Fixed32.OneValue;
    }

    // ============================================================
    // Factory Methods - Translation
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat4x4 CreateTranslation(Fixed32 x, Fixed32 y, Fixed32 z)
    {
        return new Fixed32Mat4x4(
            Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero, x,
            Fixed32.Zero, Fixed32.OneValue, Fixed32.Zero, y,
            Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue, z,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat4x4 CreateTranslation(Fixed32Vec3 position)
    {
        return CreateTranslation(position.X, position.Y, position.Z);
    }

    // ============================================================
    // Factory Methods - Scale
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat4x4 CreateScale(Fixed32 scale)
    {
        return new Fixed32Mat4x4(
            scale, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, scale, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, scale, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat4x4 CreateScale(Fixed32 scaleX, Fixed32 scaleY, Fixed32 scaleZ)
    {
        return new Fixed32Mat4x4(
            scaleX, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, scaleY, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, scaleZ, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat4x4 CreateScale(Fixed32Vec3 scale)
    {
        return CreateScale(scale.X, scale.Y, scale.Z);
    }

    // ============================================================
    // Factory Methods - Rotation
    // ============================================================

    public static Fixed32Mat4x4 CreateRotationX(Fixed32 angle)
    {
        Fixed32.SinCosLUT(angle, out Fixed32 sin, out Fixed32 cos);
        return new Fixed32Mat4x4(
            Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, cos, -sin, Fixed32.Zero,
            Fixed32.Zero, sin, cos, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    public static Fixed32Mat4x4 CreateRotationY(Fixed32 angle)
    {
        Fixed32.SinCosLUT(angle, out Fixed32 sin, out Fixed32 cos);
        return new Fixed32Mat4x4(
            cos, Fixed32.Zero, sin, Fixed32.Zero,
            Fixed32.Zero, Fixed32.OneValue, Fixed32.Zero, Fixed32.Zero,
            -sin, Fixed32.Zero, cos, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    public static Fixed32Mat4x4 CreateRotationZ(Fixed32 angle)
    {
        Fixed32.SinCosLUT(angle, out Fixed32 sin, out Fixed32 cos);
        return new Fixed32Mat4x4(
            cos, -sin, Fixed32.Zero, Fixed32.Zero,
            sin, cos, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    public static Fixed32Mat4x4 CreateFromAxisAngle(Fixed32Vec3 axis, Fixed32 angle)
    {
        return new Fixed32Mat4x4(Fixed32Mat3x3.CreateFromAxisAngle(axis, angle));
    }

    public static Fixed32Mat4x4 CreateFromQuaternion(Fixed32Quaternion q)
    {
        return new Fixed32Mat4x4(Fixed32Mat3x3.CreateFromQuaternion(q));
    }

    // ============================================================
    // Factory Methods - Combined Transforms
    // ============================================================

    /// <summary>
    /// Creates a TRS (Translation, Rotation, Scale) matrix.
    /// </summary>
    public static Fixed32Mat4x4 CreateTRS(Fixed32Vec3 translation, Fixed32Quaternion rotation, Fixed32Vec3 scale)
    {
        Fixed32Mat3x3 rotMatrix = Fixed32Mat3x3.CreateFromQuaternion(rotation);

        return new Fixed32Mat4x4(
            rotMatrix.M00 * scale.X, rotMatrix.M01 * scale.Y, rotMatrix.M02 * scale.Z, translation.X,
            rotMatrix.M10 * scale.X, rotMatrix.M11 * scale.Y, rotMatrix.M12 * scale.Z, translation.Y,
            rotMatrix.M20 * scale.X, rotMatrix.M21 * scale.Y, rotMatrix.M22 * scale.Z, translation.Z,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    // ============================================================
    // Factory Methods - View/Projection
    // ============================================================

    /// <summary>
    /// Creates a look-at view matrix.
    /// </summary>
    public static Fixed32Mat4x4 CreateLookAt(Fixed32Vec3 cameraPosition, Fixed32Vec3 cameraTarget, Fixed32Vec3 cameraUpVector)
    {
        Fixed32Vec3 zAxis = (cameraPosition - cameraTarget).Normalized();
        Fixed32Vec3 xAxis = Fixed32Vec3.Cross(cameraUpVector, zAxis).Normalized();
        Fixed32Vec3 yAxis = Fixed32Vec3.Cross(zAxis, xAxis);

        return new Fixed32Mat4x4(
            xAxis.X, xAxis.Y, xAxis.Z, -Fixed32Vec3.Dot(xAxis, cameraPosition),
            yAxis.X, yAxis.Y, yAxis.Z, -Fixed32Vec3.Dot(yAxis, cameraPosition),
            zAxis.X, zAxis.Y, zAxis.Z, -Fixed32Vec3.Dot(zAxis, cameraPosition),
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    /// <summary>
    /// Creates a perspective projection matrix.
    /// </summary>
    public static Fixed32Mat4x4 CreatePerspective(Fixed32 fovY, Fixed32 aspectRatio, Fixed32 nearPlane, Fixed32 farPlane)
    {
        Fixed32 yScale = Fixed32.OneValue / Fixed32.Tan(fovY / 2);
        Fixed32 xScale = yScale / aspectRatio;
        Fixed32 range = farPlane - nearPlane;

        return new Fixed32Mat4x4(
            xScale, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, yScale, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, -(farPlane + nearPlane) / range, -(2 * farPlane * nearPlane) / range,
            Fixed32.Zero, Fixed32.Zero, -Fixed32.OneValue, Fixed32.Zero
        );
    }

    /// <summary>
    /// Creates an orthographic projection matrix.
    /// </summary>
    public static Fixed32Mat4x4 CreateOrthographic(Fixed32 width, Fixed32 height, Fixed32 nearPlane, Fixed32 farPlane)
    {
        Fixed32 range = farPlane - nearPlane;

        return new Fixed32Mat4x4(
            2 / width, Fixed32.Zero, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, 2 / height, Fixed32.Zero, Fixed32.Zero,
            Fixed32.Zero, Fixed32.Zero, -2 / range, -(farPlane + nearPlane) / range,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    /// <summary>
    /// Creates an orthographic off-center projection matrix.
    /// </summary>
    public static Fixed32Mat4x4 CreateOrthographicOffCenter(Fixed32 left, Fixed32 right, Fixed32 bottom, Fixed32 top, Fixed32 nearPlane, Fixed32 farPlane)
    {
        Fixed32 invWidth = Fixed32.OneValue / (right - left);
        Fixed32 invHeight = Fixed32.OneValue / (top - bottom);
        Fixed32 invDepth = Fixed32.OneValue / (farPlane - nearPlane);

        return new Fixed32Mat4x4(
            2 * invWidth, Fixed32.Zero, Fixed32.Zero, -(right + left) * invWidth,
            Fixed32.Zero, 2 * invHeight, Fixed32.Zero, -(top + bottom) * invHeight,
            Fixed32.Zero, Fixed32.Zero, -2 * invDepth, -(farPlane + nearPlane) * invDepth,
            Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue
        );
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat4x4 operator +(Fixed32Mat4x4 a, Fixed32Mat4x4 b)
    {
        return new Fixed32Mat4x4(
            a.M00 + b.M00, a.M01 + b.M01, a.M02 + b.M02, a.M03 + b.M03,
            a.M10 + b.M10, a.M11 + b.M11, a.M12 + b.M12, a.M13 + b.M13,
            a.M20 + b.M20, a.M21 + b.M21, a.M22 + b.M22, a.M23 + b.M23,
            a.M30 + b.M30, a.M31 + b.M31, a.M32 + b.M32, a.M33 + b.M33
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Mat4x4 operator -(Fixed32Mat4x4 a, Fixed32Mat4x4 b)
    {
        return new Fixed32Mat4x4(
            a.M00 - b.M00, a.M01 - b.M01, a.M02 - b.M02, a.M03 - b.M03,
            a.M10 - b.M10, a.M11 - b.M11, a.M12 - b.M12, a.M13 - b.M13,
            a.M20 - b.M20, a.M21 - b.M21, a.M22 - b.M22, a.M23 - b.M23,
            a.M30 - b.M30, a.M31 - b.M31, a.M32 - b.M32, a.M33 - b.M33
        );
    }

    public static Fixed32Mat4x4 operator *(Fixed32Mat4x4 a, Fixed32Mat4x4 b)
    {
        return new Fixed32Mat4x4(
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
    public static Fixed32Mat4x4 operator *(Fixed32Mat4x4 m, Fixed32 scalar)
    {
        return new Fixed32Mat4x4(
            m.M00 * scalar, m.M01 * scalar, m.M02 * scalar, m.M03 * scalar,
            m.M10 * scalar, m.M11 * scalar, m.M12 * scalar, m.M13 * scalar,
            m.M20 * scalar, m.M21 * scalar, m.M22 * scalar, m.M23 * scalar,
            m.M30 * scalar, m.M31 * scalar, m.M32 * scalar, m.M33 * scalar
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32Mat4x4 a, Fixed32Mat4x4 b)
    {
        return a.M00 == b.M00 && a.M01 == b.M01 && a.M02 == b.M02 && a.M03 == b.M03 &&
               a.M10 == b.M10 && a.M11 == b.M11 && a.M12 == b.M12 && a.M13 == b.M13 &&
               a.M20 == b.M20 && a.M21 == b.M21 && a.M22 == b.M22 && a.M23 == b.M23 &&
               a.M30 == b.M30 && a.M31 == b.M31 && a.M32 == b.M32 && a.M33 == b.M33;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32Mat4x4 a, Fixed32Mat4x4 b)
    {
        return !(a == b);
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// Returns the transpose of this matrix.
    /// </summary>
    public Fixed32Mat4x4 Transposed => new Fixed32Mat4x4(
        M00, M10, M20, M30,
        M01, M11, M21, M31,
        M02, M12, M22, M32,
        M03, M13, M23, M33
    );

    /// <summary>
    /// Gets the translation component of this matrix.
    /// </summary>
    public Fixed32Vec3 Translation => new Fixed32Vec3(M03, M13, M23);

    /// <summary>
    /// Gets the upper-left 3x3 rotation/scale portion of this matrix.
    /// </summary>
    public Fixed32Mat3x3 RotationScale => new Fixed32Mat3x3(
        M00, M01, M02,
        M10, M11, M12,
        M20, M21, M22
    );

    /// <summary>
    /// Gets the scale from this matrix (assuming no shear).
    /// </summary>
    public Fixed32Vec3 Scale => new Fixed32Vec3(
        new Fixed32Vec3(M00, M10, M20).Length(),
        new Fixed32Vec3(M01, M11, M21).Length(),
        new Fixed32Vec3(M02, M12, M22).Length()
    );

    /// <summary>
    /// Returns the determinant of this matrix.
    /// </summary>
    public Fixed32 Determinant
    {
        get
        {
            Fixed32 a = M00, b = M01, c = M02, d = M03;
            Fixed32 e = M10, f = M11, g = M12, h = M13;
            Fixed32 i = M20, j = M21, k = M22, l = M23;
            Fixed32 m = M30, n = M31, o = M32, p = M33;

            Fixed32 kp_lo = k * p - l * o;
            Fixed32 jp_ln = j * p - l * n;
            Fixed32 jo_kn = j * o - k * n;
            Fixed32 ip_lm = i * p - l * m;
            Fixed32 io_km = i * o - k * m;
            Fixed32 in_jm = i * n - j * m;

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
    public Fixed32Vec3 TransformPoint(Fixed32Vec3 point)
    {
        return new Fixed32Vec3(
            M00 * point.X + M01 * point.Y + M02 * point.Z + M03,
            M10 * point.X + M11 * point.Y + M12 * point.Z + M13,
            M20 * point.X + M21 * point.Y + M22 * point.Z + M23
        );
    }

    /// <summary>
    /// Transforms a 3D direction (applies rotation/scale only, no translation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Vec3 TransformDirection(Fixed32Vec3 direction)
    {
        return new Fixed32Vec3(
            M00 * direction.X + M01 * direction.Y + M02 * direction.Z,
            M10 * direction.X + M11 * direction.Y + M12 * direction.Z,
            M20 * direction.X + M21 * direction.Y + M22 * direction.Z
        );
    }

    /// <summary>
    /// Transforms a 3D normal (applies inverse transpose of rotation/scale).
    /// </summary>
    public Fixed32Vec3 TransformNormal(Fixed32Vec3 normal)
    {
        Fixed32Mat3x3 invTranspose = RotationScale.Inverse().Transposed;
        return (invTranspose * normal).Normalized();
    }

    // ============================================================
    // Inverse
    // ============================================================

    /// <summary>
    /// Returns the inverse of this matrix, or Identity if singular.
    /// </summary>
    public Fixed32Mat4x4 Inverse()
    {
        Fixed32 a = M00, b = M01, c = M02, d = M03;
        Fixed32 e = M10, f = M11, g = M12, h = M13;
        Fixed32 i = M20, j = M21, k = M22, l = M23;
        Fixed32 m = M30, n = M31, o = M32, p = M33;

        Fixed32 kp_lo = k * p - l * o;
        Fixed32 jp_ln = j * p - l * n;
        Fixed32 jo_kn = j * o - k * n;
        Fixed32 ip_lm = i * p - l * m;
        Fixed32 io_km = i * o - k * m;
        Fixed32 in_jm = i * n - j * m;

        Fixed32 a11 = f * kp_lo - g * jp_ln + h * jo_kn;
        Fixed32 a12 = -(e * kp_lo - g * ip_lm + h * io_km);
        Fixed32 a13 = e * jp_ln - f * ip_lm + h * in_jm;
        Fixed32 a14 = -(e * jo_kn - f * io_km + g * in_jm);

        Fixed32 det = a * a11 + b * a12 + c * a13 + d * a14;
        if (det.Raw == 0)
        {
            return Identity;
        }

        Fixed32 invDet = Fixed32.OneValue / det;

        Fixed32 gp_ho = g * p - h * o;
        Fixed32 fp_hn = f * p - h * n;
        Fixed32 fo_gn = f * o - g * n;
        Fixed32 ep_hm = e * p - h * m;
        Fixed32 eo_gm = e * o - g * m;
        Fixed32 en_fm = e * n - f * m;
        Fixed32 gl_hk = g * l - h * k;
        Fixed32 fl_hj = f * l - h * j;
        Fixed32 fk_gj = f * k - g * j;
        Fixed32 el_hi = e * l - h * i;
        Fixed32 ek_gi = e * k - g * i;
        Fixed32 ej_fi = e * j - f * i;

        return new Fixed32Mat4x4(
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
    public bool TryInverse(out Fixed32Mat4x4 result)
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

    // ============================================================
    // Decomposition
    // ============================================================

    /// <summary>
    /// Decomposes this matrix into translation, rotation, and scale.
    /// Returns false if decomposition fails (e.g., for matrices with shear).
    /// </summary>
    public bool Decompose(out Fixed32Vec3 translation, out Fixed32Quaternion rotation, out Fixed32Vec3 scale)
    {
        translation = Translation;

        Fixed32Vec3 col0 = new Fixed32Vec3(M00, M10, M20);
        Fixed32Vec3 col1 = new Fixed32Vec3(M01, M11, M21);
        Fixed32Vec3 col2 = new Fixed32Vec3(M02, M12, M22);

        scale = new Fixed32Vec3(col0.Length(), col1.Length(), col2.Length());

        // Check for zero scale
        if (scale.X.Raw == 0 || scale.Y.Raw == 0 || scale.Z.Raw == 0)
        {
            rotation = Fixed32Quaternion.Identity;
            return false;
        }

        // Normalize columns to extract rotation
        Fixed32Mat3x3 rotMatrix = new Fixed32Mat3x3(
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
    public static Fixed32Mat4x4 Lerp(Fixed32Mat4x4 a, Fixed32Mat4x4 b, Fixed32 t)
    {
        Fixed32 oneMinusT = Fixed32.OneValue - t;
        return new Fixed32Mat4x4(
            a.M00 * oneMinusT + b.M00 * t, a.M01 * oneMinusT + b.M01 * t, a.M02 * oneMinusT + b.M02 * t, a.M03 * oneMinusT + b.M03 * t,
            a.M10 * oneMinusT + b.M10 * t, a.M11 * oneMinusT + b.M11 * t, a.M12 * oneMinusT + b.M12 * t, a.M13 * oneMinusT + b.M13 * t,
            a.M20 * oneMinusT + b.M20 * t, a.M21 * oneMinusT + b.M21 * t, a.M22 * oneMinusT + b.M22 * t, a.M23 * oneMinusT + b.M23 * t,
            a.M30 * oneMinusT + b.M30 * t, a.M31 * oneMinusT + b.M31 * t, a.M32 * oneMinusT + b.M32 * t, a.M33 * oneMinusT + b.M33 * t
        );
    }

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed32Mat4x4 other)
    {
        return this == other;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32Mat4x4 other && Equals(other);
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
