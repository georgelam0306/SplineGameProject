using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using FixedMath;

namespace Property;

/// <summary>
/// Zero-allocation arithmetic operations on <see cref="PropertyValue"/>,
/// dispatched by <see cref="PropertyKind"/>.
/// </summary>
public static class PropertyValueOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Add(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(a.Float + b.Float),
            PropertyKind.Int => PropertyValue.FromInt(a.Int + b.Int),
            PropertyKind.Vec2 => PropertyValue.FromVec2(a.Vec2 + b.Vec2),
            PropertyKind.Vec3 => PropertyValue.FromVec3(a.Vec3 + b.Vec3),
            PropertyKind.Vec4 => PropertyValue.FromVec4(a.Vec4 + b.Vec4),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(a.Fixed64 + b.Fixed64),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(a.Fixed64Vec2 + b.Fixed64Vec2),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(a.Fixed64Vec3 + b.Fixed64Vec3),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Sub(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(a.Float - b.Float),
            PropertyKind.Int => PropertyValue.FromInt(a.Int - b.Int),
            PropertyKind.Vec2 => PropertyValue.FromVec2(a.Vec2 - b.Vec2),
            PropertyKind.Vec3 => PropertyValue.FromVec3(a.Vec3 - b.Vec3),
            PropertyKind.Vec4 => PropertyValue.FromVec4(a.Vec4 - b.Vec4),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(a.Fixed64 - b.Fixed64),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(a.Fixed64Vec2 - b.Fixed64Vec2),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(a.Fixed64Vec3 - b.Fixed64Vec3),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Mul(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(a.Float * b.Float),
            PropertyKind.Int => PropertyValue.FromInt(a.Int * b.Int),
            PropertyKind.Vec2 => PropertyValue.FromVec2(a.Vec2 * b.Vec2),
            PropertyKind.Vec3 => PropertyValue.FromVec3(a.Vec3 * b.Vec3),
            PropertyKind.Vec4 => PropertyValue.FromVec4(a.Vec4 * b.Vec4),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(a.Fixed64 * b.Fixed64),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Div(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(a.Float / b.Float),
            PropertyKind.Int => b.Int != 0 ? PropertyValue.FromInt(a.Int / b.Int) : default,
            PropertyKind.Vec2 => PropertyValue.FromVec2(a.Vec2 / b.Vec2),
            PropertyKind.Vec3 => PropertyValue.FromVec3(a.Vec3 / b.Vec3),
            PropertyKind.Vec4 => PropertyValue.FromVec4(a.Vec4 / b.Vec4),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(a.Fixed64 / b.Fixed64),
            _ => default,
        };
    }

    /// <summary>Scalar-broadcast multiply: Vec * Float → Vec.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue MulScalar(PropertyKind vecKind, in PropertyValue vec, float scalar)
    {
        return vecKind switch
        {
            PropertyKind.Vec2 => PropertyValue.FromVec2(vec.Vec2 * scalar),
            PropertyKind.Vec3 => PropertyValue.FromVec3(vec.Vec3 * scalar),
            PropertyKind.Vec4 => PropertyValue.FromVec4(vec.Vec4 * scalar),
            _ => default,
        };
    }

    /// <summary>Scalar-broadcast multiply: Fixed64Vec * Fixed64 → Fixed64Vec.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue MulFixed64Scalar(PropertyKind vecKind, in PropertyValue vec, Fixed64 scalar)
    {
        return vecKind switch
        {
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(vec.Fixed64Vec2 * scalar),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(vec.Fixed64Vec3 * scalar),
            _ => default,
        };
    }

    /// <summary>Scalar-broadcast divide: Vec / Float → Vec.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue DivScalar(PropertyKind vecKind, in PropertyValue vec, float scalar)
    {
        return vecKind switch
        {
            PropertyKind.Vec2 => PropertyValue.FromVec2(vec.Vec2 / new Vector2(scalar)),
            PropertyKind.Vec3 => PropertyValue.FromVec3(vec.Vec3 / new Vector3(scalar)),
            PropertyKind.Vec4 => PropertyValue.FromVec4(vec.Vec4 / new Vector4(scalar)),
            _ => default,
        };
    }

    /// <summary>Scalar-broadcast divide: Fixed64Vec / Fixed64 → Fixed64Vec.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue DivFixed64Scalar(PropertyKind vecKind, in PropertyValue vec, Fixed64 scalar)
    {
        return vecKind switch
        {
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(vec.Fixed64Vec2 / scalar),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(vec.Fixed64Vec3 / scalar),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Negate(PropertyKind kind, in PropertyValue a)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(-a.Float),
            PropertyKind.Int => PropertyValue.FromInt(-a.Int),
            PropertyKind.Vec2 => PropertyValue.FromVec2(-a.Vec2),
            PropertyKind.Vec3 => PropertyValue.FromVec3(-a.Vec3),
            PropertyKind.Vec4 => PropertyValue.FromVec4(-a.Vec4),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(-a.Fixed64),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(-a.Fixed64Vec2),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(-a.Fixed64Vec3),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Not(in PropertyValue a)
    {
        return PropertyValue.FromBool(!a.Bool);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Equal(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return PropertyValue.FromBool(a.Equals(kind, b));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue NotEqual(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return PropertyValue.FromBool(!a.Equals(kind, b));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Less(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromBool(a.Float < b.Float),
            PropertyKind.Int => PropertyValue.FromBool(a.Int < b.Int),
            PropertyKind.Fixed64 => PropertyValue.FromBool(a.Fixed64 < b.Fixed64),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue LessEqual(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromBool(a.Float <= b.Float),
            PropertyKind.Int => PropertyValue.FromBool(a.Int <= b.Int),
            PropertyKind.Fixed64 => PropertyValue.FromBool(a.Fixed64 <= b.Fixed64),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Greater(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromBool(a.Float > b.Float),
            PropertyKind.Int => PropertyValue.FromBool(a.Int > b.Int),
            PropertyKind.Fixed64 => PropertyValue.FromBool(a.Fixed64 > b.Fixed64),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue GreaterEqual(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromBool(a.Float >= b.Float),
            PropertyKind.Int => PropertyValue.FromBool(a.Int >= b.Int),
            PropertyKind.Fixed64 => PropertyValue.FromBool(a.Fixed64 >= b.Fixed64),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Min(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(MathF.Min(a.Float, b.Float)),
            PropertyKind.Int => PropertyValue.FromInt(Math.Min(a.Int, b.Int)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(Fixed64.Min(a.Fixed64, b.Fixed64)),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(Fixed64Vec2.Min(a.Fixed64Vec2, b.Fixed64Vec2)),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(Fixed64Vec3.Min(a.Fixed64Vec3, b.Fixed64Vec3)),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Max(PropertyKind kind, in PropertyValue a, in PropertyValue b)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(MathF.Max(a.Float, b.Float)),
            PropertyKind.Int => PropertyValue.FromInt(Math.Max(a.Int, b.Int)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(Fixed64.Max(a.Fixed64, b.Fixed64)),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(Fixed64Vec2.Max(a.Fixed64Vec2, b.Fixed64Vec2)),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(Fixed64Vec3.Max(a.Fixed64Vec3, b.Fixed64Vec3)),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Clamp(PropertyKind kind, in PropertyValue x, in PropertyValue lo, in PropertyValue hi)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(Math.Clamp(x.Float, lo.Float, hi.Float)),
            PropertyKind.Int => PropertyValue.FromInt(Math.Clamp(x.Int, lo.Int, hi.Int)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(Fixed64.Clamp(x.Fixed64, lo.Fixed64, hi.Fixed64)),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(Fixed64Vec2.Clamp(x.Fixed64Vec2, lo.Fixed64Vec2, hi.Fixed64Vec2)),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(Fixed64Vec3.Clamp(x.Fixed64Vec3, lo.Fixed64Vec3, hi.Fixed64Vec3)),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Abs(PropertyKind kind, in PropertyValue a)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(MathF.Abs(a.Float)),
            PropertyKind.Int => PropertyValue.FromInt(Math.Abs(a.Int)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(Fixed64.Abs(a.Fixed64)),
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Floor(PropertyKind kind, in PropertyValue a)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(MathF.Floor(a.Float)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(Fixed64.Floor(a.Fixed64)),
            _ => a,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Ceil(PropertyKind kind, in PropertyValue a)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(MathF.Ceiling(a.Float)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(Fixed64.Ceiling(a.Fixed64)),
            _ => a,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue Round(PropertyKind kind, in PropertyValue a)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(MathF.Round(a.Float)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(Fixed64.Round(a.Fixed64)),
            _ => a,
        };
    }

    public static PropertyValue Lerp(PropertyKind kind, in PropertyValue a, in PropertyValue b, float t)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(a.Float + (b.Float - a.Float) * t),
            PropertyKind.Vec2 => PropertyValue.FromVec2(Vector2.Lerp(a.Vec2, b.Vec2, t)),
            PropertyKind.Vec3 => PropertyValue.FromVec3(Vector3.Lerp(a.Vec3, b.Vec3, t)),
            PropertyKind.Vec4 => PropertyValue.FromVec4(Vector4.Lerp(a.Vec4, b.Vec4, t)),
            PropertyKind.Color32 => LerpColor32(a.Color32, b.Color32, t),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(Fixed64.Lerp(a.Fixed64, b.Fixed64, Fixed64.FromFloat(t))),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(Fixed64Vec2.Lerp(a.Fixed64Vec2, b.Fixed64Vec2, Fixed64.FromFloat(t))),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(Fixed64Vec3.Lerp(a.Fixed64Vec3, b.Fixed64Vec3, Fixed64.FromFloat(t))),
            _ => default,
        };
    }

    public static PropertyValue Remap(float x, float inLo, float inHi, float outLo, float outHi)
    {
        float t = (x - inLo) / (inHi - inLo);
        return PropertyValue.FromFloat(outLo + (outHi - outLo) * t);
    }

    /// <summary>Promote Int → Float.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue PromoteToFloat(in PropertyValue a)
    {
        return PropertyValue.FromFloat(a.Int);
    }

    /// <summary>Promote Int → Fixed64.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyValue PromoteToFixed64(in PropertyValue a)
    {
        return PropertyValue.FromFixed64(Fixed64.FromInt(a.Int));
    }

    private static PropertyValue LerpColor32(Core.Color32 a, Core.Color32 b, float t)
    {
        byte r = (byte)(a.R + (b.R - a.R) * t);
        byte g = (byte)(a.G + (b.G - a.G) * t);
        byte bl = (byte)(a.B + (b.B - a.B) * t);
        byte al = (byte)(a.A + (b.A - a.A) * t);
        return PropertyValue.FromColor32(new Core.Color32(r, g, bl, al));
    }
}
