using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#pragma warning disable RS1035 // Do not use banned APIs for analyzers

namespace GameDocDatabase.Generator;

[Generator]
public sealed class GameDocDbGenerator : IIncrementalGenerator
{
    private const string GameDocTableAttributeName = "GameDocDatabase.GameDocTableAttribute";
    private const string PrimaryKeyAttributeName = "GameDocDatabase.PrimaryKeyAttribute";
    private const string SecondaryKeyAttributeName = "GameDocDatabase.SecondaryKeyAttribute";
    private const string NonUniqueAttributeName = "GameDocDatabase.NonUniqueAttribute";
    private const string ForeignKeyAttributeName = "GameDocDatabase.ForeignKeyAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var tables = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GameDocTableAttributeName,
                predicate: static (node, _) => node is StructDeclarationSyntax,
                transform: TransformTable)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Collect JSON files for registry generation
        var jsonFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, ct) => (
                FileName: Path.GetFileNameWithoutExtension(file.Path),
                Content: file.GetText(ct)?.ToString() ?? ""
            ))
            .Collect();

        // Combine tables and JSON files
        var combined = tables.Collect().Combine(jsonFiles);

        context.RegisterSourceOutput(combined, GenerateCode);
    }

    private static TableSchemaModel? TransformTable(
        GeneratorAttributeSyntaxContext context,
        System.Threading.CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var attr = context.Attributes.FirstOrDefault();
        if (attr is null)
            return null;

        string tableName = attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value?.ToString() ?? typeSymbol.Name
            : typeSymbol.Name;

        int version = 1;
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == "Version" && namedArg.Value.Value is int v)
                version = v;
        }

        var model = new TableSchemaModel(typeSymbol, tableName, version);

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IFieldSymbol field || field.IsStatic || field.IsConst)
                continue;

            int size = GetTypeSize(field.Type);
            var fieldInfo = new FieldInfo(field.Name, field.Type, size);

            foreach (var fieldAttr in field.GetAttributes())
            {
                var attrName = fieldAttr.AttributeClass?.ToDisplayString();

                if (attrName == PrimaryKeyAttributeName)
                {
                    fieldInfo.IsPrimaryKey = true;
                    model.PrimaryKey = fieldInfo;
                }
                else if (attrName == SecondaryKeyAttributeName)
                {
                    fieldInfo.IsSecondaryKey = true;
                    if (fieldAttr.ConstructorArguments.Length > 0 &&
                        fieldAttr.ConstructorArguments[0].Value is int idx)
                    {
                        fieldInfo.SecondaryKeyIndex = idx;
                    }
                }
                else if (attrName == NonUniqueAttributeName)
                {
                    fieldInfo.IsNonUnique = true;
                }
                else if (attrName == ForeignKeyAttributeName)
                {
                    if (fieldAttr.ConstructorArguments.Length > 0 &&
                        fieldAttr.ConstructorArguments[0].Value is INamedTypeSymbol refTable)
                    {
                        fieldInfo.ForeignKeyTable = refTable;
                    }
                }
            }

            model.Fields.Add(fieldInfo);

            if (fieldInfo.IsSecondaryKey)
            {
                model.SecondaryKeys.Add(new SecondaryKeyInfo(
                    fieldInfo,
                    fieldInfo.SecondaryKeyIndex,
                    fieldInfo.IsNonUnique));
            }
        }

        return model;
    }

    private static int GetTypeSize(ITypeSymbol type)
    {
        var name = type.ToDisplayString();
        return name switch
        {
            "int" or "System.Int32" => 4,
            "uint" or "System.UInt32" => 4,
            "long" or "System.Int64" => 8,
            "ulong" or "System.UInt64" => 8,
            "short" or "System.Int16" => 2,
            "ushort" or "System.UInt16" => 2,
            "byte" or "System.Byte" => 1,
            "sbyte" or "System.SByte" => 1,
            "float" or "System.Single" => 4,
            "double" or "System.Double" => 8,
            "bool" or "System.Boolean" => 1,
            _ when name.Contains("Fixed64") => 8,
            _ when name.Contains("Fixed32") => 4,
            _ when name.Contains("GameDataId") => 4,
            _ when name.Contains("StringHandle") => 4,  // StringHandle is uint internally
            _ => 4 // Default assumption for enums, etc.
        };
    }

    private static void GenerateCode(
        SourceProductionContext context,
        (ImmutableArray<TableSchemaModel> Tables, ImmutableArray<(string FileName, string Content)> JsonFiles) input)
    {
        var (tables, jsonFiles) = input;

        if (tables.IsEmpty)
            return;

        // Build a lookup from table name to JSON data
        var jsonDataByTableName = new Dictionary<string, List<(int Id, string Name)>>();
        foreach (var (fileName, content) in jsonFiles)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            try
            {
                var entries = ParseJsonEntries(content);
                if (entries.Count > 0)
                {
                    jsonDataByTableName[fileName] = entries;
                }
            }
            catch
            {
                // Skip malformed JSON files
            }
        }

        var tablesList = tables.ToList();

        foreach (var table in tables)
        {
            var tableCode = TableRenderer.Render(table);
            context.AddSource($"{table.TypeName}Table.g.cs", tableCode);

            // Generate serialization code for each table
            var serializerCode = BinarySerializerRenderer.Render(table);
            context.AddSource($"{table.TypeName}Serialization.g.cs", serializerCode);

            // Generate registry if we have matching JSON data
            if (jsonDataByTableName.TryGetValue(table.TableName, out var entries))
            {
                var registryCode = RegistryRenderer.Render(table, entries);
                var className = table.TypeName.EndsWith("Data")
                    ? table.TypeName.Substring(0, table.TypeName.Length - 4) + "Ids"
                    : table.TypeName + "Ids";
                context.AddSource($"{className}.g.cs", registryCode);
            }
        }

        // Generate GameDocDb container
        var ns = tables.First().Namespace;
        var dbCode = GenerateDbContainer(tables);
        context.AddSource("GameDocDb.g.cs", dbCode);

        // Generate binary builder (JSON -> .bin)
        var builderCode = BinaryBuilderRenderer.Render(tablesList, ns);
        context.AddSource("GameDataBinaryBuilder.g.cs", builderCode);

        // Generate binary loader (.bin -> GameDocDb)
        var loaderCode = BinaryLoaderRenderer.Render(tablesList, ns);
        context.AddSource("GameDataBinaryLoader.g.cs", loaderCode);
    }

    /// <summary>
    /// Parses JSON array to extract Id and Name fields.
    /// </summary>
    private static List<(int Id, string Name)> ParseJsonEntries(string json)
    {
        var result = new List<(int, string)>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            int? id = null;
            string? name = null;

            if (element.TryGetProperty("Id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                id = idProp.GetInt32();

            if (element.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                name = nameProp.GetString();

            if (id.HasValue && !string.IsNullOrEmpty(name))
                result.Add((id.Value, name));
        }

        return result;
    }

    private static string GenerateDbContainer(ImmutableArray<TableSchemaModel> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using GameDocDatabase;");
        sb.AppendLine();

        // Use the namespace of the first table
        var ns = tables.First().Namespace;
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        sb.AppendLine("public sealed class GameDocDb");
        sb.AppendLine("{");

        // Table properties
        foreach (var table in tables)
        {
            sb.AppendLine($"    public {table.TypeName}Table {table.TypeName} {{ get; }}");
        }

        sb.AppendLine();

        // Constructor
        sb.AppendLine("    public GameDocDb(");
        for (int i = 0; i < tables.Length; i++)
        {
            var comma = i < tables.Length - 1 ? "," : "";
            sb.AppendLine($"        {tables[i].TypeName}Table {ToCamelCase(tables[i].TypeName)}{comma}");
        }
        sb.AppendLine("    )");
        sb.AppendLine("    {");
        foreach (var table in tables)
        {
            sb.AppendLine($"        {table.TypeName} = {ToCamelCase(table.TypeName)};");
        }
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
