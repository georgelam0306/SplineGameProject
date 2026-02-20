using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A quaternion using Fixed32 components for deterministic 3D rotations.
/// </summary>
public readonly struct Fixed32Quaternion : IEquatable<Fixed32Quaternion>
{
    public readonly Fixed32 X;
    public readonly Fixed32 Y;
    public readonly Fixed32 Z;
    public readonly Fixed32 W;

    public static readonly Fixed32Quaternion Identity = new(Fixed32.Zero, Fixed32.Zero, Fixed32.Zero, Fixed32.OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Quaternion(Fixed32 x, Fixed32 y, Fixed32 z, Fixed32 w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    // ============================================================
    // Factory Methods
    // ============================================================

    /// <summary>
    /// Creates a quaternion from an axis and angle (in radians).
    /// </summary>
    public static Fixed32Quaternion FromAxisAngle(Fixed32Vec3 axis, Fixed32 angle)
    {
        Fixed32 halfAngle = angle / 2;
        Fixed32.SinCosLUT(halfAngle, out Fixed32 sin, out Fixed32 cos);
        Fixed32Vec3 normalizedAxis = axis.Normalized();

        return new Fixed32Quaternion(
            normalizedAxis.X * sin,
            normalizedAxis.Y * sin,
            normalizedAxis.Z * sin,
            cos
        );
    }

    /// <summary>
    /// Creates a quaternion from Euler angles (in radians, applied in ZYX order).
    /// </summary>
    public static Fixed32Quaternion FromEuler(Fixed32 pitch, Fixed32 yaw, Fixed32 roll)
    {
        Fixed32 halfPitch = pitch / 2;
        Fixed32 halfYaw = yaw / 2;
        Fixed32 halfRoll = roll / 2;

        Fixed32.SinCosLUT(halfPitch, out Fixed32 sp, out Fixed32 cp);
        Fixed32.SinCosLUT(halfYaw, out Fixed32 sy, out Fixed32 cy);
        Fixed32.SinCosLUT(halfRoll, out Fixed32 sr, out Fixed32 cr);

        return new Fixed32Quaternion(
            sr * cp * cy - cr * sp * sy,
            cr * sp * cy + sr * cp * sy,
            cr * cp * sy - sr * sp * cy,
            cr * cp * cy + sr * sp * sy
        );
    }

    /// <summary>
    /// Creates a quaternion from Euler angles vector (in radians).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Quaternion FromEuler(Fixed32Vec3 euler)
    {
        return FromEuler(euler.X, euler.Y, euler.Z);
    }

    /// <summary>
    /// Creates a rotation that looks in the specified direction.
    /// </summary>
    public static Fixed32Quaternion LookRotation(Fixed32Vec3 forward, Fixed32Vec3 up)
    {
        Fixed32Vec3 f = forward.Normalized();
        Fixed32Vec3 r = Fixed32Vec3.Cross(up, f).Normalized();
        Fixed32Vec3 u = Fixed32Vec3.Cross(f, r);

        // Build rotation matrix and convert to quaternion
        Fixed32 m00 = r.X, m01 = r.Y, m02 = r.Z;
        Fixed32 m10 = u.X, m11 = u.Y, m12 = u.Z;
        Fixed32 m20 = f.X, m21 = f.Y, m22 = f.Z;

        Fixed32 trace = m00 + m11 + m22;

        if (trace > Fixed32.Zero)
        {
            Fixed32 s = Fixed32.Sqrt(trace + Fixed32.OneValue) * 2;
            return new Fixed32Quaternion(
                (m12 - m21) / s,
                (m20 - m02) / s,
                (m01 - m10) / s,
                s / 4
            );
        }
        else if (m00 > m11 && m00 > m22)
        {
            Fixed32 s = Fixed32.Sqrt(Fixed32.OneValue + m00 - m11 - m22) * 2;
            return new Fixed32Quaternion(
                s / 4,
                (m01 + m10) / s,
                (m02 + m20) / s,
                (m12 - m21) / s
            );
        }
        else if (m11 > m22)
        {
            Fixed32 s = Fixed32.Sqrt(Fixed32.OneValue + m11 - m00 - m22) * 2;
            return new Fixed32Quaternion(
                (m01 + m10) / s,
                s / 4,
                (m12 + m21) / s,
                (m20 - m02) / s
            );
        }
        else
        {
            Fixed32 s = Fixed32.Sqrt(Fixed32.OneValue + m22 - m00 - m11) * 2;
            return new Fixed32Quaternion(
                (m02 + m20) / s,
                (m12 + m21) / s,
                s / 4,
                (m01 - m10) / s
            );
        }
    }

    /// <summary>
    /// Creates a rotation from one direction to another.
    /// </summary>
    public static Fixed32Quaternion FromToRotation(Fixed32Vec3 fromDirection, Fixed32Vec3 toDirection)
    {
        Fixed32Vec3 from = fromDirection.Normalized();
        Fixed32Vec3 to = toDirection.Normalized();

        Fixed32 dot = Fixed32Vec3.Dot(from, to);

        // Vectors are nearly opposite
        if (dot < Fixed32.FromFloat(-0.999f))
        {
            // Find orthogonal axis
            Fixed32Vec3 axis = Fixed32Vec3.Cross(Fixed32Vec3.UnitX, from);
            if (axis.LengthSquared() < Fixed32.FromFloat(0.001f))
            {
                axis = Fixed32Vec3.Cross(Fixed32Vec3.UnitY, from);
            }
            axis = axis.Normalized();
            return FromAxisAngle(axis, Fixed32.Pi);
        }

        // Vectors are nearly parallel
        if (dot > Fixed32.FromFloat(0.999f))
        {
            return Identity;
        }

        Fixed32Vec3 cross = Fixed32Vec3.Cross(from, to);
        return new Fixed32Quaternion(
            cross.X,
            cross.Y,
            cross.Z,
            Fixed32.OneValue + dot
        ).Normalized();
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Quaternion operator *(Fixed32Quaternion a, Fixed32Quaternion b)
    {
        return new Fixed32Quaternion(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z
        );
    }

    /// <summary>
    /// Rotates a vector by this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Vec3 operator *(Fixed32Quaternion q, Fixed32Vec3 v)
    {
        // Optimized quaternion-vector multiplication
        Fixed32Vec3 qv = new Fixed32Vec3(q.X, q.Y, q.Z);
        Fixed32Vec3 uv = Fixed32Vec3.Cross(qv, v);
        Fixed32Vec3 uuv = Fixed32Vec3.Cross(qv, uv);

        return v + (uv * q.W + uuv) * 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed32Quaternion a, Fixed32Quaternion b)
    {
        return a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed32Quaternion a, Fixed32Quaternion b)
    {
        return !(a == b);
    }

    // ============================================================
    // Properties and Methods
    // ============================================================

    /// <summary>
    /// Returns the conjugate of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Quaternion Conjugate()
    {
        return new Fixed32Quaternion(-X, -Y, -Z, W);
    }

    /// <summary>
    /// Returns the inverse of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Quaternion Inverse()
    {
        Fixed32 lengthSq = X * X + Y * Y + Z * Z + W * W;
        if (lengthSq.Raw == 0)
        {
            return Identity;
        }
        return new Fixed32Quaternion(-X / lengthSq, -Y / lengthSq, -Z / lengthSq, W / lengthSq);
    }

    /// <summary>
    /// Returns the length squared of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 LengthSquared()
    {
        return X * X + Y * Y + Z * Z + W * W;
    }

    /// <summary>
    /// Returns the length of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32 Length()
    {
        return Fixed32.Sqrt(LengthSquared());
    }

    /// <summary>
    /// Returns a normalized version of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed32Quaternion Normalized()
    {
        Fixed32 length = Length();
        if (length.Raw == 0)
        {
            return Identity;
        }
        return new Fixed32Quaternion(X / length, Y / length, Z / length, W / length);
    }

    /// <summary>
    /// Returns the dot product of two quaternions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32 Dot(Fixed32Quaternion a, Fixed32Quaternion b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
    }

    /// <summary>
    /// Returns the angle in radians between two quaternions.
    /// </summary>
    public static Fixed32 Angle(Fixed32Quaternion a, Fixed32Quaternion b)
    {
        Fixed32 dot = Fixed32.Abs(Dot(a, b));
        dot = Fixed32.Min(dot, Fixed32.OneValue);
        return Fixed32.Acos(dot) * 2;
    }

    // ============================================================
    // Conversion Methods
    // ============================================================

    /// <summary>
    /// Converts to Euler angles (in radians).
    /// </summary>
    public Fixed32Vec3 ToEuler()
    {
        // Roll (X-axis rotation)
        Fixed32 sinr_cosp = 2 * (W * X + Y * Z);
        Fixed32 cosr_cosp = Fixed32.OneValue - 2 * (X * X + Y * Y);
        Fixed32 roll = Fixed32.Atan2(sinr_cosp, cosr_cosp);

        // Pitch (Y-axis rotation)
        Fixed32 sinp = 2 * (W * Y - Z * X);
        Fixed32 pitch;
        if (Fixed32.Abs(sinp) >= Fixed32.OneValue)
        {
            pitch = sinp >= Fixed32.Zero ? Fixed32.HalfPi : -Fixed32.HalfPi;
        }
        else
        {
            pitch = Fixed32.Asin(sinp);
        }

        // Yaw (Z-axis rotation)
        Fixed32 siny_cosp = 2 * (W * Z + X * Y);
        Fixed32 cosy_cosp = Fixed32.OneValue - 2 * (Y * Y + Z * Z);
        Fixed32 yaw = Fixed32.Atan2(siny_cosp, cosy_cosp);

        return new Fixed32Vec3(roll, pitch, yaw);
    }

    /// <summary>
    /// Converts to axis-angle representation.
    /// </summary>
    public void ToAxisAngle(out Fixed32Vec3 axis, out Fixed32 angle)
    {
        Fixed32Quaternion q = W < Fixed32.Zero ? new Fixed32Quaternion(-X, -Y, -Z, -W) : this;

        Fixed32 sinHalfAngle = Fixed32.Sqrt(Fixed32.OneValue - q.W * q.W);
        angle = Fixed32.Acos(q.W) * 2;

        if (sinHalfAngle.Raw == 0)
        {
            axis = Fixed32Vec3.UnitX;
        }
        else
        {
            axis = new Fixed32Vec3(q.X / sinHalfAngle, q.Y / sinHalfAngle, q.Z / sinHalfAngle);
        }
    }

    // ============================================================
    // Interpolation
    // ============================================================

    /// <summary>
    /// Linear interpolation between two quaternions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Quaternion Lerp(Fixed32Quaternion a, Fixed32Quaternion b, Fixed32 t)
    {
        return LerpUnclamped(a, b, Fixed32.Clamp(t, Fixed32.Zero, Fixed32.OneValue));
    }

    /// <summary>
    /// Linear interpolation between two quaternions (unclamped).
    /// </summary>
    public static Fixed32Quaternion LerpUnclamped(Fixed32Quaternion a, Fixed32Quaternion b, Fixed32 t)
    {
        // Ensure shortest path
        Fixed32 dot = Dot(a, b);
        Fixed32Quaternion bAdjusted = dot < Fixed32.Zero ? new Fixed32Quaternion(-b.X, -b.Y, -b.Z, -b.W) : b;

        Fixed32 oneMinusT = Fixed32.OneValue - t;
        return new Fixed32Quaternion(
            a.X * oneMinusT + bAdjusted.X * t,
            a.Y * oneMinusT + bAdjusted.Y * t,
            a.Z * oneMinusT + bAdjusted.Z * t,
            a.W * oneMinusT + bAdjusted.W * t
        ).Normalized();
    }

    /// <summary>
    /// Spherical linear interpolation between two quaternions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed32Quaternion Slerp(Fixed32Quaternion a, Fixed32Quaternion b, Fixed32 t)
    {
        return SlerpUnclamped(a, b, Fixed32.Clamp(t, Fixed32.Zero, Fixed32.OneValue));
    }

    /// <summary>
    /// Spherical linear interpolation between two quaternions (unclamped).
    /// </summary>
    public static Fixed32Quaternion SlerpUnclamped(Fixed32Quaternion a, Fixed32Quaternion b, Fixed32 t)
    {
        Fixed32 dot = Dot(a, b);

        // Ensure shortest path
        Fixed32Quaternion bAdjusted = b;
        if (dot < Fixed32.Zero)
        {
            dot = -dot;
            bAdjusted = new Fixed32Quaternion(-b.X, -b.Y, -b.Z, -b.W);
        }

        // Use lerp for nearly identical quaternions
        if (dot > Fixed32.FromFloat(0.9995f))
        {
            return LerpUnclamped(a, bAdjusted, t);
        }

        Fixed32 theta0 = Fixed32.Acos(dot);
        Fixed32 theta = theta0 * t;

        Fixed32.SinCosLUT(theta, out Fixed32 sinTheta, out Fixed32 cosTheta);
        Fixed32 sinTheta0 = Fixed32.Sqrt(Fixed32.OneValue - dot * dot);

        Fixed32 s0 = cosTheta - dot * sinTheta / sinTheta0;
        Fixed32 s1 = sinTheta / sinTheta0;

        return new Fixed32Quaternion(
            a.X * s0 + bAdjusted.X * s1,
            a.Y * s0 + bAdjusted.Y * s1,
            a.Z * s0 + bAdjusted.Z * s1,
            a.W * s0 + bAdjusted.W * s1
        );
    }

    /// <summary>
    /// Rotates a quaternion towards another by a maximum angle delta.
    /// </summary>
    public static Fixed32Quaternion RotateTowards(Fixed32Quaternion from, Fixed32Quaternion to, Fixed32 maxDegreesDelta)
    {
        Fixed32 angle = Angle(from, to);
        if (angle.Raw == 0)
        {
            return to;
        }

        Fixed32 t = Fixed32.Min(Fixed32.OneValue, maxDegreesDelta / angle);
        return SlerpUnclamped(from, to, t);
    }

    // ============================================================
    // Direction Vectors
    // ============================================================

    /// <summary>
    /// Returns the forward direction of this rotation.
    /// </summary>
    public Fixed32Vec3 Forward => this * Fixed32Vec3.Forward;

    /// <summary>
    /// Returns the up direction of this rotation.
    /// </summary>
    public Fixed32Vec3 Up => this * Fixed32Vec3.Up;

    /// <summary>
    /// Returns the right direction of this rotation.
    /// </summary>
    public Fixed32Vec3 Right => this * Fixed32Vec3.Right;

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed32Quaternion other)
    {
        return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed32Quaternion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Z, W);
    }

    public override string ToString()
    {
        return $"({X}, {Y}, {Z}, {W})";
    }

    // ============================================================
    // Conversion
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public System.Numerics.Quaternion ToQuaternion()
    {
        return new System.Numerics.Quaternion(X.ToFloat(), Y.ToFloat(), Z.ToFloat(), W.ToFloat());
    }
}
