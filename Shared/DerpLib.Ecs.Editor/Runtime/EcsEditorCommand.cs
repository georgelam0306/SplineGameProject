using DerpLib.Ecs;
using FixedMath;
using System.Numerics;
using Core;
using Property;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Editor/user-intent command for modifying ECS state. Commands are intended to be stored in
/// reusable buffers without boxing/allocations.
/// </summary>
public readonly struct EcsEditorCommand
{
    public readonly EcsEditorCommandKind Kind;
    public readonly EcsEditMode EditMode;
    public readonly EcsEditorPropertyAddress Address;
    public readonly PropertyKind PropertyKind;

    public readonly float FloatValue;
    public readonly int IntValue;
    public readonly bool BoolValue;
    public readonly Vector2 Vec2Value;
    public readonly Vector3 Vec3Value;
    public readonly Vector4 Vec4Value;
    public readonly Color32 Color32Value;
    public readonly StringHandle StringHandleValue;
    public readonly Fixed64 Fixed64Value;
    public readonly Fixed64Vec2 Fixed64Vec2Value;
    public readonly Fixed64Vec3 Fixed64Vec3Value;

    private EcsEditorCommand(
        EcsEditorCommandKind kind,
        EcsEditMode editMode,
        in EcsEditorPropertyAddress address,
        PropertyKind propertyKind,
        float floatValue,
        int intValue,
        bool boolValue,
        Vector2 vec2Value,
        Vector3 vec3Value,
        Vector4 vec4Value,
        Color32 color32Value,
        StringHandle stringHandleValue,
        Fixed64 fixed64Value,
        Fixed64Vec2 fixed64Vec2Value,
        Fixed64Vec3 fixed64Vec3Value)
    {
        Kind = kind;
        EditMode = editMode;
        Address = address;
        PropertyKind = propertyKind;
        FloatValue = floatValue;
        IntValue = intValue;
        BoolValue = boolValue;
        Vec2Value = vec2Value;
        Vec3Value = vec3Value;
        Vec4Value = vec4Value;
        Color32Value = color32Value;
        StringHandleValue = stringHandleValue;
        Fixed64Value = fixed64Value;
        Fixed64Vec2Value = fixed64Vec2Value;
        Fixed64Vec3Value = fixed64Vec3Value;
    }

    public static EcsEditorCommand SetFloat(EcsEditMode editMode, in EcsEditorPropertyAddress address, float value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Float, value, 0, false, default, default, default, default, default, default, default, default);
    }

    public static EcsEditorCommand SetInt(EcsEditMode editMode, in EcsEditorPropertyAddress address, int value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Int, 0f, value, false, default, default, default, default, default, default, default, default);
    }

    public static EcsEditorCommand SetBool(EcsEditMode editMode, in EcsEditorPropertyAddress address, bool value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Bool, 0f, 0, value, default, default, default, default, default, default, default, default);
    }

    public static EcsEditorCommand SetVec2(EcsEditMode editMode, in EcsEditorPropertyAddress address, Vector2 value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Vec2, 0f, 0, false, value, default, default, default, default, default, default, default);
    }

    public static EcsEditorCommand SetVec3(EcsEditMode editMode, in EcsEditorPropertyAddress address, Vector3 value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Vec3, 0f, 0, false, default, value, default, default, default, default, default, default);
    }

    public static EcsEditorCommand SetVec4(EcsEditMode editMode, in EcsEditorPropertyAddress address, Vector4 value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Vec4, 0f, 0, false, default, default, value, default, default, default, default, default);
    }

    public static EcsEditorCommand SetColor32(EcsEditMode editMode, in EcsEditorPropertyAddress address, Color32 value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Color32, 0f, 0, false, default, default, default, value, default, default, default, default);
    }

    public static EcsEditorCommand SetStringHandle(EcsEditMode editMode, in EcsEditorPropertyAddress address, StringHandle value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.StringHandle, 0f, 0, false, default, default, default, default, value, default, default, default);
    }

    public static EcsEditorCommand SetFixed64(EcsEditMode editMode, in EcsEditorPropertyAddress address, Fixed64 value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Fixed64, 0f, 0, false, default, default, default, default, default, value, default, default);
    }

    public static EcsEditorCommand SetFixed64Vec2(EcsEditMode editMode, in EcsEditorPropertyAddress address, Fixed64Vec2 value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Fixed64Vec2, 0f, 0, false, default, default, default, default, default, default, value, default);
    }

    public static EcsEditorCommand SetFixed64Vec3(EcsEditMode editMode, in EcsEditorPropertyAddress address, Fixed64Vec3 value)
    {
        return new EcsEditorCommand(EcsEditorCommandKind.SetProperty, editMode, in address, PropertyKind.Fixed64Vec3, 0f, 0, false, default, default, default, default, default, default, default, value);
    }
}
