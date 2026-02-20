using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string SchemaSuffix = ".derpecs.json";
    private const string PropertyAttributeFqn = "global::Property.Property";
    private const string PropertyFlagsFqn = "global::Property.PropertyFlags";
    private const string PropertyKindFqn = "global::Property.PropertyKind";
    private const string EditorResizableAttributeFqn = "global::DerpLib.Ecs.Editor.EditorResizable";
    private const string ListHandleOpenType = "global::DerpLib.Ecs.ListHandle<";

    public static int Main(string[] args)
    {
        if (!TryParseArgs(args, out List<string> inputFiles, out string? outputDirectory, out string? error))
        {
            Console.Error.WriteLine(error ?? "Invalid arguments.");
            PrintUsage();
            return 2;
        }

        Directory.CreateDirectory(outputDirectory!);
        DeleteOldOutputs(outputDirectory!);

        var allComponents = new List<ComponentSpec>(32);
        var allEnums = new List<EnumSpec>(16);
        var allWorlds = new List<WorldSpec>(8);
        var allKinds = new List<KindSpec>(8);
        for (int i = 0; i < inputFiles.Count; i++)
        {
            string inputPath = Path.GetFullPath(inputFiles[i]);
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine("Input file not found: " + inputPath);
                return 2;
            }

            string text = File.ReadAllText(inputPath);
            if (!TryParseSchemaFile(inputPath, text, allComponents, allEnums, allWorlds, allKinds, out error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        allComponents.Sort(static (a, b) => string.CompareOrdinal(a.FullyQualifiedTypeName, b.FullyQualifiedTypeName));
        allEnums.Sort(static (a, b) => string.CompareOrdinal(a.FullyQualifiedTypeName, b.FullyQualifiedTypeName));
        allWorlds.Sort(static (a, b) => string.CompareOrdinal(a.FullyQualifiedTypeName, b.FullyQualifiedTypeName));
        allKinds.Sort(static (a, b) => string.CompareOrdinal(a.FullyQualifiedTypeName, b.FullyQualifiedTypeName));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < allComponents.Count; i++)
        {
            ComponentSpec component = allComponents[i];
            if (!seen.Add(component.FullyQualifiedTypeName))
            {
                Console.Error.WriteLine("Duplicate component type: " + component.FullyQualifiedTypeName);
                return 1;
            }

            string fileName = component.TypeName + ".UgcComponent.g.cs";
            string outputPath = Path.Combine(outputDirectory!, fileName);
            File.WriteAllText(outputPath, EmitComponent(component), Encoding.UTF8);
        }

        seen.Clear();
        for (int i = 0; i < allEnums.Count; i++)
        {
            EnumSpec e = allEnums[i];
            if (!seen.Add(e.FullyQualifiedTypeName))
            {
                Console.Error.WriteLine("Duplicate enum type: " + e.FullyQualifiedTypeName);
                return 1;
            }

            string fileName = e.TypeName + ".UgcEnum.g.cs";
            string outputPath = Path.Combine(outputDirectory!, fileName);
            File.WriteAllText(outputPath, EmitEnum(e), Encoding.UTF8);
        }

        seen.Clear();
        for (int i = 0; i < allKinds.Count; i++)
        {
            KindSpec kind = allKinds[i];
            if (!seen.Add(kind.FullyQualifiedTypeName))
            {
                continue;
            }

            string fileName = kind.TypeName + ".UgcKind.g.cs";
            string outputPath = Path.Combine(outputDirectory!, fileName);
            File.WriteAllText(outputPath, EmitKind(kind), Encoding.UTF8);
        }

        seen.Clear();
        for (int i = 0; i < allWorlds.Count; i++)
        {
            WorldSpec world = allWorlds[i];
            if (!seen.Add(world.FullyQualifiedTypeName))
            {
                Console.Error.WriteLine("Duplicate world type: " + world.FullyQualifiedTypeName);
                return 1;
            }

            string fileName = world.TypeName + ".UgcWorldSetup.g.cs";
            string outputPath = Path.Combine(outputDirectory!, fileName);
            File.WriteAllText(outputPath, EmitWorldSetup(world), Encoding.UTF8);

            if (world.HasBakedContent)
            {
                string bakedFileName = world.TypeName + ".UgcBaked.g.cs";
                string bakedOutputPath = Path.Combine(outputDirectory!, bakedFileName);
                File.WriteAllText(bakedOutputPath, EmitWorldBaked(world), Encoding.UTF8);

                string binaryFileName = world.TypeName + ".derpentitydata";
                string binaryOutputPath = Path.Combine(outputDirectory!, binaryFileName);
                WriteDerpEntityDataBinary(world, allEnums, binaryOutputPath);
            }
        }

        return 0;
    }

    private static void DeleteOldOutputs(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return;
        }

        try
        {
            string[] files = Directory.GetFiles(outputDirectory, "*.Ugc*.g.cs", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                File.Delete(files[i]);
            }

            string[] dataFiles = Directory.GetFiles(outputDirectory, "*.derpentitydata", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < dataFiles.Length; i++)
            {
                File.Delete(dataFiles[i]);
            }
        }
        catch
        {
            // Best effort. Any failure will surface as compile errors later.
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  DerpEcs.UgcCodegen --out <dir> --input <schema.derpecs.json> [--input <...>]");
    }

    private static bool TryParseArgs(string[] args, out List<string> inputFiles, out string? outputDirectory, out string? error)
    {
        inputFiles = new List<string>(4);
        outputDirectory = null;
        error = null;

        for (int argIndex = 0; argIndex < args.Length; argIndex++)
        {
            string arg = args[argIndex];
            if (arg == "--out" && argIndex + 1 < args.Length)
            {
                outputDirectory = args[++argIndex];
                continue;
            }

            if (arg == "--input" && argIndex + 1 < args.Length)
            {
                inputFiles.Add(args[++argIndex]);
                continue;
            }

            error = "Unknown or incomplete argument: " + arg;
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            error = "Missing required --out <dir>.";
            return false;
        }

        if (inputFiles.Count == 0)
        {
            error = "Missing at least one --input <schema.derpecs.json>.";
            return false;
        }

        for (int i = 0; i < inputFiles.Count; i++)
        {
            if (!inputFiles[i].EndsWith(SchemaSuffix, StringComparison.OrdinalIgnoreCase))
            {
                error = "Input file must end with '" + SchemaSuffix + "': " + inputFiles[i];
                return false;
            }
        }

        return true;
    }

    private static bool TryParseSchemaFile(
        string path,
        string text,
        List<ComponentSpec> components,
        List<EnumSpec> enums,
        List<WorldSpec> worlds,
        List<KindSpec> kinds,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "UGC schema file was empty: " + path;
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (Exception ex)
        {
            error = "Invalid JSON in '" + path + "': " + ex.Message;
            return false;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Root must be a JSON object: " + path;
                return false;
            }

            string? defaultNamespace = ReadOptionalString(root, "namespace");
            if (string.IsNullOrWhiteSpace(defaultNamespace))
            {
                error = "Missing required file-level 'namespace' string: " + path;
                return false;
            }

            bool parsedAnything = false;

            if (root.TryGetProperty("enums", out JsonElement enumsElem) && enumsElem.ValueKind == JsonValueKind.Array)
            {
                parsedAnything = true;
                if (!TryParseEnums(path, defaultNamespace, enumsElem, enums, out error))
                {
                    return false;
                }
            }

            if (root.TryGetProperty("components", out JsonElement comps) && comps.ValueKind == JsonValueKind.Array)
            {
                parsedAnything = true;
                if (!TryParseComponents(path, defaultNamespace, comps, components, out error))
                {
                    return false;
                }
            }

            if (root.TryGetProperty("worlds", out JsonElement worldsElem) && worldsElem.ValueKind == JsonValueKind.Array)
            {
                parsedAnything = true;
                if (!TryParseWorlds(path, defaultNamespace, worldsElem, components, worlds, kinds, out error))
                {
                    return false;
                }
            }

            if (!parsedAnything)
            {
                error = "Schema must contain 'components' and/or 'worlds': " + path;
                return false;
            }
        }

        return true;
    }

    private static bool TryParseEnums(string path, string defaultNamespace, JsonElement enumsElem, List<EnumSpec> enums, out string? error)
    {
        error = null;

        for (int enumIndex = 0; enumIndex < enumsElem.GetArrayLength(); enumIndex++)
        {
            JsonElement e = enumsElem[enumIndex];
            if (e.ValueKind != JsonValueKind.Object)
            {
                error = "Enum entry must be an object: " + path;
                return false;
            }

            string? name = ReadRequiredString(e, "name", out error);
            if (name == null)
            {
                error = "Enum missing required 'name': " + path + " (" + error + ")";
                return false;
            }

            if (!IsValidIdentifier(name))
            {
                error = "Enum name '" + name + "' is not a valid C# identifier: " + path;
                return false;
            }

            string? ns = ReadOptionalString(e, "namespace") ?? defaultNamespace;
            if (string.IsNullOrWhiteSpace(ns))
            {
                error = "Enum '" + name + "' missing namespace (and no file-level default): " + path;
                return false;
            }
            ns = ns.Trim();

            string underlying = ReadOptionalString(e, "underlyingType") ?? "int";
            string lowerUnderlying = underlying.Trim().ToLowerInvariant();
            string underlyingTypeName;
            switch (lowerUnderlying)
            {
                case "byte":
                    underlyingTypeName = "byte";
                    break;
                case "sbyte":
                    underlyingTypeName = "sbyte";
                    break;
                case "short":
                case "int16":
                    underlyingTypeName = "short";
                    break;
                case "ushort":
                case "uint16":
                    underlyingTypeName = "ushort";
                    break;
                case "int":
                case "int32":
                    underlyingTypeName = "int";
                    break;
                case "uint":
                case "uint32":
                    underlyingTypeName = "uint";
                    break;
                default:
                    error = "Enum '" + name + "' has unsupported underlyingType '" + underlying + "': " + path;
                    return false;
            }

            bool isFlags = ReadOptionalBool(e, "flags", defaultValue: false);

            if (!e.TryGetProperty("values", out JsonElement valuesElem) || valuesElem.ValueKind != JsonValueKind.Array)
            {
                error = "Enum '" + name + "' missing required array property 'values': " + path;
                return false;
            }

            var values = new List<EnumValueSpec>(valuesElem.GetArrayLength());
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            for (int valueIndex = 0; valueIndex < valuesElem.GetArrayLength(); valueIndex++)
            {
                JsonElement v = valuesElem[valueIndex];
                if (v.ValueKind != JsonValueKind.Object)
                {
                    error = "Enum value entry must be an object: " + path;
                    return false;
                }

                string? valueName = ReadRequiredString(v, "name", out error);
                if (valueName == null)
                {
                    error = "Enum '" + name + "' value missing required 'name': " + path + " (" + error + ")";
                    return false;
                }

                if (!IsValidIdentifier(valueName))
                {
                    error = "Enum '" + name + "' value name '" + valueName + "' is not a valid C# identifier: " + path;
                    return false;
                }

                if (!seenNames.Add(valueName))
                {
                    error = "Enum '" + name + "' has duplicate value name '" + valueName + "': " + path;
                    return false;
                }

                if (!v.TryGetProperty("value", out JsonElement valueElem) || valueElem.ValueKind != JsonValueKind.Number)
                {
                    error = "Enum '" + name + "' value '" + valueName + "' missing required numeric 'value': " + path;
                    return false;
                }

                if (!valueElem.TryGetInt64(out long value))
                {
                    error = "Enum '" + name + "' value '" + valueName + "' 'value' must fit in int64: " + path;
                    return false;
                }

                values.Add(new EnumValueSpec(valueName, value));
            }

            values.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            enums.Add(new EnumSpec(path, ns, name, underlyingTypeName, isFlags, values.ToArray()));
        }

        return true;
    }

    private static bool TryParseComponents(string path, string defaultNamespace, JsonElement comps, List<ComponentSpec> components, out string? error)
    {
        error = null;

        for (int componentIndex = 0; componentIndex < comps.GetArrayLength(); componentIndex++)
        {
            JsonElement component = comps[componentIndex];
            if (component.ValueKind != JsonValueKind.Object)
            {
                error = "Component entry must be an object: " + path;
                return false;
            }

            string? name = ReadRequiredString(component, "name", out error);
            if (name == null)
            {
                error = "Component missing required 'name': " + path + " (" + error + ")";
                return false;
            }

            string? ns = ReadOptionalString(component, "namespace") ?? defaultNamespace;
            if (string.IsNullOrWhiteSpace(ns))
            {
                error = "Component '" + name + "' missing namespace (and no file-level default): " + path;
                return false;
            }
            ns = ns.Trim();

            if (!IsValidIdentifier(name))
            {
                error = "Component name '" + name + "' is not a valid C# identifier: " + path;
                return false;
            }

            string typeName = name.EndsWith("Component", StringComparison.Ordinal) ? name : name + "Component";

            if (!component.TryGetProperty("fields", out JsonElement fieldsElem) || fieldsElem.ValueKind != JsonValueKind.Array)
            {
                error = "Component '" + name + "' missing required array property 'fields': " + path;
                return false;
            }

            var fields = new List<FieldSpec>(fieldsElem.GetArrayLength());
            for (int fieldIndex = 0; fieldIndex < fieldsElem.GetArrayLength(); fieldIndex++)
            {
                JsonElement field = fieldsElem[fieldIndex];
                if (field.ValueKind != JsonValueKind.Object)
                {
                    error = "Field entry must be an object: " + path;
                    return false;
                }

                string? fieldName = ReadRequiredString(field, "name", out error);
                if (fieldName == null)
                {
                    error = "Field missing required 'name': " + path + " (" + error + ")";
                    return false;
                }

                if (!IsValidIdentifier(fieldName))
                {
                    error = "Field name '" + fieldName + "' is not a valid C# identifier: " + path;
                    return false;
                }

                string? schemaType = ReadRequiredString(field, "type", out error);
                if (schemaType == null)
                {
                    error = "Field '" + fieldName + "' missing required 'type': " + path + " (" + error + ")";
                    return false;
                }

                bool editorResizable = ReadOptionalBool(field, "editorResizable", defaultValue: false);
                if (!TryMapFieldType(schemaType, out string mappedType, out bool isListHandle))
                {
                    error = "Field '" + fieldName + "' has unsupported type '" + schemaType + "': " + path;
                    return false;
                }

                if (editorResizable && !isListHandle)
                {
                    error = "Field '" + fieldName + "' set editorResizable=true but type was not a ListHandle<T>: " + path;
                    return false;
                }

                bool isProperty = ReadOptionalBool(field, "property", defaultValue: true);
                if (editorResizable && isProperty)
                {
                    error = "Field '" + fieldName + "' cannot be both editorResizable and property: " + path;
                    return false;
                }

                PropertyAttr propertyAttr = PropertyAttr.FromJson(field);
                fields.Add(new FieldSpec(fieldName, mappedType, isProperty, editorResizable, propertyAttr));
            }

            fields.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            for (int fieldIndex = 1; fieldIndex < fields.Count; fieldIndex++)
            {
                if (string.Equals(fields[fieldIndex - 1].Name, fields[fieldIndex].Name, StringComparison.Ordinal))
                {
                    error = "Component '" + name + "' has duplicate field '" + fields[fieldIndex].Name + "': " + path;
                    return false;
                }
            }

            components.Add(new ComponentSpec(
                sourcePath: path,
                @namespace: ns,
                typeName: typeName,
                fields: fields.ToArray()));
        }

        return true;
    }

    private static bool TryParseWorlds(string path, string defaultNamespace, JsonElement worldsElem, List<ComponentSpec> components, List<WorldSpec> worlds, List<KindSpec> kinds, out string? error)
    {
        error = null;

        for (int worldIndex = 0; worldIndex < worldsElem.GetArrayLength(); worldIndex++)
        {
            JsonElement world = worldsElem[worldIndex];
            if (world.ValueKind != JsonValueKind.Object)
            {
                error = "World entry must be an object: " + path;
                return false;
            }

            string? worldName = ReadRequiredString(world, "name", out error);
            if (worldName == null)
            {
                error = "World missing required 'name': " + path + " (" + error + ")";
                return false;
            }

            if (!IsValidIdentifier(worldName))
            {
                error = "World name '" + worldName + "' is not a valid C# identifier: " + path;
                return false;
            }

            string? ns = ReadOptionalString(world, "namespace") ?? defaultNamespace;
            if (string.IsNullOrWhiteSpace(ns))
            {
                error = "World '" + worldName + "' missing namespace (and no file-level default): " + path;
                return false;
            }
            ns = ns.Trim();

            bool isSealed = ReadOptionalBool(world, "sealed", defaultValue: true);

            if (!world.TryGetProperty("archetypes", out JsonElement archetypesElem) || archetypesElem.ValueKind != JsonValueKind.Array)
            {
                error = "World '" + worldName + "' missing required array property 'archetypes': " + path;
                return false;
            }

            var archetypes = new List<ArchetypeSpec>(archetypesElem.GetArrayLength());
            int nextListBakeId = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypesElem.GetArrayLength(); archetypeIndex++)
            {
                JsonElement archetype = archetypesElem[archetypeIndex];
                if (archetype.ValueKind != JsonValueKind.Object)
                {
                    error = "Archetype entry must be an object: " + path;
                    return false;
                }

                string? kindName = ReadRequiredString(archetype, "kind", out error);
                if (kindName == null)
                {
                    error = "Archetype missing required 'kind': " + path + " (" + error + ")";
                    return false;
                }

                if (!IsValidIdentifier(kindName))
                {
                    error = "Kind name '" + kindName + "' is not a valid C# identifier: " + path;
                    return false;
                }

                if (!TryReadRequiredInt(archetype, "capacity", out int capacity))
                {
                    error = "Archetype kind '" + kindName + "' missing required int 'capacity': " + path;
                    return false;
                }

                int spawnQueueCapacity = ReadOptionalInt(archetype, "spawnQueueCapacity", defaultValue: 0);
                int destroyQueueCapacity = ReadOptionalInt(archetype, "destroyQueueCapacity", defaultValue: 0);

                if (!archetype.TryGetProperty("components", out JsonElement componentsElem) || componentsElem.ValueKind != JsonValueKind.Array)
                {
                    error = "Archetype kind '" + kindName + "' missing required array property 'components': " + path;
                    return false;
                }

                var componentTypeNames = new List<string>(componentsElem.GetArrayLength());
                for (int compIndex = 0; compIndex < componentsElem.GetArrayLength(); compIndex++)
                {
                    JsonElement comp = componentsElem[compIndex];
                    if (comp.ValueKind != JsonValueKind.String)
                    {
                        error = "Archetype kind '" + kindName + "' components must be strings: " + path;
                        return false;
                    }

                    string? compName = comp.GetString();
                    if (string.IsNullOrWhiteSpace(compName))
                    {
                        error = "Archetype kind '" + kindName + "' component name must be non-empty: " + path;
                        return false;
                    }

                    compName = compName.Trim();
                    if (!IsValidIdentifier(compName))
                    {
                        error = "Component type name '" + compName + "' is not a valid identifier: " + path;
                        return false;
                    }

                    string typeName = compName.EndsWith("Component", StringComparison.Ordinal) ? compName : compName + "Component";
                    componentTypeNames.Add(typeName);
                }

                componentTypeNames.Sort(StringComparer.Ordinal);

                SpatialSpec? spatial = null;
                if (archetype.TryGetProperty("spatial", out JsonElement spatialElem) && spatialElem.ValueKind == JsonValueKind.Object)
                {
                    if (!TryParseSpatialSpec(path, spatialElem, componentTypeNames, out spatial, out error))
                    {
                        return false;
                    }
                }

                var queries = new List<QuerySpec>(4);
                if (archetype.TryGetProperty("queries", out JsonElement queriesElem) && queriesElem.ValueKind == JsonValueKind.Array)
                {
                    for (int queryIndex = 0; queryIndex < queriesElem.GetArrayLength(); queryIndex++)
                    {
                        JsonElement queryElem = queriesElem[queryIndex];
                        if (queryElem.ValueKind != JsonValueKind.Object)
                        {
                            error = "Archetype kind '" + kindName + "' query entry must be an object: " + path;
                            return false;
                        }

                        if (!TryParseQuerySpec(path, queryElem, componentTypeNames, out QuerySpec query, out error))
                        {
                            return false;
                        }

                        queries.Add(query);
                    }
                }

                var bakedEntities = Array.Empty<BakedEntitySpec>();
                if (archetype.TryGetProperty("entities", out JsonElement entitiesElem) && entitiesElem.ValueKind == JsonValueKind.Array)
                {
                    if (!TryParseBakedEntities(path, kindName, entitiesElem, componentTypeNames, components, ref nextListBakeId, out bakedEntities, out error))
                    {
                        return false;
                    }
                }

                archetypes.Add(new ArchetypeSpec(kindName, capacity, spawnQueueCapacity, destroyQueueCapacity, componentTypeNames.ToArray(), spatial, queries.ToArray(), bakedEntities));

                kinds.Add(new KindSpec(path, ns, kindName));
            }

            worlds.Add(new WorldSpec(path, ns, worldName, isSealed, archetypes.ToArray(), nextListBakeId));
        }

        return true;
    }

    private static bool TryParseSpatialSpec(string path, JsonElement spatialElem, List<string> componentTypeNames, out SpatialSpec? spatial, out string? error)
    {
        spatial = null;
        error = null;

        string? position = ReadRequiredString(spatialElem, "position", out error);
        if (position == null)
        {
            error = "Spatial missing required 'position': " + path + " (" + error + ")";
            return false;
        }

        if (!TryParseComponentMember(position, out string componentTypeName, out string memberName))
        {
            error = "Spatial position must be '<Component>.<Member>' (optionally '<Component>Component.<Member>'): " + path;
            return false;
        }

        if (!componentTypeNames.Contains(componentTypeName))
        {
            error = "Spatial position component '" + componentTypeName + "' must be included in archetype components: " + path;
            return false;
        }

        if (!TryReadRequiredInt(spatialElem, "cellSize", out int cellSize) || cellSize <= 0)
        {
            error = "Spatial requires int cellSize > 0: " + path;
            return false;
        }

        if (!TryReadRequiredInt(spatialElem, "gridSize", out int gridSize) || gridSize <= 0)
        {
            error = "Spatial requires int gridSize > 0: " + path;
            return false;
        }

        int originX = ReadOptionalInt(spatialElem, "originX", defaultValue: 0);
        int originY = ReadOptionalInt(spatialElem, "originY", defaultValue: 0);

        spatial = new SpatialSpec(componentTypeName, memberName, cellSize, gridSize, originX, originY);
        return true;
    }

    private static bool TryParseQuerySpec(string path, JsonElement queryElem, List<string> componentTypeNames, out QuerySpec query, out string? error)
    {
        query = default;
        error = null;

        string? kindString = ReadRequiredString(queryElem, "kind", out error);
        if (kindString == null)
        {
            error = "Query missing required 'kind': " + path + " (" + error + ")";
            return false;
        }

        QueryKind kind;
        string lowerKind = kindString.Trim().ToLowerInvariant();
        if (lowerKind == "radius")
        {
            kind = QueryKind.Radius;
        }
        else if (lowerKind == "aabb")
        {
            kind = QueryKind.Aabb;
        }
        else
        {
            error = "Query kind must be 'radius' or 'aabb' (got '" + kindString + "'): " + path;
            return false;
        }

        string? position = ReadRequiredString(queryElem, "position", out error);
        if (position == null)
        {
            error = "Query missing required 'position': " + path + " (" + error + ")";
            return false;
        }

        if (!TryParseComponentMember(position, out string componentTypeName, out string memberName))
        {
            error = "Query position must be '<Component>.<Member>' (optionally '<Component>Component.<Member>'): " + path;
            return false;
        }

        if (!componentTypeNames.Contains(componentTypeName))
        {
            error = "Query position component '" + componentTypeName + "' must be included in archetype components: " + path;
            return false;
        }

        if (!TryReadRequiredInt(queryElem, "maxResults", out int maxResults) || maxResults <= 0)
        {
            error = "Query requires int maxResults > 0: " + path;
            return false;
        }

        query = new QuerySpec(kind, componentTypeName, memberName, maxResults);
        return true;
    }

    private static bool TryParseComponentMember(string value, out string componentTypeName, out string memberName)
    {
        componentTypeName = string.Empty;
        memberName = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        int dot = trimmed.IndexOf('.');
        if (dot <= 0 || dot >= trimmed.Length - 1)
        {
            return false;
        }

        string component = trimmed.Substring(0, dot).Trim();
        string member = trimmed.Substring(dot + 1).Trim();
        if (!IsValidIdentifier(component) || !IsValidIdentifier(member))
        {
            return false;
        }

        componentTypeName = component.EndsWith("Component", StringComparison.Ordinal) ? component : component + "Component";
        memberName = member;
        return true;
    }

    private static bool TryParseBakedEntities(
        string path,
        string kindName,
        JsonElement entitiesElem,
        List<string> archetypeComponentTypeNames,
        List<ComponentSpec> components,
        ref int nextListBakeId,
        out BakedEntitySpec[] bakedEntities,
        out string? error)
    {
        bakedEntities = Array.Empty<BakedEntitySpec>();
        error = null;

        int entityCount = entitiesElem.GetArrayLength();
        if (entityCount == 0)
        {
            return true;
        }

        var baked = new List<BakedEntitySpec>(entityCount);
        for (int entityIndex = 0; entityIndex < entityCount; entityIndex++)
        {
            JsonElement entityElem = entitiesElem[entityIndex];
            if (entityElem.ValueKind != JsonValueKind.Object)
            {
                error = "Archetype kind '" + kindName + "' entities entries must be objects: " + path;
                return false;
            }

            var bakedComponents = new List<BakedComponentSpec>(8);
            foreach (JsonProperty compProp in entityElem.EnumerateObject())
            {
                string componentKey = compProp.Name;
                if (!IsValidIdentifier(componentKey))
                {
                    error = "Baked entity component key '" + componentKey + "' is not a valid identifier: " + path;
                    return false;
                }

                string componentTypeName = componentKey.EndsWith("Component", StringComparison.Ordinal) ? componentKey : componentKey + "Component";
                if (!archetypeComponentTypeNames.Contains(componentTypeName))
                {
                    error = "Baked entity component '" + componentTypeName + "' must be listed in archetype components: " + path;
                    return false;
                }

                if (compProp.Value.ValueKind != JsonValueKind.Object)
                {
                    error = "Baked entity component '" + componentKey + "' value must be an object of field assignments: " + path;
                    return false;
                }

                if (!TryFindUniqueComponentSpecByTypeName(components, componentTypeName, out ComponentSpec componentSpec))
                {
                    error = "Baked entities only support schema-defined components. Unknown or ambiguous component type '" + componentTypeName + "': " + path;
                    return false;
                }

                string accessorName = TrimComponentSuffix(componentTypeName);
                if (!TryParseBakedComponentAssignments(path, kindName, accessorName, componentSpec, compProp.Value, ref nextListBakeId, out BakedComponentSpec bakedComponent, out error))
                {
                    return false;
                }

                bakedComponents.Add(bakedComponent);
            }

            bakedComponents.Sort(static (a, b) => string.CompareOrdinal(a.ComponentAccessorName, b.ComponentAccessorName));
            baked.Add(new BakedEntitySpec(bakedComponents.ToArray()));
        }

        bakedEntities = baked.ToArray();

        // Validate uniform field layout across all entities in this archetype.
        // The binary loader computes recordSize from entity[0] and uses it for all entities,
        // so all entities must have identical component/field structure.
        if (bakedEntities.Length > 1)
        {
            string layout0 = GetEntityFieldLayout(bakedEntities[0]);
            for (int i = 1; i < bakedEntities.Length; i++)
            {
                string layoutI = GetEntityFieldLayout(bakedEntities[i]);
                if (layoutI != layout0)
                {
                    error = "Baked entities in archetype '" + kindName + "' have non-uniform field layout. " +
                            "Entity[0] layout: [" + layout0 + "], Entity[" + i + "] layout: [" + layoutI + "]. " +
                            "All entities in the same archetype must specify the exact same components and fields: " + path;
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Returns a canonical string describing the component/field layout of a baked entity,
    /// used to detect non-uniform layouts across entities in the same archetype.
    /// Components and fields are already sorted by name at parse time.
    /// </summary>
    private static string GetEntityFieldLayout(BakedEntitySpec entity)
    {
        var sb = new System.Text.StringBuilder();
        for (int c = 0; c < entity.Components.Length; c++)
        {
            if (c > 0) sb.Append(';');
            BakedComponentSpec comp = entity.Components[c];
            sb.Append(comp.ComponentAccessorName).Append(':');
            for (int f = 0; f < comp.Assignments.Length; f++)
            {
                if (f > 0) sb.Append(',');
                sb.Append(comp.Assignments[f].FieldName);
            }
        }
        return sb.ToString();
    }

    private static bool TryParseBakedComponentAssignments(
        string path,
        string kindName,
        string componentAccessorName,
        ComponentSpec componentSpec,
        JsonElement assignmentsObj,
        ref int nextListBakeId,
        out BakedComponentSpec bakedComponent,
        out string? error)
    {
        bakedComponent = default;
        error = null;

        var bakedAssignments = new List<BakedAssignmentSpec>(8);
        foreach (JsonProperty fieldProp in assignmentsObj.EnumerateObject())
        {
            string fieldName = fieldProp.Name;
            if (!IsValidIdentifier(fieldName))
            {
                error = "Baked entity field key '" + fieldName + "' is not a valid identifier: " + path;
                return false;
            }

            if (!TryFindField(componentSpec.Fields, fieldName, out FieldSpec field))
            {
                error = "Baked entity specified unknown field '" + fieldName + "' on component '" + componentSpec.TypeName + "': " + path;
                return false;
            }

            if (field.IsEditorResizable)
            {
                if (fieldProp.Value.ValueKind != JsonValueKind.Array)
                {
                    error = "Baked list field '" + componentSpec.TypeName + "." + fieldName + "' must be a JSON array: " + path;
                    return false;
                }

                if (!TryParseBakedList(field.TypeName, fieldProp.Value, out BakedListSpec list, out error))
                {
                    error = "Baked list field '" + componentSpec.TypeName + "." + fieldName + "' error: " + error + " (" + path + ")";
                    return false;
                }

                int listBakeId = nextListBakeId++;
                bakedAssignments.Add(BakedAssignmentSpec.ForList(fieldName, field.TypeName, listBakeId, list));
            }
            else
            {
                if (!TryFormatBakedScalarExpression(field.TypeName, fieldProp.Value, out string expr, out error))
                {
                    error = "Baked scalar field '" + componentSpec.TypeName + "." + fieldName + "' error: " + error + " (" + path + ")";
                    return false;
                }

                bakedAssignments.Add(BakedAssignmentSpec.ForScalar(fieldName, field.TypeName, expr, fieldProp.Value));
            }
        }

        bakedAssignments.Sort(static (a, b) => string.CompareOrdinal(a.FieldName, b.FieldName));
        bakedComponent = new BakedComponentSpec(componentAccessorName, bakedAssignments.ToArray());
        return true;
    }

    private static bool TryParseBakedList(string listHandleTypeName, JsonElement array, out BakedListSpec list, out string? error)
    {
        list = default;
        error = null;

        if (!TryGetListHandleElementTypeName(listHandleTypeName, out string elementTypeName))
        {
            error = "Unsupported ListHandle type '" + listHandleTypeName + "'.";
            return false;
        }

        int len = array.GetArrayLength();
        string rawArrayJson = array.GetRawText();
        list = new BakedListSpec(elementTypeName, rawArrayJson, len);
        return true;
    }

    private static bool TryGetListHandleElementTypeName(string listHandleTypeName, out string elementTypeName)
    {
        elementTypeName = string.Empty;
        if (!listHandleTypeName.StartsWith(ListHandleOpenType, StringComparison.Ordinal) || !listHandleTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        int start = ListHandleOpenType.Length;
        int len = listHandleTypeName.Length - start - 1;
        if (len <= 0)
        {
            return false;
        }

        string element = listHandleTypeName.Substring(start, len).Trim();
        if (element.StartsWith("global::", StringComparison.Ordinal))
        {
            element = element.Substring("global::".Length);
        }

        elementTypeName = element;
        return elementTypeName.Length != 0;
    }

    private static bool TryFindUniqueComponentSpecByTypeName(List<ComponentSpec> components, string typeName, out ComponentSpec component)
    {
        component = default;
        bool found = false;

        for (int i = 0; i < components.Count; i++)
        {
            ComponentSpec spec = components[i];
            if (!string.Equals(spec.TypeName, typeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            found = true;
            component = spec;
        }

        return found;
    }

    private static bool TryFindField(FieldSpec[] fields, string fieldName, out FieldSpec field)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (string.Equals(fields[i].Name, fieldName, StringComparison.Ordinal))
            {
                field = fields[i];
                return true;
            }
        }

        field = default;
        return false;
    }

    private static bool TryFormatBakedScalarExpression(string typeName, JsonElement value, out string expr, out string? error)
    {
        error = null;

        if (typeName == "int")
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int i))
            {
                expr = string.Empty;
                error = "Expected int.";
                return false;
            }

            expr = i.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (typeName == "float")
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out float f))
            {
                expr = string.Empty;
                error = "Expected float.";
                return false;
            }

            expr = FormatFloat(f);
            return true;
        }

        if (typeName == "bool")
        {
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            {
                expr = string.Empty;
                error = "Expected bool.";
                return false;
            }

            expr = value.GetBoolean() ? "true" : "false";
            return true;
        }

        if (typeName == "global::Core.Color32")
        {
            return TryParseColor32Expression(value, out expr, out error);
        }

        if (typeName == "global::Core.StringHandle")
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                expr = string.Empty;
                error = "Expected string.";
                return false;
            }

            expr = FormatStringLiteral(value.GetString() ?? string.Empty);
            return true;
        }

        if (typeName == "global::System.Numerics.Vector2")
        {
            if (!TryParseFloatArray(value, 2, out float[] v, out error))
            {
                expr = string.Empty;
                return false;
            }

            expr = "new global::System.Numerics.Vector2(" + FormatFloat(v[0]) + ", " + FormatFloat(v[1]) + ")";
            return true;
        }

        if (typeName == "global::System.Numerics.Vector3")
        {
            if (!TryParseFloatArray(value, 3, out float[] v, out error))
            {
                expr = string.Empty;
                return false;
            }

            expr = "new global::System.Numerics.Vector3(" + FormatFloat(v[0]) + ", " + FormatFloat(v[1]) + ", " + FormatFloat(v[2]) + ")";
            return true;
        }

        if (typeName == "global::System.Numerics.Vector4")
        {
            if (!TryParseFloatArray(value, 4, out float[] v, out error))
            {
                expr = string.Empty;
                return false;
            }

            expr = "new global::System.Numerics.Vector4(" + FormatFloat(v[0]) + ", " + FormatFloat(v[1]) + ", " + FormatFloat(v[2]) + ", " + FormatFloat(v[3]) + ")";
            return true;
        }

        if (typeName == "global::FixedMath.Fixed64")
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i))
            {
                expr = "global::FixedMath.Fixed64.FromInt(" + i.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float f))
            {
                expr = "global::FixedMath.Fixed64.FromFloat(" + FormatFloat(f) + ")";
                return true;
            }

            expr = string.Empty;
            error = "Expected number.";
            return false;
        }

        if (typeName == "global::FixedMath.Fixed64Vec2")
        {
            if (!TryParseFloatArray(value, 2, out float[] v, out error))
            {
                expr = string.Empty;
                return false;
            }

            expr = "global::FixedMath.Fixed64Vec2.FromFloat(" + FormatFloat(v[0]) + ", " + FormatFloat(v[1]) + ")";
            return true;
        }

        if (typeName == "global::FixedMath.Fixed64Vec3")
        {
            if (!TryParseFloatArray(value, 3, out float[] v, out error))
            {
                expr = string.Empty;
                return false;
            }

            expr = "global::FixedMath.Fixed64Vec3.FromFloat(" + FormatFloat(v[0]) + ", " + FormatFloat(v[1]) + ", " + FormatFloat(v[2]) + ")";
            return true;
        }

        if (IsValidIdentifier(typeName) || typeName.StartsWith("global::", StringComparison.Ordinal))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                string member = value.GetString() ?? string.Empty;
                if (!IsValidIdentifier(member))
                {
                    expr = string.Empty;
                    error = "Expected enum member identifier string.";
                    return false;
                }

                expr = typeName + "." + member;
                return true;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i))
            {
                expr = "(" + typeName + ")" + i.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }

        expr = string.Empty;
        error = "Unsupported baked scalar type '" + typeName + "'.";
        return false;
    }

    private static bool TryParseColor32Expression(JsonElement value, out string expr, out string? error)
    {
        error = null;
        int r;
        int g;
        int b;
        int a = 255;

        if (value.ValueKind == JsonValueKind.Array)
        {
            int len = value.GetArrayLength();
            if (len < 3 || len > 4)
            {
                expr = string.Empty;
                error = "Expected [r, g, b] or [r, g, b, a].";
                return false;
            }

            if (!value[0].TryGetInt32(out r) || !value[1].TryGetInt32(out g) || !value[2].TryGetInt32(out b))
            {
                expr = string.Empty;
                error = "Expected integer channel values.";
                return false;
            }

            if (len == 4 && !value[3].TryGetInt32(out a))
            {
                expr = string.Empty;
                error = "Expected integer alpha channel value.";
                return false;
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            if (!TryReadRequiredInt(value, "r", out r) || !TryReadRequiredInt(value, "g", out g) || !TryReadRequiredInt(value, "b", out b))
            {
                expr = string.Empty;
                error = "Expected object { r, g, b, a? } with int channels.";
                return false;
            }

            a = ReadOptionalInt(value, "a", defaultValue: 255);
        }
        else
        {
            expr = string.Empty;
            error = "Expected array or object.";
            return false;
        }

        if ((uint)r > 255 || (uint)g > 255 || (uint)b > 255 || (uint)a > 255)
        {
            expr = string.Empty;
            error = "Color32 channels must be in [0,255].";
            return false;
        }

        expr = "new global::Core.Color32((byte)" + r.ToString(CultureInfo.InvariantCulture) +
               ", (byte)" + g.ToString(CultureInfo.InvariantCulture) +
               ", (byte)" + b.ToString(CultureInfo.InvariantCulture) +
               ", (byte)" + a.ToString(CultureInfo.InvariantCulture) + ")";
        return true;
    }

    private static bool TryParseFloatArray(JsonElement value, int expectedLength, out float[] values, out string? error)
    {
        values = Array.Empty<float>();
        error = null;

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != expectedLength)
        {
            error = "Expected array length " + expectedLength.ToString(CultureInfo.InvariantCulture) + ".";
            return false;
        }

        var tmp = new float[expectedLength];
        for (int i = 0; i < expectedLength; i++)
        {
            if (value[i].ValueKind != JsonValueKind.Number || !value[i].TryGetSingle(out float f))
            {
                error = "Expected float elements.";
                return false;
            }
            tmp[i] = f;
        }

        values = tmp;
        return true;
    }

    private static string TrimComponentSuffix(string typeName)
    {
        const string suffix = "Component";
        return typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - suffix.Length)
            : typeName;
    }

    private static string EmitComponent(ComponentSpec component)
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace " + component.Namespace + ";");
        sb.AppendLine();
        sb.AppendLine("public partial struct " + component.TypeName + " : global::DerpLib.Ecs.IEcsComponent");
        sb.AppendLine("{");

        for (int i = 0; i < component.Fields.Length; i++)
        {
            FieldSpec f = component.Fields[i];
            if (f.IsProperty)
            {
                string attr = FormatPropertyAttribute(f.PropertyAttr);
                sb.AppendLine("    " + attr);
            }
            if (f.IsEditorResizable)
            {
                sb.AppendLine("    [" + EditorResizableAttributeFqn + "]");
            }
            sb.AppendLine("    public " + f.TypeName + " " + f.Name + ";");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitKind(KindSpec kind)
    {
        var sb = new StringBuilder(256);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace " + kind.Namespace + ";");
        sb.AppendLine();
        sb.AppendLine("public partial struct " + kind.TypeName);
        sb.AppendLine("{");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitEnum(EnumSpec e)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace " + e.Namespace + ";");
        sb.AppendLine();
        if (e.IsFlags)
        {
            sb.AppendLine("[global::System.FlagsAttribute]");
        }
        sb.AppendLine("public enum " + e.TypeName + " : " + e.UnderlyingTypeName);
        sb.AppendLine("{");
        for (int i = 0; i < e.Values.Length; i++)
        {
            EnumValueSpec v = e.Values[i];
            sb.Append("    ").Append(v.Name).Append(" = ").Append(v.Value.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitWorldSetup(WorldSpec world)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Diagnostics;");
        sb.AppendLine("using DerpLib.Ecs.Setup;");
        sb.AppendLine();
        sb.AppendLine("namespace " + world.Namespace + ";");
        sb.AppendLine();
        sb.Append("public ");
        if (world.IsSealed)
        {
            sb.Append("sealed ");
        }
        sb.AppendLine("partial class " + world.TypeName);
        sb.AppendLine("{");
        sb.AppendLine("    [Conditional(DerpEcsSetupConstants.ConditionalSymbol)]");
        sb.AppendLine("    private static void Setup(DerpEcsSetupBuilder b)");
        sb.AppendLine("    {");

        for (int i = 0; i < world.Archetypes.Length; i++)
        {
            ArchetypeSpec archetype = world.Archetypes[i];
            sb.Append("        b.Archetype<").Append(archetype.KindTypeName).AppendLine(">()");
            sb.Append("            .Capacity(").Append(archetype.Capacity.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
            for (int c = 0; c < archetype.ComponentTypeNames.Length; c++)
            {
                sb.Append("            .With<").Append(archetype.ComponentTypeNames[c]).AppendLine(">()");
            }

            if (archetype.Spatial.HasValue)
            {
                SpatialSpec spatial = archetype.Spatial.Value;
                sb.Append("            .Spatial(nameof(")
                    .Append(spatial.PositionComponentTypeName)
                    .Append('.')
                    .Append(spatial.PositionMemberName)
                    .Append("), ")
                    .Append(spatial.CellSize.ToString(CultureInfo.InvariantCulture))
                    .Append(", ")
                    .Append(spatial.GridSize.ToString(CultureInfo.InvariantCulture))
                    .Append(", ")
                    .Append(spatial.OriginX.ToString(CultureInfo.InvariantCulture))
                    .Append(", ")
                    .Append(spatial.OriginY.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(")");
            }

            for (int q = 0; q < archetype.Queries.Length; q++)
            {
                QuerySpec query = archetype.Queries[q];
                sb.Append("            .");
                sb.Append(query.Kind == QueryKind.Radius ? "QueryRadius" : "QueryAabb");
                sb.Append("(nameof(")
                    .Append(query.PositionComponentTypeName)
                    .Append('.')
                    .Append(query.PositionMemberName)
                    .Append("), ")
                    .Append(query.MaxResults.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(")");
            }

            if (archetype.SpawnQueueCapacity != 0)
            {
                sb.Append("            .SpawnQueueCapacity(").Append(archetype.SpawnQueueCapacity.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
            }

            if (archetype.DestroyQueueCapacity != 0)
            {
                sb.Append("            .DestroyQueueCapacity(").Append(archetype.DestroyQueueCapacity.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
            }

            sb.AppendLine("            ;");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitWorldBaked(WorldSpec world)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using DerpLib.Ecs;");
        sb.AppendLine();
        sb.AppendLine("namespace " + world.Namespace + ";");
        sb.AppendLine();
        sb.AppendLine("public static class " + world.TypeName + "UgcBakedAssets");
        sb.AppendLine("{");

        // --- Load(ReadOnlySpan<byte>, world) ---
        sb.AppendLine("    public static void Load(ReadOnlySpan<byte> data, " + world.TypeName + " world)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (world == null)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new ArgumentNullException(nameof(world));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (data.Length < 32)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(\"Data too small for header.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        ref readonly DerpEntityDataHeader header = ref MemoryMarshal.AsRef<DerpEntityDataHeader>(data);");
        sb.AppendLine("        if (header.Magic != DerpEntityDataFormat.Magic || header.Version != DerpEntityDataFormat.Version)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(\"Invalid .derpentitydata header.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        uint checksum = DerpEntityDataFormat.ComputeCrc32(data.Slice(DerpEntityDataFormat.HeaderSize));");
        sb.AppendLine("        if (checksum != header.Checksum)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(\"Checksum mismatch in .derpentitydata file.\");");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Build var-heap info to determine offsets (mirrors binary writer layout)
        var heap = new VarHeapBakeBuilder();
        var listHandleSpecs = world.TotalBakedListCount == 0 ? Array.Empty<ListHandleSpec>() : new ListHandleSpec[world.TotalBakedListCount];
        bool hasStringHandleFields = false;
        // Temporary string registry for BuildListBytes (only needed for offset computation)
        var tempStringRegistry = new BakeStringRegistry();

        for (int a = 0; a < world.Archetypes.Length; a++)
        {
            ArchetypeSpec archetype = world.Archetypes[a];
            for (int e = 0; e < archetype.BakedEntities.Length; e++)
            {
                BakedEntitySpec entity = archetype.BakedEntities[e];
                for (int c = 0; c < entity.Components.Length; c++)
                {
                    BakedComponentSpec comp = entity.Components[c];
                    for (int i = 0; i < comp.Assignments.Length; i++)
                    {
                        BakedAssignmentSpec assignment = comp.Assignments[i];
                        if (assignment.FieldTypeName == "global::Core.StringHandle")
                            hasStringHandleFields = true;
                        if (!assignment.IsList) continue;

                        if (assignment.List.ElementTypeName == "Core.StringHandle")
                            hasStringHandleFields = true;

                        int id = assignment.ListBakeId;
                        byte[] listBytes = BuildListBytes(assignment.List, tempStringRegistry, null);
                        listHandleSpecs[id] = heap.AddBytes(listBytes, assignment.List.ElementCount);
                    }
                }
            }
        }

        // Emit string table loading (if any StringHandle fields)
        if (hasStringHandleFields)
        {
            sb.AppendLine("        string[] stringTable = Array.Empty<string>();");
            sb.AppendLine("        if (header.StringTableCount > 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            stringTable = new string[header.StringTableCount];");
            sb.AppendLine("            int stOff = (int)header.StringTableOffset;");
            sb.AppendLine("            for (int stI = 0; stI < (int)header.StringTableCount; stI++)");
            sb.AppendLine("            {");
            sb.AppendLine("                uint stId = MemoryMarshal.Read<uint>(data.Slice(stOff));");
            sb.AppendLine("                stOff += 4;");
            sb.AppendLine("                ushort stLen = MemoryMarshal.Read<ushort>(data.Slice(stOff));");
            sb.AppendLine("                stOff += 2;");
            sb.AppendLine("                stringTable[stId] = System.Text.Encoding.UTF8.GetString(data.Slice(stOff, stLen));");
            sb.AppendLine("                stOff += stLen;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // Emit var-heap loading
        sb.AppendLine("        if (header.VarHeapSize > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            byte[] heapBytes = data.Slice((int)header.VarHeapOffset, (int)header.VarHeapSize).ToArray();");
        sb.AppendLine("            world.VarHeap.SetBytes(heapBytes);");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            world.VarHeap.SetBytes(Array.Empty<byte>());");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Emit entity data loading with compile-time offsets
        int entityDataOffset = 32; // after header
        bool anySpawns = false;

        for (int a = 0; a < world.Archetypes.Length; a++)
        {
            ArchetypeSpec archetype = world.Archetypes[a];
            if (archetype.BakedEntities.Length == 0) continue;

            anySpawns = true;

            // Calculate record size for this archetype
            int recordSize = 0;
            if (archetype.BakedEntities.Length > 0)
            {
                BakedEntitySpec firstEntity = archetype.BakedEntities[0];
                for (int c = 0; c < firstEntity.Components.Length; c++)
                {
                    BakedComponentSpec comp = firstEntity.Components[c];
                    for (int i = 0; i < comp.Assignments.Length; i++)
                    {
                        recordSize += GetBinaryFieldSize(comp.Assignments[i].FieldTypeName);
                    }
                }
            }

            for (int e = 0; e < archetype.BakedEntities.Length; e++)
            {
                BakedEntitySpec entity = archetype.BakedEntities[e];
                string spawnVar = "spawn" + a.ToString(CultureInfo.InvariantCulture) + "_" + e.ToString(CultureInfo.InvariantCulture);

                sb.Append("        if (!world.").Append(archetype.KindTypeName).Append(".TryQueueSpawn(out var ").Append(spawnVar).AppendLine("))");
                sb.AppendLine("        {");
                sb.Append("            throw new InvalidOperationException(\"Baked spawn failed (queue full): ").Append(archetype.KindTypeName).AppendLine(".\");");
                sb.AppendLine("        }");

                int fieldOffset = 0;
                for (int c = 0; c < entity.Components.Length; c++)
                {
                    BakedComponentSpec comp = entity.Components[c];
                    for (int i = 0; i < comp.Assignments.Length; i++)
                    {
                        BakedAssignmentSpec assignment = comp.Assignments[i];
                        int fieldSize = GetBinaryFieldSize(assignment.FieldTypeName);
                        string accessor = spawnVar + "." + comp.ComponentAccessorName + "." + assignment.FieldName;

                        if (assignment.IsList)
                        {
                            // ListHandle<T> is 8 bytes (offsetBytes:i32 + count:i32)
                            EmitListHandleRead(sb, accessor, assignment.FieldTypeName, entityDataOffset, fieldOffset);
                        }
                        else
                        {
                            EmitScalarRead(sb, accessor, assignment.FieldTypeName, entityDataOffset, fieldOffset);
                        }

                        fieldOffset += fieldSize;
                    }
                }

                sb.AppendLine();
                entityDataOffset += recordSize;
            }
        }

        if (anySpawns)
        {
            sb.AppendLine("        world.PlaybackStructuralChanges();");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // --- LoadFromFile ---
        sb.AppendLine("    public static void LoadFromFile(string filePath, " + world.TypeName + " world)");
        sb.AppendLine("    {");
        sb.AppendLine("        Load(File.ReadAllBytes(filePath), world);");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitScalarRead(StringBuilder sb, string accessor, string typeName, int baseOffset, int fieldOffset)
    {
        int offset = baseOffset + fieldOffset;
        string offsetStr = offset.ToString(CultureInfo.InvariantCulture);

        if (typeName == "int")
        {
            sb.Append("        ").Append(accessor).Append(" = MemoryMarshal.Read<int>(data.Slice(").Append(offsetStr).AppendLine("));");
        }
        else if (typeName == "float")
        {
            sb.Append("        ").Append(accessor).Append(" = MemoryMarshal.Read<float>(data.Slice(").Append(offsetStr).AppendLine("));");
        }
        else if (typeName == "bool")
        {
            sb.Append("        ").Append(accessor).Append(" = data[").Append(offsetStr).AppendLine("] != 0;");
        }
        else if (typeName == "global::Core.Color32")
        {
            sb.Append("        ").Append(accessor).Append(" = MemoryMarshal.Read<global::Core.Color32>(data.Slice(").Append(offsetStr).AppendLine("));");
        }
        else if (typeName == "global::Core.StringHandle")
        {
            sb.Append("        ").Append(accessor).Append(" = stringTable[MemoryMarshal.Read<uint>(data.Slice(").Append(offsetStr).AppendLine("))];");
        }
        else if (typeName == "global::System.Numerics.Vector2")
        {
            sb.Append("        ").Append(accessor).Append(" = MemoryMarshal.Read<global::System.Numerics.Vector2>(data.Slice(").Append(offsetStr).AppendLine("));");
        }
        else if (typeName == "global::System.Numerics.Vector3")
        {
            sb.Append("        ").Append(accessor).Append(" = MemoryMarshal.Read<global::System.Numerics.Vector3>(data.Slice(").Append(offsetStr).AppendLine("));");
        }
        else if (typeName == "global::System.Numerics.Vector4")
        {
            sb.Append("        ").Append(accessor).Append(" = MemoryMarshal.Read<global::System.Numerics.Vector4>(data.Slice(").Append(offsetStr).AppendLine("));");
        }
        else if (typeName == "global::FixedMath.Fixed64")
        {
            sb.Append("        ").Append(accessor).Append(" = global::FixedMath.Fixed64.FromRaw(MemoryMarshal.Read<long>(data.Slice(").Append(offsetStr).AppendLine(")));");
        }
        else if (typeName == "global::FixedMath.Fixed64Vec2")
        {
            sb.Append("        ").Append(accessor).Append(" = new global::FixedMath.Fixed64Vec2(global::FixedMath.Fixed64.FromRaw(MemoryMarshal.Read<long>(data.Slice(").Append(offsetStr).Append("))), global::FixedMath.Fixed64.FromRaw(MemoryMarshal.Read<long>(data.Slice(").Append((offset + 8).ToString(CultureInfo.InvariantCulture)).AppendLine("))));");
        }
        else if (typeName == "global::FixedMath.Fixed64Vec3")
        {
            sb.Append("        ").Append(accessor).Append(" = new global::FixedMath.Fixed64Vec3(global::FixedMath.Fixed64.FromRaw(MemoryMarshal.Read<long>(data.Slice(").Append(offsetStr).Append("))), global::FixedMath.Fixed64.FromRaw(MemoryMarshal.Read<long>(data.Slice(").Append((offset + 8).ToString(CultureInfo.InvariantCulture)).Append("))), global::FixedMath.Fixed64.FromRaw(MemoryMarshal.Read<long>(data.Slice(").Append((offset + 16).ToString(CultureInfo.InvariantCulture)).AppendLine("))));");
        }
        else
        {
            // Enum fallback — read as int, cast to enum type
            sb.Append("        ").Append(accessor).Append(" = (").Append(typeName).Append(")MemoryMarshal.Read<int>(data.Slice(").Append(offsetStr).AppendLine("));");
        }
    }

    private static void EmitListHandleRead(StringBuilder sb, string accessor, string fieldTypeName, int baseOffset, int fieldOffset)
    {
        int offset = baseOffset + fieldOffset;
        string offsetStr = offset.ToString(CultureInfo.InvariantCulture);
        // ListHandle<T> stores (int OffsetBytes, int Count) — 8 bytes, read as two ints
        sb.Append("        ").Append(accessor).Append(" = MemoryMarshal.Read<").Append(fieldTypeName).Append(">(data.Slice(").Append(offsetStr).AppendLine("));");
    }

    private static int GetBinaryFieldSize(string typeName)
    {
        if (typeName == "int") return 4;
        if (typeName == "float") return 4;
        if (typeName == "bool") return 1;
        if (typeName == "global::Core.Color32") return 4;
        if (typeName == "global::Core.StringHandle") return 4;
        if (typeName == "global::System.Numerics.Vector2") return 8;
        if (typeName == "global::System.Numerics.Vector3") return 12;
        if (typeName == "global::System.Numerics.Vector4") return 16;
        if (typeName == "global::FixedMath.Fixed64") return 8;
        if (typeName == "global::FixedMath.Fixed64Vec2") return 16;
        if (typeName == "global::FixedMath.Fixed64Vec3") return 24;

        // ListHandle<T>
        if (typeName.StartsWith(ListHandleOpenType, StringComparison.Ordinal))
            return 8; // int OffsetBytes + int Count

        // Enum fallback: int-sized
        return 4;
    }

    private static void WriteDerpEntityDataBinary(WorldSpec world, List<EnumSpec> allEnums, string outputPath)
    {
        var heap = new VarHeapBakeBuilder();
        var listHandleSpecs = world.TotalBakedListCount == 0 ? Array.Empty<ListHandleSpec>() : new ListHandleSpec[world.TotalBakedListCount];
        var stringRegistry = new BakeStringRegistry();

        // First pass: build var-heap and string registry
        for (int a = 0; a < world.Archetypes.Length; a++)
        {
            ArchetypeSpec archetype = world.Archetypes[a];
            for (int e = 0; e < archetype.BakedEntities.Length; e++)
            {
                BakedEntitySpec entity = archetype.BakedEntities[e];
                for (int c = 0; c < entity.Components.Length; c++)
                {
                    BakedComponentSpec comp = entity.Components[c];
                    for (int i = 0; i < comp.Assignments.Length; i++)
                    {
                        BakedAssignmentSpec assignment = comp.Assignments[i];
                        if (assignment.IsList)
                        {
                            int id = assignment.ListBakeId;
                            byte[] listBytes = BuildListBytes(assignment.List, stringRegistry, allEnums);
                            listHandleSpecs[id] = heap.AddBytes(listBytes, assignment.List.ElementCount);
                        }
                        else if (assignment.FieldTypeName == "global::Core.StringHandle")
                        {
                            string str = ParseJsonString(assignment.RawValueJson);
                            stringRegistry.Register(str);
                        }
                    }
                }
            }
        }

        using var ms = new MemoryStream(4096);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        // Write placeholder header (32 bytes)
        bw.Write((uint)0x44454544); // Magic
        bw.Write((uint)1);          // Version
        bw.Write((uint)0);          // Checksum placeholder
        bw.Write((uint)0);          // Flags
        bw.Write((uint)0);          // VarHeapOffset placeholder
        bw.Write((uint)0);          // VarHeapSize placeholder
        bw.Write((uint)0);          // StringTableOffset placeholder
        bw.Write((uint)0);          // StringTableCount placeholder

        // Write entity data
        for (int a = 0; a < world.Archetypes.Length; a++)
        {
            ArchetypeSpec archetype = world.Archetypes[a];
            for (int e = 0; e < archetype.BakedEntities.Length; e++)
            {
                BakedEntitySpec entity = archetype.BakedEntities[e];
                for (int c = 0; c < entity.Components.Length; c++)
                {
                    BakedComponentSpec comp = entity.Components[c];
                    for (int i = 0; i < comp.Assignments.Length; i++)
                    {
                        BakedAssignmentSpec assignment = comp.Assignments[i];
                        if (assignment.IsList)
                        {
                            ListHandleSpec handle = listHandleSpecs[assignment.ListBakeId];
                            bw.Write(handle.OffsetBytes); // int32 OffsetBytes
                            bw.Write(handle.Count);       // int32 Count
                        }
                        else
                        {
                            WriteBakedScalarBinary(bw, assignment.FieldTypeName, assignment.RawValueJson, stringRegistry, allEnums);
                        }
                    }
                }
            }
        }

        // Pad to 16-byte alignment before var-heap
        int varHeapOffset = 0;
        int varHeapSize = heap.UsedBytes;
        if (varHeapSize > 0)
        {
            PadToAlignment(bw, 16);
            varHeapOffset = (int)ms.Position;
            heap.WriteBinaryTo(bw);
        }

        // Pad to 16-byte alignment before string table
        int stringTableOffset = 0;
        int stringTableCount = stringRegistry.Count;
        if (stringTableCount > 0)
        {
            PadToAlignment(bw, 16);
            stringTableOffset = (int)ms.Position;
            stringRegistry.WriteTo(bw);
        }

        bw.Flush();

        // Patch header with final offsets
        byte[] data = ms.ToArray();
        // Checksum: CRC32 of bytes [32..EOF]
        uint checksum = ComputeCrc32(data, 32, data.Length - 32);

        using var patchMs = new MemoryStream(data);
        using var patchBw = new BinaryWriter(patchMs, Encoding.UTF8, leaveOpen: true);
        patchBw.BaseStream.Position = 8; // Checksum at offset 8
        patchBw.Write(checksum);
        patchBw.BaseStream.Position = 16; // VarHeapOffset
        patchBw.Write((uint)varHeapOffset);
        patchBw.Write((uint)varHeapSize);
        patchBw.Write((uint)stringTableOffset);
        patchBw.Write((uint)stringTableCount);
        patchBw.Flush();

        File.WriteAllBytes(outputPath, data);
    }

    private static void WriteBakedScalarBinary(BinaryWriter bw, string typeName, string rawJson, BakeStringRegistry stringRegistry, List<EnumSpec> allEnums)
    {
        using JsonDocument doc = JsonDocument.Parse(rawJson);
        JsonElement value = doc.RootElement;

        if (typeName == "int")
        {
            bw.Write(value.GetInt32());
            return;
        }

        if (typeName == "float")
        {
            bw.Write(value.GetSingle());
            return;
        }

        if (typeName == "bool")
        {
            bw.Write(value.GetBoolean() ? (byte)1 : (byte)0);
            return;
        }

        if (typeName == "global::Core.Color32")
        {
            WriteColor32Binary(bw, value);
            return;
        }

        if (typeName == "global::Core.StringHandle")
        {
            string str = value.GetString() ?? string.Empty;
            uint id = stringRegistry.GetId(str);
            bw.Write(id);
            return;
        }

        if (typeName == "global::System.Numerics.Vector2")
        {
            float[] v = ParseFloatArrayUnchecked(value, 2);
            bw.Write(v[0]);
            bw.Write(v[1]);
            return;
        }

        if (typeName == "global::System.Numerics.Vector3")
        {
            float[] v = ParseFloatArrayUnchecked(value, 3);
            bw.Write(v[0]);
            bw.Write(v[1]);
            bw.Write(v[2]);
            return;
        }

        if (typeName == "global::System.Numerics.Vector4")
        {
            float[] v = ParseFloatArrayUnchecked(value, 4);
            bw.Write(v[0]);
            bw.Write(v[1]);
            bw.Write(v[2]);
            bw.Write(v[3]);
            return;
        }

        if (typeName == "global::FixedMath.Fixed64")
        {
            long raw = Fixed64ToRaw(value);
            bw.Write(raw);
            return;
        }

        if (typeName == "global::FixedMath.Fixed64Vec2")
        {
            float[] v = ParseFloatArrayUnchecked(value, 2);
            bw.Write(Fixed64FromFloat(v[0]));
            bw.Write(Fixed64FromFloat(v[1]));
            return;
        }

        if (typeName == "global::FixedMath.Fixed64Vec3")
        {
            float[] v = ParseFloatArrayUnchecked(value, 3);
            bw.Write(Fixed64FromFloat(v[0]));
            bw.Write(Fixed64FromFloat(v[1]));
            bw.Write(Fixed64FromFloat(v[2]));
            return;
        }

        // Enum fallback: write as int32
        if (value.ValueKind == JsonValueKind.Number)
        {
            bw.Write(value.GetInt32());
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            string memberName = value.GetString() ?? string.Empty;
            int enumIntValue = ResolveEnumMemberValue(typeName, memberName, allEnums);
            bw.Write(enumIntValue);
        }
        else
        {
            throw new InvalidOperationException("Cannot write binary for type '" + typeName + "'.");
        }
    }

    private static int ResolveEnumMemberValue(string typeName, string memberName, List<EnumSpec> allEnums)
    {
        // typeName can be bare "UgcState" or "global::Ns.UgcState"
        for (int i = 0; i < allEnums.Count; i++)
        {
            EnumSpec e = allEnums[i];
            if (!string.Equals(e.TypeName, typeName, StringComparison.Ordinal) &&
                !string.Equals(e.FullyQualifiedTypeName, typeName, StringComparison.Ordinal))
            {
                continue;
            }

            for (int j = 0; j < e.Values.Length; j++)
            {
                if (string.Equals(e.Values[j].Name, memberName, StringComparison.Ordinal))
                {
                    return (int)e.Values[j].Value;
                }
            }

            throw new InvalidOperationException("Enum '" + typeName + "' does not have member '" + memberName + "'.");
        }

        throw new InvalidOperationException("Cannot resolve enum type '" + typeName + "' for binary baking.");
    }

    private static void WriteColor32Binary(BinaryWriter bw, JsonElement value)
    {
        int r, g, b, a = 255;
        if (value.ValueKind == JsonValueKind.Array)
        {
            r = value[0].GetInt32();
            g = value[1].GetInt32();
            b = value[2].GetInt32();
            if (value.GetArrayLength() == 4) a = value[3].GetInt32();
        }
        else
        {
            r = value.GetProperty("r").GetInt32();
            g = value.GetProperty("g").GetInt32();
            b = value.GetProperty("b").GetInt32();
            if (value.TryGetProperty("a", out JsonElement ae)) a = ae.GetInt32();
        }

        bw.Write((byte)r);
        bw.Write((byte)g);
        bw.Write((byte)b);
        bw.Write((byte)a);
    }

    private static string ParseJsonString(string rawJson)
    {
        using JsonDocument doc = JsonDocument.Parse(rawJson);
        return doc.RootElement.GetString() ?? string.Empty;
    }

    private static float[] ParseFloatArrayUnchecked(JsonElement value, int count)
    {
        var result = new float[count];
        for (int i = 0; i < count; i++)
            result[i] = value[i].GetSingle();
        return result;
    }

    /// <summary>Q48.16 fixed-point from int (matches FixedMath.Fixed64.FractionalBits = 16).</summary>
    private static long Fixed64FromInt(int value) => (long)value << 16;

    /// <summary>Q48.16 fixed-point from float (matches FixedMath.Fixed64.FractionalBits = 16).</summary>
    private static long Fixed64FromFloat(float value) => (long)(value * 65536.0);

    private static long Fixed64ToRaw(JsonElement value)
    {
        if (value.TryGetInt32(out int i))
            return Fixed64FromInt(i);
        return Fixed64FromFloat(value.GetSingle());
    }

    private static byte[] BuildListBytes(BakedListSpec list, BakeStringRegistry? stringRegistry, List<EnumSpec>? allEnums)
    {
        if (list.ElementCount == 0)
            return Array.Empty<byte>();

        using JsonDocument doc = JsonDocument.Parse(list.RawArrayJson);
        JsonElement array = doc.RootElement;

        using var ms = new MemoryStream(list.ElementCount * 4);
        using var bw = new BinaryWriter(ms);

        string elementType = list.ElementTypeName;

        if (elementType == "int")
        {
            for (int i = 0; i < list.ElementCount; i++)
                bw.Write(array[i].GetInt32());
        }
        else if (elementType == "float")
        {
            for (int i = 0; i < list.ElementCount; i++)
                bw.Write(array[i].GetSingle());
        }
        else if (elementType == "bool")
        {
            for (int i = 0; i < list.ElementCount; i++)
                bw.Write(array[i].GetBoolean() ? (byte)1 : (byte)0);
        }
        else if (elementType == "Core.Color32")
        {
            for (int i = 0; i < list.ElementCount; i++)
                WriteColor32Binary(bw, array[i]);
        }
        else if (elementType == "System.Numerics.Vector2")
        {
            for (int i = 0; i < list.ElementCount; i++)
            {
                JsonElement v = array[i];
                bw.Write(v[0].GetSingle());
                bw.Write(v[1].GetSingle());
            }
        }
        else if (elementType == "System.Numerics.Vector3")
        {
            for (int i = 0; i < list.ElementCount; i++)
            {
                JsonElement v = array[i];
                bw.Write(v[0].GetSingle());
                bw.Write(v[1].GetSingle());
                bw.Write(v[2].GetSingle());
            }
        }
        else if (elementType == "System.Numerics.Vector4")
        {
            for (int i = 0; i < list.ElementCount; i++)
            {
                JsonElement v = array[i];
                bw.Write(v[0].GetSingle());
                bw.Write(v[1].GetSingle());
                bw.Write(v[2].GetSingle());
                bw.Write(v[3].GetSingle());
            }
        }
        else if (elementType == "FixedMath.Fixed64")
        {
            for (int i = 0; i < list.ElementCount; i++)
                bw.Write(Fixed64ToRaw(array[i]));
        }
        else if (elementType == "FixedMath.Fixed64Vec2")
        {
            for (int i = 0; i < list.ElementCount; i++)
            {
                JsonElement v = array[i];
                bw.Write(Fixed64ToRaw(v[0]));
                bw.Write(Fixed64ToRaw(v[1]));
            }
        }
        else if (elementType == "FixedMath.Fixed64Vec3")
        {
            for (int i = 0; i < list.ElementCount; i++)
            {
                JsonElement v = array[i];
                bw.Write(Fixed64ToRaw(v[0]));
                bw.Write(Fixed64ToRaw(v[1]));
                bw.Write(Fixed64ToRaw(v[2]));
            }
        }
        else if (elementType == "Core.StringHandle")
        {
            if (stringRegistry == null)
                throw new InvalidOperationException("StringHandle list requires a string registry.");
            for (int i = 0; i < list.ElementCount; i++)
            {
                string str = array[i].GetString() ?? string.Empty;
                uint id = stringRegistry.Register(str);
                bw.Write(id);
            }
        }
        else
        {
            // Enum fallback
            if (allEnums == null)
                throw new InvalidOperationException("Enum list requires enum specs.");
            for (int i = 0; i < list.ElementCount; i++)
            {
                JsonElement v = array[i];
                if (v.ValueKind == JsonValueKind.Number)
                {
                    bw.Write(v.GetInt32());
                }
                else if (v.ValueKind == JsonValueKind.String)
                {
                    string memberName = v.GetString() ?? string.Empty;
                    int enumIntValue = ResolveEnumMemberValue(elementType, memberName, allEnums);
                    bw.Write(enumIntValue);
                }
                else
                {
                    throw new InvalidOperationException("Unsupported list element value for enum type '" + elementType + "'.");
                }
            }
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static void PadToAlignment(BinaryWriter bw, int alignment)
    {
        long pos = bw.BaseStream.Position;
        long remainder = pos % alignment;
        if (remainder != 0)
        {
            int padBytes = alignment - (int)remainder;
            for (int i = 0; i < padBytes; i++)
                bw.Write((byte)0);
        }
    }

    private static uint ComputeCrc32(byte[] data, int offset, int length)
    {
        const uint polynomial = 0xEDB88320;
        uint crc = 0xFFFFFFFF;
        for (int i = offset; i < offset + length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
        }
        return crc ^ 0xFFFFFFFF;
    }

    private sealed class BakeStringRegistry
    {
        private readonly Dictionary<string, uint> _ids = new Dictionary<string, uint>(StringComparer.Ordinal);
        private readonly List<(uint Id, string Value)> _entries = new List<(uint, string)>();
        private uint _nextId;

        public int Count => _entries.Count;

        public uint Register(string value)
        {
            if (_ids.TryGetValue(value, out uint id))
                return id;

            id = _nextId++;
            _ids[value] = id;
            _entries.Add((id, value));
            return id;
        }

        public uint GetId(string value)
        {
            return _ids[value];
        }

        public void WriteTo(BinaryWriter bw)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                bw.Write(_entries[i].Id);
                byte[] utf8 = Encoding.UTF8.GetBytes(_entries[i].Value);
                bw.Write((ushort)utf8.Length);
                bw.Write(utf8);
            }
        }
    }

    private static string FormatPropertyAttribute(in PropertyAttr attr)
    {
        if (!attr.HasAny)
        {
            return "[" + PropertyAttributeFqn + "]";
        }

        var sb = new StringBuilder(256);
        sb.Append('[').Append(PropertyAttributeFqn).Append('(');

        bool first = true;
        AppendNamedArg(sb, ref first, "Name", attr.Name);
        AppendNamedArg(sb, ref first, "Group", attr.Group);
        AppendNamedArg(sb, ref first, "Order", attr.Order);
        AppendNamedArg(sb, ref first, "Min", attr.Min);
        AppendNamedArg(sb, ref first, "Max", attr.Max);
        AppendNamedArg(sb, ref first, "Step", attr.Step);
        AppendNamedArg(sb, ref first, "Flags", attr.FlagsExpression);
        AppendNamedArg(sb, ref first, "ExpandSubfields", attr.ExpandSubfields);
        AppendNamedArg(sb, ref first, "Kind", attr.KindExpression);

        sb.Append(")]");
        return sb.ToString();
    }

    private static void AppendNamedArg(StringBuilder sb, ref bool first, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (!first)
        {
            sb.Append(", ");
        }

        first = false;
        sb.Append(name).Append(" = ").Append(value);
    }

    private static void AppendNamedArg(StringBuilder sb, ref bool first, string name, int? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        if (!first)
        {
            sb.Append(", ");
        }

        first = false;
        sb.Append(name).Append(" = ").Append(value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendNamedArg(StringBuilder sb, ref bool first, string name, float? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        if (!first)
        {
            sb.Append(", ");
        }

        first = false;
        sb.Append(name).Append(" = ").Append(FormatFloat(value.Value));
    }

    private static void AppendNamedArg(StringBuilder sb, ref bool first, string name, bool? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        if (!first)
        {
            sb.Append(", ");
        }

        first = false;
        sb.Append(name).Append(" = ").Append(value.Value ? "true" : "false");
    }

    private static string? ReadRequiredString(JsonElement obj, string name, out string? error)
    {
        error = null;
        if (!obj.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.String)
        {
            error = "missing required string property '" + name + "'";
            return null;
        }

        string? value = e.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "property '" + name + "' must be non-empty";
            return null;
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return e.GetString();
    }

    private static bool ReadOptionalBool(JsonElement obj, string name, bool defaultValue)
    {
        if (!obj.TryGetProperty(name, out JsonElement e))
        {
            return defaultValue;
        }

        if (e.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (e.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return defaultValue;
    }

    private static bool TryMapFieldType(string schemaType, out string typeName, out bool isListHandle)
    {
        isListHandle = false;
        string trimmed = schemaType.Trim();
        if (trimmed.Length == 0)
        {
            typeName = string.Empty;
            return false;
        }

        if (TryMapListHandleType(trimmed, out typeName))
        {
            isListHandle = true;
            return true;
        }

        string lower = trimmed.ToLowerInvariant();
        switch (lower)
        {
            case "float":
            case "single":
                typeName = "float";
                return true;
            case "int":
            case "int32":
                typeName = "int";
                return true;
            case "bool":
            case "boolean":
                typeName = "bool";
                return true;
            case "vector2":
            case "vec2":
                typeName = "global::System.Numerics.Vector2";
                return true;
            case "vector3":
            case "vec3":
                typeName = "global::System.Numerics.Vector3";
                return true;
            case "vector4":
            case "vec4":
                typeName = "global::System.Numerics.Vector4";
                return true;
            case "color32":
                typeName = "global::Core.Color32";
                return true;
            case "stringhandle":
                typeName = "global::Core.StringHandle";
                return true;
            case "fixed64":
                typeName = "global::FixedMath.Fixed64";
                return true;
            case "fixed64vec2":
                typeName = "global::FixedMath.Fixed64Vec2";
                return true;
            case "fixed64vec3":
                typeName = "global::FixedMath.Fixed64Vec3";
                return true;
        }

        if (trimmed.IndexOf('<') >= 0 || trimmed.IndexOf('>') >= 0)
        {
            typeName = string.Empty;
            return false;
        }

        if (trimmed.StartsWith("global::", StringComparison.Ordinal))
        {
            typeName = trimmed;
            return true;
        }

        if (trimmed.IndexOf('.') >= 0)
        {
            typeName = "global::" + trimmed;
            return true;
        }

        if (IsValidIdentifier(trimmed))
        {
            typeName = trimmed;
            return true;
        }

        typeName = string.Empty;
        return false;
    }

    private static bool TryMapListHandleType(string schemaType, out string mappedType)
    {
        mappedType = string.Empty;

        string trimmed = schemaType.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        string lower = trimmed.ToLowerInvariant();
        if (!lower.StartsWith("listhandle<", StringComparison.Ordinal) &&
            !lower.StartsWith("list<", StringComparison.Ordinal))
        {
            return false;
        }

        int start = trimmed.IndexOf('<');
        int end = trimmed.LastIndexOf('>');
        if (start < 0 || end <= start + 1)
        {
            return false;
        }

        string element = trimmed.Substring(start + 1, end - start - 1).Trim();
        if (element.Length == 0)
        {
            return false;
        }

        if (!TryMapFieldType(element, out string elementType, out _))
        {
            return false;
        }

        mappedType = ListHandleOpenType + elementType + ">";
        return true;
    }

    private static bool TryReadRequiredInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return e.TryGetInt32(out value);
    }

    private static int ReadOptionalInt(JsonElement obj, string name, int defaultValue)
    {
        if (!obj.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.Number)
        {
            return defaultValue;
        }

        return e.TryGetInt32(out int value) ? value : defaultValue;
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string FormatStringLiteral(string value)
    {
        return "\"" + EscapeString(value) + "\"";
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value))
        {
            return "float.NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "float.PositiveInfinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "float.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    private static bool IsValidIdentifier(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        char first = value[0];
        if (!(char.IsLetter(first) || first == '_'))
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char ch = value[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private readonly struct EnumSpec
    {
        public readonly string SourcePath;
        public readonly string Namespace;
        public readonly string TypeName;
        public readonly string UnderlyingTypeName;
        public readonly bool IsFlags;
        public readonly EnumValueSpec[] Values;

        public EnumSpec(string sourcePath, string @namespace, string typeName, string underlyingTypeName, bool isFlags, EnumValueSpec[] values)
        {
            SourcePath = sourcePath;
            Namespace = @namespace;
            TypeName = typeName;
            UnderlyingTypeName = underlyingTypeName;
            IsFlags = isFlags;
            Values = values;
        }

        public string FullyQualifiedTypeName => "global::" + Namespace + "." + TypeName;
    }

    private readonly struct EnumValueSpec
    {
        public readonly string Name;
        public readonly long Value;

        public EnumValueSpec(string name, long value)
        {
            Name = name;
            Value = value;
        }
    }

    private readonly struct ComponentSpec
    {
        public readonly string SourcePath;
        public readonly string Namespace;
        public readonly string TypeName;
        public readonly string FullyQualifiedTypeName;
        public readonly FieldSpec[] Fields;

        public ComponentSpec(string sourcePath, string @namespace, string typeName, FieldSpec[] fields)
        {
            SourcePath = sourcePath;
            Namespace = @namespace;
            TypeName = typeName;
            FullyQualifiedTypeName = @namespace + "." + typeName;
            Fields = fields;
        }
    }

    private readonly struct FieldSpec
    {
        public readonly string Name;
        public readonly string TypeName;
        public readonly bool IsProperty;
        public readonly bool IsEditorResizable;
        public readonly PropertyAttr PropertyAttr;

        public FieldSpec(string name, string typeName, bool isProperty, bool isEditorResizable, PropertyAttr propertyAttr)
        {
            Name = name;
            TypeName = typeName;
            IsProperty = isProperty;
            IsEditorResizable = isEditorResizable;
            PropertyAttr = propertyAttr;
        }
    }

    private readonly struct KindSpec
    {
        public readonly string SourcePath;
        public readonly string Namespace;
        public readonly string TypeName;
        public readonly string FullyQualifiedTypeName;

        public KindSpec(string sourcePath, string @namespace, string typeName)
        {
            SourcePath = sourcePath;
            Namespace = @namespace;
            TypeName = typeName;
            FullyQualifiedTypeName = @namespace + "." + typeName;
        }
    }

    private readonly struct WorldSpec
    {
        public readonly string SourcePath;
        public readonly string Namespace;
        public readonly string TypeName;
        public readonly string FullyQualifiedTypeName;
        public readonly bool IsSealed;
        public readonly ArchetypeSpec[] Archetypes;
        public readonly int TotalBakedListCount;

        public WorldSpec(string sourcePath, string @namespace, string typeName, bool isSealed, ArchetypeSpec[] archetypes, int totalBakedListCount)
        {
            SourcePath = sourcePath;
            Namespace = @namespace;
            TypeName = typeName;
            FullyQualifiedTypeName = @namespace + "." + typeName;
            IsSealed = isSealed;
            Archetypes = archetypes;
            TotalBakedListCount = totalBakedListCount;
        }

        public bool HasBakedContent
        {
            get
            {
                for (int i = 0; i < Archetypes.Length; i++)
                {
                    if (Archetypes[i].BakedEntities.Length != 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    private readonly struct ArchetypeSpec
    {
        public readonly string KindTypeName;
        public readonly int Capacity;
        public readonly int SpawnQueueCapacity;
        public readonly int DestroyQueueCapacity;
        public readonly string[] ComponentTypeNames;
        public readonly SpatialSpec? Spatial;
        public readonly QuerySpec[] Queries;
        public readonly BakedEntitySpec[] BakedEntities;

        public ArchetypeSpec(
            string kindTypeName,
            int capacity,
            int spawnQueueCapacity,
            int destroyQueueCapacity,
            string[] componentTypeNames,
            SpatialSpec? spatial,
            QuerySpec[] queries,
            BakedEntitySpec[] bakedEntities)
        {
            KindTypeName = kindTypeName;
            Capacity = capacity;
            SpawnQueueCapacity = spawnQueueCapacity;
            DestroyQueueCapacity = destroyQueueCapacity;
            ComponentTypeNames = componentTypeNames;
            Spatial = spatial;
            Queries = queries;
            BakedEntities = bakedEntities;
        }
    }

    private readonly struct BakedEntitySpec
    {
        public readonly BakedComponentSpec[] Components;

        public BakedEntitySpec(BakedComponentSpec[] components)
        {
            Components = components;
        }
    }

    private readonly struct BakedComponentSpec
    {
        public readonly string ComponentAccessorName;
        public readonly BakedAssignmentSpec[] Assignments;

        public BakedComponentSpec(string componentAccessorName, BakedAssignmentSpec[] assignments)
        {
            ComponentAccessorName = componentAccessorName;
            Assignments = assignments;
        }
    }

    private readonly struct BakedAssignmentSpec
    {
        public readonly string FieldName;
        public readonly string FieldTypeName;
        public readonly bool IsList;
        public readonly int ListBakeId;
        public readonly BakedListSpec List;
        public readonly string ScalarExpression;
        public readonly string RawValueJson;

        private BakedAssignmentSpec(string fieldName, string fieldTypeName, bool isList, int listBakeId, BakedListSpec list, string scalarExpression, string rawValueJson)
        {
            FieldName = fieldName;
            FieldTypeName = fieldTypeName;
            IsList = isList;
            ListBakeId = listBakeId;
            List = list;
            ScalarExpression = scalarExpression;
            RawValueJson = rawValueJson;
        }

        public static BakedAssignmentSpec ForScalar(string fieldName, string fieldTypeName, string scalarExpression, JsonElement rawValue)
        {
            return new BakedAssignmentSpec(fieldName, fieldTypeName, isList: false, listBakeId: -1, default, scalarExpression, rawValue.GetRawText());
        }

        public static BakedAssignmentSpec ForList(string fieldName, string fieldTypeName, int listBakeId, BakedListSpec list)
        {
            return new BakedAssignmentSpec(fieldName, fieldTypeName, isList: true, listBakeId, list, scalarExpression: string.Empty, string.Empty);
        }
    }

    private readonly struct BakedListSpec
    {
        public readonly string ElementTypeName;
        public readonly string RawArrayJson;
        public readonly int ElementCount;

        public BakedListSpec(string elementTypeName, string rawArrayJson, int elementCount)
        {
            ElementTypeName = elementTypeName;
            RawArrayJson = rawArrayJson;
            ElementCount = elementCount;
        }
    }

    private readonly struct ListHandleSpec
    {
        public readonly int OffsetBytes;
        public readonly int Count;

        public ListHandleSpec(int offsetBytes, int count)
        {
            OffsetBytes = offsetBytes;
            Count = count;
        }

        public bool IsValid => OffsetBytes >= 0 && Count > 0;
    }

    private sealed class VarHeapBakeBuilder
    {
        private readonly List<byte> _bytes = new List<byte>(256);
        private int _usedBytes;

        // Content-addressed dedup: hash → (offset, count, byteLength)
        private readonly Dictionary<int, (int Offset, int Count, int ByteLength)> _dedup = new Dictionary<int, (int, int, int)>();

        public int UsedBytes => _usedBytes;

        public ListHandleSpec AddBytes(byte[] data, int elementCount)
        {
            if (data.Length == 0 || elementCount == 0)
            {
                return default;
            }

            int hash = ComputeContentHash(data);
            if (_dedup.TryGetValue(hash, out var existing) && existing.ByteLength == data.Length && existing.Count == elementCount)
            {
                // Verify actual content match
                bool match = true;
                for (int i = 0; i < data.Length; i++)
                {
                    if (_bytes[existing.Offset + i] != data[i])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return new ListHandleSpec(existing.Offset, existing.Count);
            }

            int offset = Align16(_usedBytes);
            EnsurePadding(offset);

            for (int i = 0; i < data.Length; i++)
                _bytes.Add(data[i]);

            _usedBytes = offset + data.Length;

            _dedup[hash] = (offset, elementCount, data.Length);
            return new ListHandleSpec(offset, elementCount);
        }

        public void WriteBinaryTo(BinaryWriter bw)
        {
            for (int i = 0; i < _usedBytes; i++)
            {
                bw.Write(_bytes[i]);
            }
        }

        public void EmitByteArrayLiteral(StringBuilder sb, string indent)
        {
            sb.AppendLine("new byte[]");
            sb.Append(indent).AppendLine("{");

            const int bytesPerLine = 16;
            int iByte = 0;
            while (iByte < _usedBytes)
            {
                sb.Append(indent).Append("    ");
                int end = iByte + bytesPerLine;
                if (end > _usedBytes)
                {
                    end = _usedBytes;
                }

                for (int i = iByte; i < end; i++)
                {
                    sb.Append("0x").Append(_bytes[i].ToString("X2", CultureInfo.InvariantCulture));
                    if (i + 1 < _usedBytes)
                    {
                        sb.Append(", ");
                    }
                }
                sb.AppendLine();
                iByte = end;
            }

            sb.Append(indent).Append("}");
        }

        private static int ComputeContentHash(byte[] data)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < data.Length; i++)
                    hash = hash * 31 + data[i];
                return hash;
            }
        }

        private void EnsurePadding(int offset)
        {
            while (_bytes.Count < offset)
            {
                _bytes.Add(0);
            }
        }

        private static int Align16(int value)
        {
            const int alignment = 16;
            int mask = alignment - 1;
            return (value + mask) & ~mask;
        }
    }

    private readonly struct SpatialSpec
    {
        public readonly string PositionComponentTypeName;
        public readonly string PositionMemberName;
        public readonly int CellSize;
        public readonly int GridSize;
        public readonly int OriginX;
        public readonly int OriginY;

        public SpatialSpec(string positionComponentTypeName, string positionMemberName, int cellSize, int gridSize, int originX, int originY)
        {
            PositionComponentTypeName = positionComponentTypeName;
            PositionMemberName = positionMemberName;
            CellSize = cellSize;
            GridSize = gridSize;
            OriginX = originX;
            OriginY = originY;
        }
    }

    private enum QueryKind
    {
        Radius,
        Aabb
    }

    private readonly struct QuerySpec
    {
        public readonly QueryKind Kind;
        public readonly string PositionComponentTypeName;
        public readonly string PositionMemberName;
        public readonly int MaxResults;

        public QuerySpec(QueryKind kind, string positionComponentTypeName, string positionMemberName, int maxResults)
        {
            Kind = kind;
            PositionComponentTypeName = positionComponentTypeName;
            PositionMemberName = positionMemberName;
            MaxResults = maxResults;
        }
    }

    private readonly struct PropertyAttr
    {
        public readonly string? Name;
        public readonly string? Group;
        public readonly int? Order;
        public readonly float? Min;
        public readonly float? Max;
        public readonly float? Step;
        public readonly string? FlagsExpression;
        public readonly bool? ExpandSubfields;
        public readonly string? KindExpression;

        public PropertyAttr(
            string? name,
            string? group,
            int? order,
            float? min,
            float? max,
            float? step,
            string? flagsExpression,
            bool? expandSubfields,
            string? kindExpression)
        {
            Name = name;
            Group = group;
            Order = order;
            Min = min;
            Max = max;
            Step = step;
            FlagsExpression = flagsExpression;
            ExpandSubfields = expandSubfields;
            KindExpression = kindExpression;
        }

        public bool HasAny =>
            !string.IsNullOrEmpty(Name) ||
            !string.IsNullOrEmpty(Group) ||
            Order.HasValue ||
            Min.HasValue ||
            Max.HasValue ||
            Step.HasValue ||
            !string.IsNullOrEmpty(FlagsExpression) ||
            ExpandSubfields.HasValue ||
            !string.IsNullOrEmpty(KindExpression);

        public static PropertyAttr FromJson(JsonElement field)
        {
            string? displayName = ReadOptionalString(field, "displayName");
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                displayName = FormatStringLiteral(displayName.Trim());
            }
            else
            {
                displayName = null;
            }

            string? group = ReadOptionalString(field, "group");
            if (!string.IsNullOrWhiteSpace(group))
            {
                group = FormatStringLiteral(group.Trim());
            }
            else
            {
                group = null;
            }

            int? order = null;
            if (field.TryGetProperty("order", out JsonElement orderElem) && orderElem.ValueKind == JsonValueKind.Number && orderElem.TryGetInt32(out int orderValue))
            {
                order = orderValue;
            }

            float? min = ReadOptionalFloat(field, "min");
            float? max = ReadOptionalFloat(field, "max");
            float? step = ReadOptionalFloat(field, "step");

            bool? expandSubfields = null;
            if (field.TryGetProperty("expandSubfields", out JsonElement expandElem))
            {
                if (expandElem.ValueKind == JsonValueKind.True)
                {
                    expandSubfields = true;
                }
                else if (expandElem.ValueKind == JsonValueKind.False)
                {
                    expandSubfields = false;
                }
            }

            string? flagsExpr = TryParseFlagsExpression(field);
            string? kindExpr = TryParseKindExpression(field);

            return new PropertyAttr(displayName, group, order, min, max, step, flagsExpr, expandSubfields, kindExpr);
        }

        private static float? ReadOptionalFloat(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            if (e.TryGetSingle(out float v))
            {
                return v;
            }

            if (e.TryGetDouble(out double dv))
            {
                return (float)dv;
            }

            return null;
        }

        private static string? TryParseKindExpression(JsonElement field)
        {
            if (!field.TryGetProperty("kind", out JsonElement e))
            {
                return null;
            }

            if (e.ValueKind == JsonValueKind.String)
            {
                string? s = e.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                string enumName = s.Trim();
                return PropertyKindFqn + "." + enumName;
            }

            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int raw))
            {
                return "(" + PropertyKindFqn + ")" + raw.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static string? TryParseFlagsExpression(JsonElement field)
        {
            if (!field.TryGetProperty("flags", out JsonElement e))
            {
                return null;
            }

            if (e.ValueKind == JsonValueKind.String)
            {
                string? s = e.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                return ParseFlagsString(s);
            }

            if (e.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>(e.GetArrayLength());
                for (int i = 0; i < e.GetArrayLength(); i++)
                {
                    JsonElement item = e[i];
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string? s = item.GetString();
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        continue;
                    }

                    parts.Add(s.Trim());
                }

                if (parts.Count == 0)
                {
                    return null;
                }

                return JoinFlags(parts);
            }

            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int raw))
            {
                return "(" + PropertyFlagsFqn + ")" + raw.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static string ParseFlagsString(string s)
        {
            string text = s.Trim();
            if (text.Length == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>(4);
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                bool atEnd = i == text.Length;
                char ch = atEnd ? '\0' : text[i];
                if (atEnd || ch == '|' || ch == ',' || ch == ';')
                {
                    int len = i - start;
                    if (len > 0)
                    {
                        string part = text.Substring(start, len).Trim();
                        if (part.Length != 0)
                        {
                            parts.Add(part);
                        }
                    }

                    start = i + 1;
                }
            }

            return JoinFlags(parts);
        }

        private static string JoinFlags(List<string> parts)
        {
            if (parts.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(128);
            for (int i = 0; i < parts.Count; i++)
            {
                if (i != 0)
                {
                    sb.Append(" | ");
                }
                sb.Append(PropertyFlagsFqn).Append('.').Append(parts[i]);
            }
            return sb.ToString();
        }
    }
}
