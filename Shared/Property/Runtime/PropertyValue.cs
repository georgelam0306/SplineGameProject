using System.Numerics;
using System.Runtime.InteropServices;
using Core;

namespace Property;

[StructLayout(LayoutKind.Explicit)]
public readonly struct PropertyValue
{
    [FieldOffset(0)] public readonly float Float;
    [FieldOffset(0)] public readonly int Int;
    [FieldOffset(0)] public readonly uint UInt;
    [FieldOffset(0)] public readonly bool Bool;
    [FieldOffset(0)] public readonly Vector2 Vec2;
    [FieldOffset(0)] public readonly Vector3 Vec3;
    [FieldOffset(0)] public readonly Vector4 Vec4;
    [FieldOffset(0)] public readonly Color32 Color32;
    [FieldOffset(0)] public readonly StringHandle StringHandle;
    [FieldOffset(0)] public readonly FixedMath.Fixed64 Fixed64;
    [FieldOffset(0)] public readonly FixedMath.Fixed64Vec2 Fixed64Vec2;
    [FieldOffset(0)] public readonly FixedMath.Fixed64Vec3 Fixed64Vec3;

    private PropertyValue(float value) : this() { Float = value; }
    private PropertyValue(int value) : this() { Int = value; }
    private PropertyValue(uint value) : this() { UInt = value; }
    private PropertyValue(bool value) : this() { Bool = value; }
    private PropertyValue(Vector2 value) : this() { Vec2 = value; }
    private PropertyValue(Vector3 value) : this() { Vec3 = value; }
    private PropertyValue(Vector4 value) : this() { Vec4 = value; }
    private PropertyValue(Color32 value) : this() { Color32 = value; }
    private PropertyValue(StringHandle value) : this() { StringHandle = value; }
    private PropertyValue(FixedMath.Fixed64 value) : this() { Fixed64 = value; }
    private PropertyValue(FixedMath.Fixed64Vec2 value) : this() { Fixed64Vec2 = value; }
    private PropertyValue(FixedMath.Fixed64Vec3 value) : this() { Fixed64Vec3 = value; }

    public static PropertyValue FromFloat(float value) => new(value);
    public static PropertyValue FromInt(int value) => new(value);
    public static PropertyValue FromUInt(uint value) => new(value);
    public static PropertyValue FromBool(bool value) => new(value);
    public static PropertyValue FromVec2(Vector2 value) => new(value);
    public static PropertyValue FromVec3(Vector3 value) => new(value);
    public static PropertyValue FromVec4(Vector4 value) => new(value);
    public static PropertyValue FromColor32(Color32 value) => new(value);
    public static PropertyValue FromStringHandle(StringHandle value) => new(value);
    public static PropertyValue FromFixed64(FixedMath.Fixed64 value) => new(value);
    public static PropertyValue FromFixed64Vec2(FixedMath.Fixed64Vec2 value) => new(value);
    public static PropertyValue FromFixed64Vec3(FixedMath.Fixed64Vec3 value) => new(value);

    public bool Equals(PropertyKind kind, in PropertyValue other)
    {
        switch (kind)
        {
            case PropertyKind.Float:
                return Float.Equals(other.Float);
            case PropertyKind.Int:
                return Int == other.Int;
            case PropertyKind.PrefabRef:
            case PropertyKind.ShapeRef:
            case PropertyKind.List:
                return UInt == other.UInt;
            case PropertyKind.Bool:
                return Bool == other.Bool;
            case PropertyKind.Vec2:
                return Vec2 == other.Vec2;
            case PropertyKind.Vec3:
                return Vec3 == other.Vec3;
            case PropertyKind.Vec4:
                return Vec4 == other.Vec4;
            case PropertyKind.Color32:
                return Color32.Equals(other.Color32);
            case PropertyKind.StringHandle:
                return StringHandle.Equals(other.StringHandle);
            case PropertyKind.Fixed64:
                return Fixed64.Equals(other.Fixed64);
            case PropertyKind.Fixed64Vec2:
                return Fixed64Vec2.Equals(other.Fixed64Vec2);
            case PropertyKind.Fixed64Vec3:
                return Fixed64Vec3.Equals(other.Fixed64Vec3);
            default:
                return false;
        }
    }
}
