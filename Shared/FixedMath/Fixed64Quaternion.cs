using System;
using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// A quaternion using Fixed64 components for deterministic 3D rotations.
/// </summary>
public readonly struct Fixed64Quaternion : IEquatable<Fixed64Quaternion>
{
    public readonly Fixed64 X;
    public readonly Fixed64 Y;
    public readonly Fixed64 Z;
    public readonly Fixed64 W;

    public static readonly Fixed64Quaternion Identity = new(Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.OneValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Quaternion(Fixed64 x, Fixed64 y, Fixed64 z, Fixed64 w)
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
    public static Fixed64Quaternion FromAxisAngle(Fixed64Vec3 axis, Fixed64 angle)
    {
        Fixed64 halfAngle = angle / 2;
        Fixed64.SinCosLUT(halfAngle, out Fixed64 sin, out Fixed64 cos);
        Fixed64Vec3 normalizedAxis = axis.Normalized();

        return new Fixed64Quaternion(
            normalizedAxis.X * sin,
            normalizedAxis.Y * sin,
            normalizedAxis.Z * sin,
            cos
        );
    }

    /// <summary>
    /// Creates a quaternion from Euler angles (in radians, applied in ZYX order).
    /// </summary>
    public static Fixed64Quaternion FromEuler(Fixed64 pitch, Fixed64 yaw, Fixed64 roll)
    {
        Fixed64 halfPitch = pitch / 2;
        Fixed64 halfYaw = yaw / 2;
        Fixed64 halfRoll = roll / 2;

        Fixed64.SinCosLUT(halfPitch, out Fixed64 sp, out Fixed64 cp);
        Fixed64.SinCosLUT(halfYaw, out Fixed64 sy, out Fixed64 cy);
        Fixed64.SinCosLUT(halfRoll, out Fixed64 sr, out Fixed64 cr);

        return new Fixed64Quaternion(
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
    public static Fixed64Quaternion FromEuler(Fixed64Vec3 euler)
    {
        return FromEuler(euler.X, euler.Y, euler.Z);
    }

    /// <summary>
    /// Creates a rotation that looks in the specified direction.
    /// </summary>
    public static Fixed64Quaternion LookRotation(Fixed64Vec3 forward, Fixed64Vec3 up)
    {
        Fixed64Vec3 f = forward.Normalized();
        Fixed64Vec3 r = Fixed64Vec3.Cross(up, f).Normalized();
        Fixed64Vec3 u = Fixed64Vec3.Cross(f, r);

        // Build rotation matrix and convert to quaternion
        Fixed64 m00 = r.X, m01 = r.Y, m02 = r.Z;
        Fixed64 m10 = u.X, m11 = u.Y, m12 = u.Z;
        Fixed64 m20 = f.X, m21 = f.Y, m22 = f.Z;

        Fixed64 trace = m00 + m11 + m22;

        if (trace > Fixed64.Zero)
        {
            Fixed64 s = Fixed64.Sqrt(trace + Fixed64.OneValue) * 2;
            return new Fixed64Quaternion(
                (m12 - m21) / s,
                (m20 - m02) / s,
                (m01 - m10) / s,
                s / 4
            );
        }
        else if (m00 > m11 && m00 > m22)
        {
            Fixed64 s = Fixed64.Sqrt(Fixed64.OneValue + m00 - m11 - m22) * 2;
            return new Fixed64Quaternion(
                s / 4,
                (m01 + m10) / s,
                (m02 + m20) / s,
                (m12 - m21) / s
            );
        }
        else if (m11 > m22)
        {
            Fixed64 s = Fixed64.Sqrt(Fixed64.OneValue + m11 - m00 - m22) * 2;
            return new Fixed64Quaternion(
                (m01 + m10) / s,
                s / 4,
                (m12 + m21) / s,
                (m20 - m02) / s
            );
        }
        else
        {
            Fixed64 s = Fixed64.Sqrt(Fixed64.OneValue + m22 - m00 - m11) * 2;
            return new Fixed64Quaternion(
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
    public static Fixed64Quaternion FromToRotation(Fixed64Vec3 fromDirection, Fixed64Vec3 toDirection)
    {
        Fixed64Vec3 from = fromDirection.Normalized();
        Fixed64Vec3 to = toDirection.Normalized();

        Fixed64 dot = Fixed64Vec3.Dot(from, to);

        // Vectors are nearly opposite
        if (dot < Fixed64.FromFloat(-0.999f))
        {
            // Find orthogonal axis
            Fixed64Vec3 axis = Fixed64Vec3.Cross(Fixed64Vec3.UnitX, from);
            if (axis.LengthSquared() < Fixed64.FromFloat(0.001f))
            {
                axis = Fixed64Vec3.Cross(Fixed64Vec3.UnitY, from);
            }
            axis = axis.Normalized();
            return FromAxisAngle(axis, Fixed64.Pi);
        }

        // Vectors are nearly parallel
        if (dot > Fixed64.FromFloat(0.999f))
        {
            return Identity;
        }

        Fixed64Vec3 cross = Fixed64Vec3.Cross(from, to);
        return new Fixed64Quaternion(
            cross.X,
            cross.Y,
            cross.Z,
            Fixed64.OneValue + dot
        ).Normalized();
    }

    // ============================================================
    // Operators
    // ============================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Quaternion operator *(Fixed64Quaternion a, Fixed64Quaternion b)
    {
        return new Fixed64Quaternion(
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
    public static Fixed64Vec3 operator *(Fixed64Quaternion q, Fixed64Vec3 v)
    {
        // Optimized quaternion-vector multiplication
        Fixed64Vec3 qv = new Fixed64Vec3(q.X, q.Y, q.Z);
        Fixed64Vec3 uv = Fixed64Vec3.Cross(qv, v);
        Fixed64Vec3 uuv = Fixed64Vec3.Cross(qv, uv);

        return v + (uv * q.W + uuv) * 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Fixed64Quaternion a, Fixed64Quaternion b)
    {
        return a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Fixed64Quaternion a, Fixed64Quaternion b)
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
    public Fixed64Quaternion Conjugate()
    {
        return new Fixed64Quaternion(-X, -Y, -Z, W);
    }

    /// <summary>
    /// Returns the inverse of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Quaternion Inverse()
    {
        Fixed64 lengthSq = X * X + Y * Y + Z * Z + W * W;
        if (lengthSq.Raw == 0)
        {
            return Identity;
        }
        return new Fixed64Quaternion(-X / lengthSq, -Y / lengthSq, -Z / lengthSq, W / lengthSq);
    }

    /// <summary>
    /// Returns the length squared of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 LengthSquared()
    {
        return X * X + Y * Y + Z * Z + W * W;
    }

    /// <summary>
    /// Returns the length of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64 Length()
    {
        return Fixed64.Sqrt(LengthSquared());
    }

    /// <summary>
    /// Returns a normalized version of this quaternion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed64Quaternion Normalized()
    {
        Fixed64 length = Length();
        if (length.Raw == 0)
        {
            return Identity;
        }
        return new Fixed64Quaternion(X / length, Y / length, Z / length, W / length);
    }

    /// <summary>
    /// Returns the dot product of two quaternions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Dot(Fixed64Quaternion a, Fixed64Quaternion b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
    }

    /// <summary>
    /// Returns the angle in radians between two quaternions.
    /// </summary>
    public static Fixed64 Angle(Fixed64Quaternion a, Fixed64Quaternion b)
    {
        Fixed64 dot = Fixed64.Abs(Dot(a, b));
        dot = Fixed64.Min(dot, Fixed64.OneValue);
        return Fixed64.Acos(dot) * 2;
    }

    // ============================================================
    // Conversion Methods
    // ============================================================

    /// <summary>
    /// Converts to Euler angles (in radians).
    /// </summary>
    public Fixed64Vec3 ToEuler()
    {
        // Roll (X-axis rotation)
        Fixed64 sinr_cosp = 2 * (W * X + Y * Z);
        Fixed64 cosr_cosp = Fixed64.OneValue - 2 * (X * X + Y * Y);
        Fixed64 roll = Fixed64.Atan2(sinr_cosp, cosr_cosp);

        // Pitch (Y-axis rotation)
        Fixed64 sinp = 2 * (W * Y - Z * X);
        Fixed64 pitch;
        if (Fixed64.Abs(sinp) >= Fixed64.OneValue)
        {
            pitch = sinp >= Fixed64.Zero ? Fixed64.HalfPi : -Fixed64.HalfPi;
        }
        else
        {
            pitch = Fixed64.Asin(sinp);
        }

        // Yaw (Z-axis rotation)
        Fixed64 siny_cosp = 2 * (W * Z + X * Y);
        Fixed64 cosy_cosp = Fixed64.OneValue - 2 * (Y * Y + Z * Z);
        Fixed64 yaw = Fixed64.Atan2(siny_cosp, cosy_cosp);

        return new Fixed64Vec3(roll, pitch, yaw);
    }

    /// <summary>
    /// Converts to axis-angle representation.
    /// </summary>
    public void ToAxisAngle(out Fixed64Vec3 axis, out Fixed64 angle)
    {
        Fixed64Quaternion q = W < Fixed64.Zero ? new Fixed64Quaternion(-X, -Y, -Z, -W) : this;

        Fixed64 sinHalfAngle = Fixed64.Sqrt(Fixed64.OneValue - q.W * q.W);
        angle = Fixed64.Acos(q.W) * 2;

        if (sinHalfAngle.Raw == 0)
        {
            axis = Fixed64Vec3.UnitX;
        }
        else
        {
            axis = new Fixed64Vec3(q.X / sinHalfAngle, q.Y / sinHalfAngle, q.Z / sinHalfAngle);
        }
    }

    // ============================================================
    // Interpolation
    // ============================================================

    /// <summary>
    /// Linear interpolation between two quaternions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Quaternion Lerp(Fixed64Quaternion a, Fixed64Quaternion b, Fixed64 t)
    {
        return LerpUnclamped(a, b, Fixed64.Clamp(t, Fixed64.Zero, Fixed64.OneValue));
    }

    /// <summary>
    /// Linear interpolation between two quaternions (unclamped).
    /// </summary>
    public static Fixed64Quaternion LerpUnclamped(Fixed64Quaternion a, Fixed64Quaternion b, Fixed64 t)
    {
        // Ensure shortest path
        Fixed64 dot = Dot(a, b);
        Fixed64Quaternion bAdjusted = dot < Fixed64.Zero ? new Fixed64Quaternion(-b.X, -b.Y, -b.Z, -b.W) : b;

        Fixed64 oneMinusT = Fixed64.OneValue - t;
        return new Fixed64Quaternion(
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
    public static Fixed64Quaternion Slerp(Fixed64Quaternion a, Fixed64Quaternion b, Fixed64 t)
    {
        return SlerpUnclamped(a, b, Fixed64.Clamp(t, Fixed64.Zero, Fixed64.OneValue));
    }

    /// <summary>
    /// Spherical linear interpolation between two quaternions (unclamped).
    /// </summary>
    public static Fixed64Quaternion SlerpUnclamped(Fixed64Quaternion a, Fixed64Quaternion b, Fixed64 t)
    {
        Fixed64 dot = Dot(a, b);

        // Ensure shortest path
        Fixed64Quaternion bAdjusted = b;
        if (dot < Fixed64.Zero)
        {
            dot = -dot;
            bAdjusted = new Fixed64Quaternion(-b.X, -b.Y, -b.Z, -b.W);
        }

        // Use lerp for nearly identical quaternions
        if (dot > Fixed64.FromFloat(0.9995f))
        {
            return LerpUnclamped(a, bAdjusted, t);
        }

        Fixed64 theta0 = Fixed64.Acos(dot);
        Fixed64 theta = theta0 * t;

        Fixed64.SinCosLUT(theta, out Fixed64 sinTheta, out Fixed64 cosTheta);
        Fixed64 sinTheta0 = Fixed64.Sqrt(Fixed64.OneValue - dot * dot);

        Fixed64 s0 = cosTheta - dot * sinTheta / sinTheta0;
        Fixed64 s1 = sinTheta / sinTheta0;

        return new Fixed64Quaternion(
            a.X * s0 + bAdjusted.X * s1,
            a.Y * s0 + bAdjusted.Y * s1,
            a.Z * s0 + bAdjusted.Z * s1,
            a.W * s0 + bAdjusted.W * s1
        );
    }

    /// <summary>
    /// Rotates a quaternion towards another by a maximum angle delta.
    /// </summary>
    public static Fixed64Quaternion RotateTowards(Fixed64Quaternion from, Fixed64Quaternion to, Fixed64 maxDegreesDelta)
    {
        Fixed64 angle = Angle(from, to);
        if (angle.Raw == 0)
        {
            return to;
        }

        Fixed64 t = Fixed64.Min(Fixed64.OneValue, maxDegreesDelta / angle);
        return SlerpUnclamped(from, to, t);
    }

    // ============================================================
    // Direction Vectors
    // ============================================================

    /// <summary>
    /// Returns the forward direction of this rotation.
    /// </summary>
    public Fixed64Vec3 Forward => this * Fixed64Vec3.Forward;

    /// <summary>
    /// Returns the up direction of this rotation.
    /// </summary>
    public Fixed64Vec3 Up => this * Fixed64Vec3.Up;

    /// <summary>
    /// Returns the right direction of this rotation.
    /// </summary>
    public Fixed64Vec3 Right => this * Fixed64Vec3.Right;

    // ============================================================
    // Equality and Hashing
    // ============================================================

    public bool Equals(Fixed64Quaternion other)
    {
        return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    }

    public override bool Equals(object? obj)
    {
        return obj is Fixed64Quaternion other && Equals(other);
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
