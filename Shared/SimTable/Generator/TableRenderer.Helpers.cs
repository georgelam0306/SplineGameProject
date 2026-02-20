#nullable enable
using Microsoft.CodeAnalysis;
using System.Text;

namespace SimTable.Generator
{
    internal static partial class TableRenderer
    {
        internal static int GetTypeSize(ITypeSymbol type)
        {
            // Handle enum types by recursing on their underlying type
            if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol namedType)
            {
                return GetTypeSize(namedType.EnumUnderlyingType!);
            }

            // Use simple type name - works regardless of namespace
            var typeName = type.Name;

            return typeName switch
            {
                "Boolean" => 1,
                "Byte" => 1,
                "SByte" => 1,
                "Int16" => 2,
                "UInt16" => 2,
                "Int32" => 4,
                "UInt32" => 4,
                "Int64" => 8,
                "UInt64" => 8,
                "Single" => 4,
                "Double" => 8,
                "Fixed64" => 8,
                "Fixed32" => 4,
                "Fixed64Vec2" => 16,
                "Fixed32Vec2" => 8,
                _ => type.SpecialType switch
                {
                    SpecialType.System_Boolean => 1,
                    SpecialType.System_Byte => 1,
                    SpecialType.System_SByte => 1,
                    SpecialType.System_Int16 => 2,
                    SpecialType.System_UInt16 => 2,
                    SpecialType.System_Int32 => 4,
                    SpecialType.System_UInt32 => 4,
                    SpecialType.System_Int64 => 8,
                    SpecialType.System_UInt64 => 8,
                    SpecialType.System_Single => 4,
                    SpecialType.System_Double => 8,
                    _ => 8
                }
            };
        }

        private static string GetHashExpression(string fieldName, ITypeSymbol type)
        {
            // Use simple type name - works regardless of namespace
            var typeName = type.Name;

            return typeName switch
            {
                "Boolean" => $"({fieldName}(slot) ? 1UL : 0UL)",
                "Byte" or "SByte" or "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" => $"(ulong){fieldName}(slot)",
                "UInt64" => $"{fieldName}(slot)",
                "Fixed64" or "Fixed32" => $"(ulong){fieldName}(slot).Raw",
                "Fixed64Vec2" or "Fixed32Vec2" => $"((ulong){fieldName}(slot).X.Raw ^ ((ulong){fieldName}(slot).Y.Raw << 32))",
                "SimHandle" => $"((ulong){fieldName}(slot).TableId | ((ulong){fieldName}(slot).StableId << 32))",
                _ => type.SpecialType switch
                {
                    SpecialType.System_Boolean => $"({fieldName}(slot) ? 1UL : 0UL)",
                    SpecialType.System_UInt64 => $"{fieldName}(slot)",
                    _ => $"(ulong){fieldName}(slot)"
                }
            };
        }

        private static string GetArray2DHashExpression(string fieldName, ITypeSymbol type)
        {
            // Generate a foreach loop to hash all elements in the 2D array span
            var typeName = type.Name;
            var sb = new StringBuilder();
            sb.AppendLine($"foreach (var elem in {fieldName}Span(slot))");
            sb.AppendLine("                {");

            string elemHash = typeName switch
            {
                "Boolean" => "(elem ? 1UL : 0UL)",
                "Byte" or "SByte" or "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" => "(ulong)elem",
                "UInt64" => "elem",
                "Fixed64" or "Fixed32" => "(ulong)elem.Raw",
                "Fixed64Vec2" or "Fixed32Vec2" => "((ulong)elem.X.Raw ^ ((ulong)elem.Y.Raw << 32))",
                _ => type.SpecialType switch
                {
                    SpecialType.System_Boolean => "(elem ? 1UL : 0UL)",
                    SpecialType.System_UInt64 => "elem",
                    _ => "(ulong)elem"
                }
            };

            sb.AppendLine($"                    hash ^= {elemHash};");
            sb.AppendLine("                    hash *= 1099511628211UL;");
            sb.Append("                }");
            return sb.ToString();
        }

        /// <summary>
        /// Generates JSON writing code for a named property.
        /// </summary>
        private static void WriteJsonPropertyValue(StringBuilder sb, string fieldName, ITypeSymbol type, string indent)
        {
            var typeName = type.Name;

            if (type.TypeKind == TypeKind.Enum)
            {
                sb.AppendLine($"{indent}writer.WriteNumber(\"{fieldName}\", (int){fieldName}(slot));");
                return;
            }

            switch (typeName)
            {
                case "Boolean":
                    sb.AppendLine($"{indent}writer.WriteBoolean(\"{fieldName}\", {fieldName}(slot));");
                    break;
                case "Byte":
                case "SByte":
                case "Int16":
                case "UInt16":
                case "Int32":
                case "UInt32":
                    sb.AppendLine($"{indent}writer.WriteNumber(\"{fieldName}\", {fieldName}(slot));");
                    break;
                case "Int64":
                case "UInt64":
                    sb.AppendLine($"{indent}writer.WriteString(\"{fieldName}\", {fieldName}(slot).ToString());");
                    break;
                case "Single":
                case "Double":
                    sb.AppendLine($"{indent}writer.WriteNumber(\"{fieldName}\", {fieldName}(slot));");
                    break;
                case "Fixed64":
                case "Fixed32":
                    sb.AppendLine($"{indent}writer.WriteStartObject(\"{fieldName}\");");
                    sb.AppendLine($"{indent}writer.WriteString(\"raw\", {fieldName}(slot).Raw.ToString(\"X16\"));");
                    sb.AppendLine($"{indent}writer.WriteNumber(\"value\", {fieldName}(slot).ToDouble());");
                    sb.AppendLine($"{indent}writer.WriteEndObject();");
                    break;
                case "Fixed64Vec2":
                case "Fixed32Vec2":
                    sb.AppendLine($"{indent}writer.WriteStartObject(\"{fieldName}\");");
                    sb.AppendLine($"{indent}var _{fieldName} = {fieldName}(slot);");
                    sb.AppendLine($"{indent}writer.WriteString(\"xRaw\", _{fieldName}.X.Raw.ToString(\"X16\"));");
                    sb.AppendLine($"{indent}writer.WriteString(\"yRaw\", _{fieldName}.Y.Raw.ToString(\"X16\"));");
                    sb.AppendLine($"{indent}writer.WriteNumber(\"x\", _{fieldName}.X.ToDouble());");
                    sb.AppendLine($"{indent}writer.WriteNumber(\"y\", _{fieldName}.Y.ToDouble());");
                    sb.AppendLine($"{indent}writer.WriteEndObject();");
                    break;
                case "SimHandle":
                    sb.AppendLine($"{indent}writer.WriteStartObject(\"{fieldName}\");");
                    sb.AppendLine($"{indent}var _{fieldName} = {fieldName}(slot);");
                    sb.AppendLine($"{indent}writer.WriteNumber(\"tableId\", _{fieldName}.TableId);");
                    sb.AppendLine($"{indent}writer.WriteNumber(\"stableId\", _{fieldName}.StableId);");
                    sb.AppendLine($"{indent}writer.WriteEndObject();");
                    break;
                default:
                    if (type.SpecialType != SpecialType.None)
                    {
                        sb.AppendLine($"{indent}writer.WriteNumber(\"{fieldName}\", (long){fieldName}(slot));");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}writer.WriteString(\"{fieldName}\", {fieldName}(slot).ToString());");
                    }
                    break;
            }
        }

        /// <summary>
        /// Generates JSON writing code for an array element (value only, no property name).
        /// </summary>
        private static void WriteJsonElementValue(StringBuilder sb, ITypeSymbol type, string varName, string indent)
        {
            var typeName = type.Name;

            if (type.TypeKind == TypeKind.Enum)
            {
                sb.AppendLine($"{indent}writer.WriteNumberValue((int){varName});");
                return;
            }

            switch (typeName)
            {
                case "Boolean":
                    sb.AppendLine($"{indent}writer.WriteBooleanValue({varName});");
                    break;
                case "Byte":
                case "SByte":
                case "Int16":
                case "UInt16":
                case "Int32":
                case "UInt32":
                    sb.AppendLine($"{indent}writer.WriteNumberValue({varName});");
                    break;
                case "Int64":
                case "UInt64":
                    sb.AppendLine($"{indent}writer.WriteStringValue({varName}.ToString());");
                    break;
                case "Single":
                case "Double":
                    sb.AppendLine($"{indent}writer.WriteNumberValue({varName});");
                    break;
                case "Fixed64":
                case "Fixed32":
                    sb.AppendLine($"{indent}writer.WriteStringValue({varName}.Raw.ToString(\"X16\"));");
                    break;
                case "Fixed64Vec2":
                case "Fixed32Vec2":
                    sb.AppendLine($"{indent}writer.WriteStartObject();");
                    sb.AppendLine($"{indent}writer.WriteString(\"xRaw\", {varName}.X.Raw.ToString(\"X16\"));");
                    sb.AppendLine($"{indent}writer.WriteString(\"yRaw\", {varName}.Y.Raw.ToString(\"X16\"));");
                    sb.AppendLine($"{indent}writer.WriteEndObject();");
                    break;
                case "SimHandle":
                    sb.AppendLine($"{indent}writer.WriteStartObject();");
                    sb.AppendLine($"{indent}writer.WriteNumber(\"tableId\", {varName}.TableId);");
                    sb.AppendLine($"{indent}writer.WriteNumber(\"stableId\", {varName}.StableId);");
                    sb.AppendLine($"{indent}writer.WriteEndObject();");
                    break;
                default:
                    if (type.SpecialType != SpecialType.None)
                    {
                        sb.AppendLine($"{indent}writer.WriteNumberValue((long){varName});");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}writer.WriteStringValue({varName}.ToString());");
                    }
                    break;
            }
        }

        /// <summary>
        /// Gets position field info for spatial computations.
        /// </summary>
        internal static (bool hasPosition, string? positionTypeName) GetPositionInfo(SchemaModel model)
        {
            foreach (var col in model.Columns)
            {
                if (col.Name == "Position")
                {
                    return (true, col.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }
            return (false, null);
        }
    }
}
