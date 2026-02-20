// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;

namespace Property.Generator
{
    internal static class PropertyTypeMap
    {
        public static bool TryGetKind(ITypeSymbol typeSymbol, out PropertyKind kind)
        {
            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Single:
                    kind = PropertyKind.Float;
                    return true;
                case SpecialType.System_Int32:
                    kind = PropertyKind.Int;
                    return true;
                case SpecialType.System_Boolean:
                    kind = PropertyKind.Bool;
                    return true;
            }

            string typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            switch (typeName)
            {
                case "global::System.Numerics.Vector2":
                    kind = PropertyKind.Vec2;
                    return true;
                case "global::System.Numerics.Vector3":
                    kind = PropertyKind.Vec3;
                    return true;
                case "global::System.Numerics.Vector4":
                    kind = PropertyKind.Vec4;
                    return true;
                case "global::Core.Color32":
                    kind = PropertyKind.Color32;
                    return true;
                case "global::Core.StringHandle":
                    kind = PropertyKind.StringHandle;
                    return true;
                case "global::FixedMath.Fixed64":
                    kind = PropertyKind.Fixed64;
                    return true;
                case "global::FixedMath.Fixed64Vec2":
                    kind = PropertyKind.Fixed64Vec2;
                    return true;
                case "global::FixedMath.Fixed64Vec3":
                    kind = PropertyKind.Fixed64Vec3;
                    return true;
                default:
                    kind = PropertyKind.Auto;
                    return false;
            }
        }

        public static bool IsAutoExpandable(ITypeSymbol typeSymbol)
        {
            string typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return typeName == "global::System.Numerics.Vector2" ||
                   typeName == "global::System.Numerics.Vector3" ||
                   typeName == "global::System.Numerics.Vector4";
        }

        public static string GetTypeName(PropertyKind kind)
        {
            switch (kind)
            {
                case PropertyKind.Float:
                    return "global::System.Single";
                case PropertyKind.Int:
                    return "global::System.Int32";
                case PropertyKind.Bool:
                    return "global::System.Boolean";
                case PropertyKind.Vec2:
                    return "global::System.Numerics.Vector2";
                case PropertyKind.Vec3:
                    return "global::System.Numerics.Vector3";
                case PropertyKind.Vec4:
                    return "global::System.Numerics.Vector4";
                case PropertyKind.Color32:
                    return "global::Core.Color32";
                case PropertyKind.StringHandle:
                    return "global::Core.StringHandle";
                case PropertyKind.Fixed64:
                    return "global::FixedMath.Fixed64";
                case PropertyKind.Fixed64Vec2:
                    return "global::FixedMath.Fixed64Vec2";
                case PropertyKind.Fixed64Vec3:
                    return "global::FixedMath.Fixed64Vec3";
                default:
                    return "global::System.Object";
            }
        }

        public static string GetKindLiteral(PropertyKind kind)
        {
            return "global::Property.PropertyKind." + kind.ToString();
        }

        public static string GetFlagsLiteral(PropertyFlags flags)
        {
            if (flags == PropertyFlags.None)
            {
                return "global::Property.PropertyFlags.None";
            }

            string result = string.Empty;
            AppendFlag(PropertyFlags.ReadOnly, "ReadOnly", ref result, flags);
            AppendFlag(PropertyFlags.Hidden, "Hidden", ref result, flags);
            AppendFlag(PropertyFlags.Animated, "Animated", ref result, flags);
            return result;
        }

        private static void AppendFlag(PropertyFlags flag, string name, ref string result, PropertyFlags flags)
        {
            if ((flags & flag) != 0)
            {
                string literal = "global::Property.PropertyFlags." + name;
                if (string.IsNullOrEmpty(result))
                {
                    result = literal;
                }
                else
                {
                    result = result + " | " + literal;
                }
            }
        }
    }
}
