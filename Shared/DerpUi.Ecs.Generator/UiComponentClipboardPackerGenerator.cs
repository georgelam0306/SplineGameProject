using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DerpUi.Ecs.Generator;

[Generator]
public sealed class UiComponentClipboardPackerGenerator : IIncrementalGenerator
{
    private const string PooledAttributeMetadataName = "Pooled.PooledAttribute";
    private const string ColumnAttributeMetadataName = "Pooled.ColumnAttribute";
    private const string InlineArrayAttributeMetadataName = "System.Runtime.CompilerServices.InlineArrayAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ComponentInfo> componentInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: PooledAttributeMetadataName,
                predicate: static (node, _) => node is StructDeclarationSyntax,
                transform: static (syntaxContext, _) => TryGetComponentInfo(syntaxContext))
            .Where(static info => info.IsValid);

        IncrementalValueProvider<ImmutableArray<ComponentInfo>> allComponentInfos = componentInfos.Collect();

        context.RegisterSourceOutput(allComponentInfos, static (sourceContext, infos) =>
        {
            Emit(sourceContext, infos);
        });
    }

    private static ComponentInfo TryGetComponentInfo(GeneratorAttributeSyntaxContext syntaxContext)
    {
        if (syntaxContext.TargetSymbol is not INamedTypeSymbol componentSymbol)
        {
            return default;
        }

        if (componentSymbol.ContainingNamespace == null ||
            componentSymbol.ContainingNamespace.ToDisplayString() != "Derp.UI")
        {
            return default;
        }

        int poolId = 0;
        foreach (AttributeData attributeData in syntaxContext.Attributes)
        {
            if (attributeData.AttributeClass == null)
            {
                continue;
            }

            if (attributeData.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::" + PooledAttributeMetadataName)
            {
                continue;
            }

            foreach (KeyValuePair<string, TypedConstant> named in attributeData.NamedArguments)
            {
                if (named.Key != "PoolId" || named.Value.Kind != TypedConstantKind.Primitive)
                {
                    continue;
                }

                object? rawValue = named.Value.Value;
                if (rawValue is byte byteValue)
                {
                    poolId = byteValue;
                }
                else if (rawValue is short shortValue)
                {
                    poolId = shortValue;
                }
                else if (rawValue is ushort ushortValue)
                {
                    poolId = ushortValue;
                }
                else if (rawValue is int intValue)
                {
                    poolId = intValue;
                }

                break;
            }

            break;
        }

        if (poolId <= 0)
        {
            return default;
        }

        var fields = new List<FieldInfo>();
        foreach (ISymbol member in componentSymbol.GetMembers())
        {
            if (member is not IFieldSymbol fieldSymbol)
            {
                continue;
            }

            if (fieldSymbol.IsStatic)
            {
                continue;
            }

            if (!TryGetColumnAttribute(fieldSymbol.GetAttributes(), out AttributeData? columnAttribute) ||
                columnAttribute == null ||
                IsNoSerializeColumn(columnAttribute))
            {
                continue;
            }

            if (fieldSymbol.Type is INamedTypeSymbol namedType && TryGetInlineArrayLength(namedType, out int arrayLength))
            {
                if (!TryGetInlineArrayElementType(namedType, out ITypeSymbol elementType))
                {
                    continue;
                }

                fields.Add(FieldInfo.ForInlineArray(fieldSymbol.Name, elementType, arrayLength));
                continue;
            }

            fields.Add(FieldInfo.ForScalar(fieldSymbol.Name, fieldSymbol.Type));
        }

        if (fields.Count == 0)
        {
            return default;
        }

        return new ComponentInfo(poolId, componentSymbol, fields);
    }

    private static bool TryGetColumnAttribute(ImmutableArray<AttributeData> attributes, out AttributeData? column)
    {
        column = null;
        for (int i = 0; i < attributes.Length; i++)
        {
            AttributeData attributeData = attributes[i];
            if (attributeData.AttributeClass == null)
            {
                continue;
            }

            if (attributeData.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + ColumnAttributeMetadataName)
            {
                column = attributeData;
                return true;
            }
        }

        return false;
    }

    private static bool IsNoSerializeColumn(AttributeData columnAttribute)
    {
        foreach (KeyValuePair<string, TypedConstant> kvp in columnAttribute.NamedArguments)
        {
            if (kvp.Key != "NoSerialize" || kvp.Value.Kind != TypedConstantKind.Primitive)
            {
                continue;
            }

            if (kvp.Value.Value is bool b)
            {
                return b;
            }
        }

        return false;
    }

    private static bool TryGetInlineArrayLength(INamedTypeSymbol bufferType, out int length)
    {
        length = 0;
        foreach (AttributeData attributeData in bufferType.GetAttributes())
        {
            if (attributeData.AttributeClass == null)
            {
                continue;
            }

            if (attributeData.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::" + InlineArrayAttributeMetadataName)
            {
                continue;
            }

            if (attributeData.ConstructorArguments.Length == 1 &&
                attributeData.ConstructorArguments[0].Kind == TypedConstantKind.Primitive &&
                attributeData.ConstructorArguments[0].Value is int len &&
                len > 0)
            {
                length = len;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryGetInlineArrayElementType(INamedTypeSymbol bufferType, out ITypeSymbol elementType)
    {
        elementType = bufferType;

        foreach (ISymbol member in bufferType.GetMembers())
        {
            if (member is not IFieldSymbol field)
            {
                continue;
            }

            if (field.IsStatic)
            {
                continue;
            }

            elementType = field.Type;
            return true;
        }

        return false;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<ComponentInfo> infos)
    {
        if (infos.IsDefaultOrEmpty)
        {
            return;
        }

        var ordered = new List<ComponentInfo>(capacity: infos.Length);
        var seenPoolIds = new HashSet<int>();

        for (int i = 0; i < infos.Length; i++)
        {
            ComponentInfo info = infos[i];
            if (!info.IsValid)
            {
                continue;
            }

            if (!seenPoolIds.Add(info.PoolId))
            {
                continue;
            }

            ordered.Add(info);
        }

        ordered.Sort(static (left, right) => left.PoolId.CompareTo(right.PoolId));

        var sb = new StringBuilder(capacity: 16384);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using Pooled.Runtime;");
        sb.AppendLine("using Property.Runtime;");
        sb.AppendLine();
        sb.AppendLine("namespace Derp.UI;");
        sb.AppendLine();
        sb.AppendLine("internal static class UiComponentClipboardPacker");
        sb.AppendLine("{");
        sb.AppendLine("    public static int GetSnapshotSize(ushort componentKind)");
        sb.AppendLine("    {");
        sb.AppendLine("        return componentKind switch");
        sb.AppendLine("        {");

        for (int i = 0; i < ordered.Count; i++)
        {
            ComponentInfo info = ordered[i];
            string componentTypeName = info.ComponentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            sb.AppendLine("            " + info.PoolId.ToString(CultureInfo.InvariantCulture) + " => Unsafe.SizeOf<" + componentTypeName + ">(),");
        }

        sb.AppendLine("            _ => 0");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static bool TryCreateComponent(IPoolRegistry propertyWorld, ushort componentKind, uint stableId, out AnyComponentHandle component)");
        sb.AppendLine("    {");
        sb.AppendLine("        component = default;");
        sb.AppendLine("        if (stableId == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new ArgumentOutOfRangeException(nameof(stableId), \"StableId must be non-zero.\");");
        sb.AppendLine("        }");
        sb.AppendLine("        switch (componentKind)");
        sb.AppendLine("        {");

        for (int i = 0; i < ordered.Count; i++)
        {
            ComponentInfo info = ordered[i];
            string componentTypeName = info.ComponentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string apiTypeName = componentTypeName + ".Api";
            string idTypeName = componentTypeName + "Id";
            sb.AppendLine("            case " + info.PoolId.ToString(CultureInfo.InvariantCulture) + ":");
            sb.AppendLine("            {");
            sb.AppendLine("                var view = " + apiTypeName + ".CreateWithId(propertyWorld, new " + idTypeName + "((ulong)stableId), default(" + componentTypeName + "));");
            sb.AppendLine("                component = new AnyComponentHandle(" + info.PoolId.ToString(CultureInfo.InvariantCulture) + ", view.Handle.Index, view.Handle.Generation);");
            sb.AppendLine("                return component.IsValid;");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static bool TryMapStableId(IPoolRegistry propertyWorld, in AnyComponentHandle component, uint stableId)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!component.IsValid)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        if (stableId == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new ArgumentOutOfRangeException(nameof(stableId), \"StableId must be non-zero.\");");
        sb.AppendLine("        }");
        sb.AppendLine("        switch (component.Kind)");
        sb.AppendLine("        {");

        for (int i = 0; i < ordered.Count; i++)
        {
            ComponentInfo info = ordered[i];
            string componentTypeName = info.ComponentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string handleTypeName = componentTypeName + "Handle";
            string idTypeName = componentTypeName + "Id";
            string poolGetExpr = componentTypeName + ".Api.GetPoolForProperties(propertyWorld)";

            sb.AppendLine("            case " + info.PoolId.ToString(CultureInfo.InvariantCulture) + ":");
            sb.AppendLine("            {");
            sb.AppendLine("                var handle = new " + handleTypeName + "(component.Index, component.Generation);");
            sb.AppendLine("                var pool = " + poolGetExpr + ";");
            sb.AppendLine("                if (pool.TryGetId(handle, out var existing) && existing.Value != (ulong)stableId)");
            sb.AppendLine("                {");
            sb.AppendLine("                    throw new InvalidOperationException(\"Component stable id mismatch.\");");
            sb.AppendLine("                }");
            sb.AppendLine("                pool.MapIdToHandle(new " + idTypeName + "((ulong)stableId), handle);");
            sb.AppendLine("                return true;");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static bool TryPack(IPoolRegistry propertyWorld, in AnyComponentHandle component, Span<byte> dst)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (component.Kind)");
        sb.AppendLine("        {");

        for (int i = 0; i < ordered.Count; i++)
        {
            ComponentInfo info = ordered[i];
            sb.AppendLine("            case " + info.PoolId.ToString(CultureInfo.InvariantCulture) + ":");
            sb.AppendLine("                return TryPack_" + info.ComponentType.Name + "(propertyWorld, component, dst);");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static bool TryUnpack(IPoolRegistry propertyWorld, in AnyComponentHandle component, ReadOnlySpan<byte> src)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (component.Kind)");
        sb.AppendLine("        {");

        for (int i = 0; i < ordered.Count; i++)
        {
            ComponentInfo info = ordered[i];
            sb.AppendLine("            case " + info.PoolId.ToString(CultureInfo.InvariantCulture) + ":");
            sb.AppendLine("                return TryUnpack_" + info.ComponentType.Name + "(propertyWorld, component, src);");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        for (int i = 0; i < ordered.Count; i++)
        {
            EmitPackUnpack(sb, ordered[i]);
        }

        sb.AppendLine("}");

        context.AddSource("UiComponentClipboardPacker.g.cs", sb.ToString());
    }

    private static void EmitPackUnpack(StringBuilder sb, ComponentInfo info)
    {
        string componentTypeName = info.ComponentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string handleTypeName = "global::Derp.UI." + info.ComponentType.Name + "Handle";
        string apiTypeName = componentTypeName + ".Api";
        string poolGetExpr = apiTypeName + ".GetPoolForProperties(propertyWorld)";

        sb.AppendLine();
        sb.AppendLine("    private static bool TryPack_" + info.ComponentType.Name + "(IPoolRegistry propertyWorld, in AnyComponentHandle component, Span<byte> dst)");
        sb.AppendLine("    {");
        sb.AppendLine("        int size = Unsafe.SizeOf<" + componentTypeName + ">();");
        sb.AppendLine("        if ((uint)dst.Length < (uint)size)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var handle = new " + handleTypeName + "(component.Index, component.Generation);");
        sb.AppendLine("        var view = " + apiTypeName + ".FromHandle(propertyWorld, handle);");
        sb.AppendLine("        if (!view.IsAlive)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        " + componentTypeName + " snapshot = default;");
        sb.AppendLine("        var pool = " + poolGetExpr + ";");

        for (int i = 0; i < info.Fields.Count; i++)
        {
            FieldInfo field = info.Fields[i];
            if (field.IsInlineArray)
            {
                string elementTypeName = field.ElementType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                sb.AppendLine("        ref var buffer" + field.Name + " = ref pool." + field.Name + "Ref(handle);");
                sb.AppendLine("        Span<" + elementTypeName + "> src" + field.Name + " = MemoryMarshal.CreateSpan(ref buffer" + field.Name + "[0], " + field.ArrayLength.ToString(CultureInfo.InvariantCulture) + ");");
                sb.AppendLine("        for (int i = 0; i < " + field.ArrayLength.ToString(CultureInfo.InvariantCulture) + "; i++)");
                sb.AppendLine("        {");
                sb.AppendLine("            snapshot." + field.Name + "[i] = src" + field.Name + "[i];");
                sb.AppendLine("        }");
            }
            else
            {
                sb.AppendLine("        snapshot." + field.Name + " = view." + field.Name + ";");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        MemoryMarshal.Write(dst, in snapshot);");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    private static bool TryUnpack_" + info.ComponentType.Name + "(IPoolRegistry propertyWorld, in AnyComponentHandle component, ReadOnlySpan<byte> src)");
        sb.AppendLine("    {");
        sb.AppendLine("        int size = Unsafe.SizeOf<" + componentTypeName + ">();");
        sb.AppendLine("        if ((uint)src.Length < (uint)size)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var handle = new " + handleTypeName + "(component.Index, component.Generation);");
        sb.AppendLine("        var view = " + apiTypeName + ".FromHandle(propertyWorld, handle);");
        sb.AppendLine("        if (!view.IsAlive)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        " + componentTypeName + " snapshot = MemoryMarshal.Read<" + componentTypeName + ">(src);");
        sb.AppendLine("        var pool = " + poolGetExpr + ";");

        for (int i = 0; i < info.Fields.Count; i++)
        {
            FieldInfo field = info.Fields[i];
            if (field.IsInlineArray)
            {
                string elementTypeName = field.ElementType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                sb.AppendLine("        ref var buffer" + field.Name + " = ref pool." + field.Name + "Ref(handle);");
                sb.AppendLine("        Span<" + elementTypeName + "> dst" + field.Name + " = MemoryMarshal.CreateSpan(ref buffer" + field.Name + "[0], " + field.ArrayLength.ToString(CultureInfo.InvariantCulture) + ");");
                sb.AppendLine("        for (int i = 0; i < " + field.ArrayLength.ToString(CultureInfo.InvariantCulture) + "; i++)");
                sb.AppendLine("        {");
                sb.AppendLine("            dst" + field.Name + "[i] = snapshot." + field.Name + "[i];");
                sb.AppendLine("        }");
            }
            else
            {
                sb.AppendLine("        view." + field.Name + " = snapshot." + field.Name + ";");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
    }

    private readonly struct ComponentInfo
    {
        public readonly int PoolId;
        public readonly INamedTypeSymbol ComponentType;
        public readonly List<FieldInfo> Fields;

        public bool IsValid => PoolId > 0 && ComponentType != null && Fields != null && Fields.Count > 0;

        public ComponentInfo(int poolId, INamedTypeSymbol componentType, List<FieldInfo> fields)
        {
            PoolId = poolId;
            ComponentType = componentType;
            Fields = fields;
        }
    }

    private readonly struct FieldInfo
    {
        public readonly string Name;
        public readonly bool IsInlineArray;
        public readonly ITypeSymbol? ElementType;
        public readonly int ArrayLength;

        private FieldInfo(string name, bool isInlineArray, ITypeSymbol? elementType, int arrayLength)
        {
            Name = name;
            IsInlineArray = isInlineArray;
            ElementType = elementType;
            ArrayLength = arrayLength;
        }

        public static FieldInfo ForScalar(string name, ITypeSymbol type)
        {
            return new FieldInfo(name, isInlineArray: false, elementType: null, arrayLength: 0);
        }

        public static FieldInfo ForInlineArray(string name, ITypeSymbol elementType, int length)
        {
            return new FieldInfo(name, isInlineArray: true, elementType: elementType, arrayLength: length);
        }
    }
}
