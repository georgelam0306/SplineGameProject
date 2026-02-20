using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DerpLib.AssetPipeline.Generator
{
    [Generator]
    public class ContentGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all class declarations that implement IAsset
            var assetTypes = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax || node is RecordDeclarationSyntax,
                    transform: static (ctx, _) => GetAssetTypeOrNull(ctx))
                .Where(static t => t != null)
                .Select(static (t, _) => t!);

            // Combine with compilation
            var compilationAndTypes = context.CompilationProvider.Combine(assetTypes.Collect());

            // Generate source
            context.RegisterSourceOutput(compilationAndTypes, static (spc, source) => Execute(source.Left, source.Right, spc));
        }

        private static AssetTypeInfo? GetAssetTypeOrNull(GeneratorSyntaxContext context)
        {
            var typeDecl = (TypeDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(typeDecl);

            if (!(symbol is INamedTypeSymbol namedType))
                return null;

            // Check if it implements IAsset
            if (!ImplementsIAsset(namedType))
                return null;

            // Skip abstract types
            if (namedType.IsAbstract)
                return null;

            // Collect properties
            var properties = new List<PropertyInfo>();
            foreach (var member in namedType.GetMembers())
            {
                if (member is IPropertySymbol prop &&
                    prop.DeclaredAccessibility == Accessibility.Public &&
                    !prop.IsStatic &&
                    !prop.IsIndexer &&
                    prop.GetMethod != null &&
                    prop.SetMethod != null)
                {
                    properties.Add(new PropertyInfo(
                        prop.Name,
                        prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        GetJsonType(prop.Type),
                        IsArrayType(prop.Type),
                        GetArrayElementType(prop.Type)));
                }
            }

            return new AssetTypeInfo(
                namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                namedType.Name,
                namedType.ContainingNamespace.ToDisplayString(),
                properties);
        }

        private static bool ImplementsIAsset(INamedTypeSymbol type)
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.Name == "IAsset" &&
                    iface.ContainingNamespace.ToDisplayString() == "DerpLib.AssetPipeline")
                {
                    return true;
                }
            }
            return false;
        }

        private static JsonType GetJsonType(ITypeSymbol type)
        {
            var name = type.ToDisplayString();

            // Handle nullable
            if (type is INamedTypeSymbol named && named.IsGenericType &&
                named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            {
                type = named.TypeArguments[0];
                name = type.ToDisplayString();
            }

            // Handle arrays
            if (type is IArrayTypeSymbol arrayType)
            {
                return GetJsonType(arrayType.ElementType) switch
                {
                    JsonType.String => JsonType.StringArray,
                    JsonType.Int => JsonType.IntArray,
                    JsonType.UInt => JsonType.UIntArray,
                    JsonType.Float => JsonType.FloatArray,
                    JsonType.Double => JsonType.DoubleArray,
                    JsonType.Bool => JsonType.BoolArray,
                    JsonType.Byte => JsonType.ByteArray,
                    _ => JsonType.ObjectArray
                };
            }

            return name switch
            {
                "string" or "System.String" => JsonType.String,
                "int" or "System.Int32" => JsonType.Int,
                "uint" or "System.UInt32" => JsonType.UInt,
                "long" or "System.Int64" => JsonType.Long,
                "ulong" or "System.UInt64" => JsonType.ULong,
                "float" or "System.Single" => JsonType.Float,
                "double" or "System.Double" => JsonType.Double,
                "bool" or "System.Boolean" => JsonType.Bool,
                "byte" or "System.Byte" => JsonType.Byte,
                "byte[]" or "System.Byte[]" => JsonType.ByteArray,
                _ => JsonType.Object
            };
        }

        private static bool IsArrayType(ITypeSymbol type)
        {
            return type is IArrayTypeSymbol;
        }

        private static string GetArrayElementType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                return arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            return "";
        }

        private static void Execute(Compilation compilation, ImmutableArray<AssetTypeInfo> assetTypes, SourceProductionContext context)
        {
            if (assetTypes.IsDefaultOrEmpty)
                return;

            // Deduplicate
            var distinctTypes = assetTypes.Distinct().ToList();

            if (distinctTypes.Count == 0)
                return;

            // Determine the namespace to use
            var rootNamespace = GetRootNamespace(distinctTypes);

            // Generate ContentSerializer
            var serializerSource = GenerateSerializer(distinctTypes, rootNamespace);
            context.AddSource("ContentSerializer.g.cs", serializerSource);

            // Generate ContentModule and Content facade
            var contentSource = GenerateContent(distinctTypes, rootNamespace);
            context.AddSource("Content.g.cs", contentSource);
        }

        private static string GetRootNamespace(List<AssetTypeInfo> types)
        {
            var namespaces = types.Select(t => t.Namespace).Distinct().ToList();
            if (namespaces.Count == 1)
                return namespaces[0];

            var first = namespaces[0].Split('.');
            var commonParts = new List<string>();

            for (int i = 0; i < first.Length; i++)
            {
                var part = first[i];
                if (namespaces.All(ns => ns.Split('.').Length > i && ns.Split('.')[i] == part))
                    commonParts.Add(part);
                else
                    break;
            }

            return commonParts.Count > 0 ? string.Join(".", commonParts) : namespaces[0];
        }

        private static string GenerateSerializer(List<AssetTypeInfo> types, string rootNamespace)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Buffers;");
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using DerpLib.AssetPipeline;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}");
            sb.AppendLine("{");

            // Generate the serializer class
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Auto-generated AOT-compatible serializer for IAsset types.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public sealed class ContentSerializer : IBlobSerializer");
            sb.AppendLine("    {");
            sb.AppendLine("        public static ContentSerializer Instance { get; } = new ContentSerializer();");
            sb.AppendLine();

            // Generate Serialize<T>
            sb.AppendLine("        public byte[] Serialize<T>(T obj)");
            sb.AppendLine("        {");
            sb.AppendLine("            var buffer = new ArrayBufferWriter<byte>();");
            sb.AppendLine("            using var writer = new Utf8JsonWriter(buffer);");
            sb.AppendLine();

            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                var condition = i == 0 ? "if" : "else if";
                sb.AppendLine($"            {condition} (typeof(T) == typeof({type.FullyQualifiedName}))");
                sb.AppendLine("            {");
                sb.AppendLine($"                Write{type.Name}(writer, ({type.FullyQualifiedName})(object)obj!);");
                sb.AppendLine("            }");
            }

            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                throw new NotSupportedException($\"Type {typeof(T).FullName} is not a registered IAsset type.\");");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            writer.Flush();");
            sb.AppendLine("            return buffer.WrittenSpan.ToArray();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generate Deserialize<T>
            sb.AppendLine("        public T Deserialize<T>(ReadOnlySpan<byte> bytes)");
            sb.AppendLine("        {");
            sb.AppendLine("            var reader = new Utf8JsonReader(bytes);");
            sb.AppendLine();

            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                var condition = i == 0 ? "if" : "else if";
                sb.AppendLine($"            {condition} (typeof(T) == typeof({type.FullyQualifiedName}))");
                sb.AppendLine("            {");
                sb.AppendLine($"                return (T)(object)Read{type.Name}(ref reader);");
                sb.AppendLine("            }");
            }

            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                throw new NotSupportedException($\"Type {typeof(T).FullName} is not a registered IAsset type.\");");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Build set of all asset type names for nested type lookup
            var allAssetTypes = new HashSet<string>(types.Select(t => t.FullyQualifiedName));

            // Generate per-type Write methods
            foreach (var type in types)
            {
                GenerateWriteMethod(sb, type, allAssetTypes);
            }

            // Generate per-type Read methods
            foreach (var type in types)
            {
                GenerateReadMethod(sb, type, allAssetTypes);
            }

            // Generate Read{TypeName}Array helpers for IAsset types used in arrays
            foreach (var type in types)
            {
                sb.AppendLine($"        private static {type.FullyQualifiedName}[] Read{type.Name}Array(ref Utf8JsonReader reader)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var list = new System.Collections.Generic.List<{type.FullyQualifiedName}>();");
                sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)");
                sb.AppendLine("            {");
                sb.AppendLine("                if (reader.TokenType == JsonTokenType.StartObject)");
                sb.AppendLine("                {");
                sb.AppendLine("                    // Back up so Read can see StartObject");
                sb.AppendLine("                    var readerCopy = reader;");
                sb.AppendLine($"                    list.Add(Read{type.Name}Inner(ref reader));");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                sb.AppendLine("            return list.ToArray();");
                sb.AppendLine("        }");
                sb.AppendLine();

                // Generate inner read method that doesn't consume StartObject
                sb.AppendLine($"        private static {type.FullyQualifiedName} Read{type.Name}Inner(ref Utf8JsonReader reader)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var obj = new {type.FullyQualifiedName}();");
                sb.AppendLine();
                sb.AppendLine("            // StartObject already consumed by caller");
                sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)");
                sb.AppendLine("            {");
                sb.AppendLine("                if (reader.TokenType != JsonTokenType.PropertyName)");
                sb.AppendLine("                    continue;");
                sb.AppendLine();
                sb.AppendLine("                var propName = reader.GetString();");
                sb.AppendLine("                reader.Read();");
                sb.AppendLine();
                sb.AppendLine("                switch (propName)");
                sb.AppendLine("                {");

                foreach (var prop in type.Properties)
                {
                    var jsonName = ToCamelCase(prop.Name);
                    sb.AppendLine($"                    case \"{jsonName}\":");

                    switch (prop.JsonType)
                    {
                        case JsonType.String:
                            sb.AppendLine($"                        obj.{prop.Name} = reader.GetString() ?? string.Empty;");
                            break;
                        case JsonType.Int:
                            sb.AppendLine($"                        obj.{prop.Name} = reader.GetInt32();");
                            break;
                        case JsonType.UInt:
                            sb.AppendLine($"                        obj.{prop.Name} = reader.GetUInt32();");
                            break;
                        case JsonType.Float:
                            sb.AppendLine($"                        obj.{prop.Name} = reader.GetSingle();");
                            break;
                        case JsonType.Double:
                            sb.AppendLine($"                        obj.{prop.Name} = reader.GetDouble();");
                            break;
                        case JsonType.Bool:
                            sb.AppendLine($"                        obj.{prop.Name} = reader.GetBoolean();");
                            break;
                        case JsonType.ByteArray:
                            sb.AppendLine($"                        obj.{prop.Name} = reader.GetBytesFromBase64();");
                            break;
                        default:
                            sb.AppendLine("                        reader.Skip();");
                            break;
                    }
                    sb.AppendLine("                        break;");
                }

                sb.AppendLine("                    default:");
                sb.AppendLine("                        reader.Skip();");
                sb.AppendLine("                        break;");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine("            return obj;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Generate array reader helpers inside the serializer class
            sb.AppendLine("        private static string[] ReadStringArray(ref Utf8JsonReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine("            var list = new System.Collections.Generic.List<string>();");
            sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)");
            sb.AppendLine("                list.Add(reader.GetString() ?? string.Empty);");
            sb.AppendLine("            return list.ToArray();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static int[] ReadIntArray(ref Utf8JsonReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine("            var list = new System.Collections.Generic.List<int>();");
            sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)");
            sb.AppendLine("                list.Add(reader.GetInt32());");
            sb.AppendLine("            return list.ToArray();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static uint[] ReadUIntArray(ref Utf8JsonReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine("            var list = new System.Collections.Generic.List<uint>();");
            sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)");
            sb.AppendLine("                list.Add(reader.GetUInt32());");
            sb.AppendLine("            return list.ToArray();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static float[] ReadFloatArray(ref Utf8JsonReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine("            var list = new System.Collections.Generic.List<float>();");
            sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)");
            sb.AppendLine("                list.Add(reader.GetSingle());");
            sb.AppendLine("            return list.ToArray();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static double[] ReadDoubleArray(ref Utf8JsonReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine("            var list = new System.Collections.Generic.List<double>();");
            sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)");
            sb.AppendLine("                list.Add(reader.GetDouble());");
            sb.AppendLine("            return list.ToArray();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static bool[] ReadBoolArray(ref Utf8JsonReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine("            var list = new System.Collections.Generic.List<bool>();");
            sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)");
            sb.AppendLine("                list.Add(reader.GetBoolean());");
            sb.AppendLine("            return list.ToArray();");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void GenerateWriteMethod(StringBuilder sb, AssetTypeInfo type, HashSet<string> allAssetTypes)
        {
            sb.AppendLine($"        private static void Write{type.Name}(Utf8JsonWriter writer, {type.FullyQualifiedName} obj)");
            sb.AppendLine("        {");
            sb.AppendLine("            writer.WriteStartObject();");

            foreach (var prop in type.Properties)
            {
                var jsonName = ToCamelCase(prop.Name);

                switch (prop.JsonType)
                {
                    case JsonType.String:
                        sb.AppendLine($"            writer.WriteString(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.Int:
                        sb.AppendLine($"            writer.WriteNumber(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.UInt:
                        sb.AppendLine($"            writer.WriteNumber(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.Long:
                        sb.AppendLine($"            writer.WriteNumber(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.ULong:
                        sb.AppendLine($"            writer.WriteNumber(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.Float:
                        sb.AppendLine($"            writer.WriteNumber(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.Double:
                        sb.AppendLine($"            writer.WriteNumber(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.Bool:
                        sb.AppendLine($"            writer.WriteBoolean(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.ByteArray:
                        sb.AppendLine($"            writer.WriteBase64String(\"{jsonName}\", obj.{prop.Name});");
                        break;
                    case JsonType.StringArray:
                        sb.AppendLine($"            writer.WritePropertyName(\"{jsonName}\");");
                        sb.AppendLine("            writer.WriteStartArray();");
                        sb.AppendLine($"            foreach (var item in obj.{prop.Name} ?? Array.Empty<string>())");
                        sb.AppendLine("                writer.WriteStringValue(item);");
                        sb.AppendLine("            writer.WriteEndArray();");
                        break;
                    case JsonType.IntArray:
                        sb.AppendLine($"            writer.WritePropertyName(\"{jsonName}\");");
                        sb.AppendLine("            writer.WriteStartArray();");
                        sb.AppendLine($"            foreach (var item in obj.{prop.Name} ?? Array.Empty<int>())");
                        sb.AppendLine("                writer.WriteNumberValue(item);");
                        sb.AppendLine("            writer.WriteEndArray();");
                        break;
                    case JsonType.UIntArray:
                        sb.AppendLine($"            writer.WritePropertyName(\"{jsonName}\");");
                        sb.AppendLine("            writer.WriteStartArray();");
                        sb.AppendLine($"            foreach (var item in obj.{prop.Name} ?? Array.Empty<uint>())");
                        sb.AppendLine("                writer.WriteNumberValue(item);");
                        sb.AppendLine("            writer.WriteEndArray();");
                        break;
                    case JsonType.FloatArray:
                        sb.AppendLine($"            writer.WritePropertyName(\"{jsonName}\");");
                        sb.AppendLine("            writer.WriteStartArray();");
                        sb.AppendLine($"            foreach (var item in obj.{prop.Name} ?? Array.Empty<float>())");
                        sb.AppendLine("                writer.WriteNumberValue(item);");
                        sb.AppendLine("            writer.WriteEndArray();");
                        break;
                    case JsonType.DoubleArray:
                        sb.AppendLine($"            writer.WritePropertyName(\"{jsonName}\");");
                        sb.AppendLine("            writer.WriteStartArray();");
                        sb.AppendLine($"            foreach (var item in obj.{prop.Name} ?? Array.Empty<double>())");
                        sb.AppendLine("                writer.WriteNumberValue(item);");
                        sb.AppendLine("            writer.WriteEndArray();");
                        break;
                    case JsonType.BoolArray:
                        sb.AppendLine($"            writer.WritePropertyName(\"{jsonName}\");");
                        sb.AppendLine("            writer.WriteStartArray();");
                        sb.AppendLine($"            foreach (var item in obj.{prop.Name} ?? Array.Empty<bool>())");
                        sb.AppendLine("                writer.WriteBooleanValue(item);");
                        sb.AppendLine("            writer.WriteEndArray();");
                        break;
                    case JsonType.ObjectArray:
                        // Check if element type is a known IAsset type
                        if (allAssetTypes.Contains(prop.ArrayElementType))
                        {
                            var elemName = GetSimpleName(prop.ArrayElementType);
                            sb.AppendLine($"            writer.WritePropertyName(\"{jsonName}\");");
                            sb.AppendLine("            writer.WriteStartArray();");
                            sb.AppendLine($"            foreach (var item in obj.{prop.Name} ?? Array.Empty<{prop.ArrayElementType}>())");
                            sb.AppendLine($"                Write{elemName}(writer, item);");
                            sb.AppendLine("            writer.WriteEndArray();");
                        }
                        else
                        {
                            sb.AppendLine($"            // TODO: Unknown type {prop.TypeName} for property {prop.Name}");
                            sb.AppendLine($"            writer.WriteNull(\"{jsonName}\");");
                        }
                        break;
                    default:
                        sb.AppendLine($"            // TODO: Complex type {prop.TypeName} for property {prop.Name}");
                        sb.AppendLine($"            writer.WriteNull(\"{jsonName}\");");
                        break;
                }
            }

            sb.AppendLine("            writer.WriteEndObject();");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static string GetSimpleName(string fullyQualifiedName)
        {
            var lastDot = fullyQualifiedName.LastIndexOf('.');
            if (lastDot >= 0)
                return fullyQualifiedName.Substring(lastDot + 1);
            // Handle global:: prefix
            if (fullyQualifiedName.StartsWith("global::"))
            {
                var withoutGlobal = fullyQualifiedName.Substring(8);
                lastDot = withoutGlobal.LastIndexOf('.');
                if (lastDot >= 0)
                    return withoutGlobal.Substring(lastDot + 1);
                return withoutGlobal;
            }
            return fullyQualifiedName;
        }

        private static void GenerateReadMethod(StringBuilder sb, AssetTypeInfo type, HashSet<string> allAssetTypes)
        {
            sb.AppendLine($"        private static {type.FullyQualifiedName} Read{type.Name}(ref Utf8JsonReader reader)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var obj = new {type.FullyQualifiedName}();");
            sb.AppendLine();
            sb.AppendLine("            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)");
            sb.AppendLine("                throw new JsonException(\"Expected StartObject\");");
            sb.AppendLine();
            sb.AppendLine("            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (reader.TokenType != JsonTokenType.PropertyName)");
            sb.AppendLine("                    continue;");
            sb.AppendLine();
            sb.AppendLine("                var propName = reader.GetString();");
            sb.AppendLine("                reader.Read();");
            sb.AppendLine();
            sb.AppendLine("                switch (propName)");
            sb.AppendLine("                {");

            foreach (var prop in type.Properties)
            {
                var jsonName = ToCamelCase(prop.Name);
                sb.AppendLine($"                    case \"{jsonName}\":");

                switch (prop.JsonType)
                {
                    case JsonType.String:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetString() ?? string.Empty;");
                        break;
                    case JsonType.Int:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetInt32();");
                        break;
                    case JsonType.UInt:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetUInt32();");
                        break;
                    case JsonType.Long:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetInt64();");
                        break;
                    case JsonType.ULong:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetUInt64();");
                        break;
                    case JsonType.Float:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetSingle();");
                        break;
                    case JsonType.Double:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetDouble();");
                        break;
                    case JsonType.Bool:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetBoolean();");
                        break;
                    case JsonType.ByteArray:
                        sb.AppendLine($"                        obj.{prop.Name} = reader.GetBytesFromBase64();");
                        break;
                    case JsonType.StringArray:
                        sb.AppendLine($"                        obj.{prop.Name} = ReadStringArray(ref reader);");
                        break;
                    case JsonType.IntArray:
                        sb.AppendLine($"                        obj.{prop.Name} = ReadIntArray(ref reader);");
                        break;
                    case JsonType.UIntArray:
                        sb.AppendLine($"                        obj.{prop.Name} = ReadUIntArray(ref reader);");
                        break;
                    case JsonType.FloatArray:
                        sb.AppendLine($"                        obj.{prop.Name} = ReadFloatArray(ref reader);");
                        break;
                    case JsonType.DoubleArray:
                        sb.AppendLine($"                        obj.{prop.Name} = ReadDoubleArray(ref reader);");
                        break;
                    case JsonType.BoolArray:
                        sb.AppendLine($"                        obj.{prop.Name} = ReadBoolArray(ref reader);");
                        break;
                    case JsonType.ObjectArray:
                        // Check if element type is a known IAsset type
                        if (allAssetTypes.Contains(prop.ArrayElementType))
                        {
                            var elemName = GetSimpleName(prop.ArrayElementType);
                            sb.AppendLine($"                        obj.{prop.Name} = Read{elemName}Array(ref reader);");
                        }
                        else
                        {
                            sb.AppendLine($"                        reader.Skip(); // TODO: Unknown type {prop.TypeName}");
                        }
                        break;
                    default:
                        sb.AppendLine($"                        reader.Skip(); // TODO: Complex type {prop.TypeName}");
                        break;
                }

                sb.AppendLine("                        break;");
            }

            sb.AppendLine("                    default:");
            sb.AppendLine("                        reader.Skip();");
            sb.AppendLine("                        break;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            return obj;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static string GenerateContent(List<AssetTypeInfo> types, string rootNamespace)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using DerpLib.AssetPipeline;");
            sb.AppendLine("using DerpLib.Vfs;");
            sb.AppendLine("using Serilog;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}");
            sb.AppendLine("{");

            // ContentModule
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Factory for creating a fully configured ContentManager.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static partial class ContentModule");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Gets the auto-generated serializer for all IAsset types.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static IBlobSerializer Serializer => ContentSerializer.Instance;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Creates the auto-generated serializer for all IAsset types.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static IBlobSerializer CreateSerializer() => ContentSerializer.Instance;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Creates a ContentManager with all IAsset types registered.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static partial ContentManager CreateContentManager(VirtualFileSystem vfs, ILogger? logger)");
            sb.AppendLine("        {");
            sb.AppendLine("            var cm = new ContentManager(vfs, ContentSerializer.Instance, new DefaultContentReaderRegistry(), null, null, logger);");

            foreach (var type in types)
            {
                sb.AppendLine($"            cm.RegisterType<{type.FullyQualifiedName}>();");
            }

            sb.AppendLine("            return cm;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Content static facade
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Static facade for content loading.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class Content");
            sb.AppendLine("    {");
            sb.AppendLine("        private static ContentManager? _manager;");
            sb.AppendLine();
            sb.AppendLine("        public static bool IsInitialized => _manager != null;");
            sb.AppendLine();
            sb.AppendLine("        public static ContentManager Manager => _manager ?? throw new InvalidOperationException(\"Content not initialized. Call Content.Initialize() first.\");");
            sb.AppendLine();
            sb.AppendLine("        public static void Initialize(VirtualFileSystem vfs, ILogger? logger = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            _manager = ContentModule.CreateContentManager(vfs, logger);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public static void Initialize(string packagePath, ILogger? logger = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            var vfs = new VirtualFileSystem(logger ?? Log.Logger);");
            sb.AppendLine("            vfs.RegisterProvider(new PackageFileProvider(\"/data/db\", packagePath));");
            sb.AppendLine("            _manager = ContentModule.CreateContentManager(vfs, logger);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public static T Load<T>(string url) => Manager.Load<T>(url);");
            sb.AppendLine();
            sb.AppendLine("        public static byte[] LoadBytes(string url) => Manager.LoadBytes(url);");
            sb.AppendLine();
            sb.AppendLine("        public static bool Exists(string url) => Manager.Exists(url);");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }

    internal enum JsonType
    {
        String,
        Int,
        UInt,
        Long,
        ULong,
        Float,
        Double,
        Bool,
        Byte,
        ByteArray,
        StringArray,
        IntArray,
        UIntArray,
        FloatArray,
        DoubleArray,
        BoolArray,
        ObjectArray,
        Object
    }

    internal class PropertyInfo
    {
        public string Name { get; }
        public string TypeName { get; }
        public JsonType JsonType { get; }
        public bool IsArray { get; }
        public string ArrayElementType { get; }

        public PropertyInfo(string name, string typeName, JsonType jsonType, bool isArray, string arrayElementType)
        {
            Name = name;
            TypeName = typeName;
            JsonType = jsonType;
            IsArray = isArray;
            ArrayElementType = arrayElementType;
        }
    }

    internal class AssetTypeInfo : IEquatable<AssetTypeInfo>
    {
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public string Namespace { get; }
        public List<PropertyInfo> Properties { get; }

        public AssetTypeInfo(string fullyQualifiedName, string name, string ns, List<PropertyInfo> properties)
        {
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            Namespace = ns;
            Properties = properties;
        }

        public bool Equals(AssetTypeInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedName == other.FullyQualifiedName;
        }

        public override bool Equals(object? obj) => Equals(obj as AssetTypeInfo);

        public override int GetHashCode() => FullyQualifiedName.GetHashCode();
    }
}
