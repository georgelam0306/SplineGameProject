// SPDX-License-Identifier: MIT
#nullable enable
namespace Property.Generator
{
    internal enum PropertyKind : byte
    {
        Auto = 0,
        Float = 1,
        Int = 2,
        Bool = 3,
        Vec2 = 4,
        Vec3 = 5,
        Vec4 = 6,
        Color32 = 7,
        StringHandle = 8,
        Fixed64 = 9,
        Fixed64Vec2 = 10,
        Fixed64Vec3 = 11,

        // Derp.UI editor-only prefab variables
        PrefabRef = 12,
        ShapeRef = 13,
        List = 14,
        Trigger = 15
    }
}
