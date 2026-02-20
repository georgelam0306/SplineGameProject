using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DerpLib.Ecs.Editor.Generator;

[Generator]
public sealed class EcsPropertyCatalogGenerator : IIncrementalGenerator
{
    private const string EcsComponentInterfaceFqn = "global::DerpLib.Ecs.IEcsComponent";
    private const string PropertyAttributeFqn = "global::Property.PropertyAttribute";
    private const string PropertyKindFqn = "global::Property.PropertyKind";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> componentTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is StructDeclarationSyntax s && s.Modifiers.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, _) => TryGetComponentType(ctx))
            .Where(static t => t is not null)!
            .Select(static (t, _) => t!);

        IncrementalValueProvider<ImmutableArray<INamedTypeSymbol>> allComponents = componentTypes.Collect();

        IncrementalValueProvider<(Compilation, ImmutableArray<INamedTypeSymbol>)> compilationAndComponents =
            context.CompilationProvider.Combine(allComponents);

        context.RegisterSourceOutput(compilationAndComponents, static (spc, tuple) =>
        {
            try
            {
                EmitAll(spc, tuple.Item1, tuple.Item2);
            }
            catch (Exception ex)
            {
                string details = ex.GetType().FullName + ": " + ex.Message;
                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    details += " | " + ex.StackTrace.Replace("\r", " ").Replace("\n", " ");
                }
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.GeneratorError, Location.None, details));
            }
        });
    }

    private static INamedTypeSymbol? TryGetComponentType(GeneratorSyntaxContext ctx)
    {
        var structDecl = (StructDeclarationSyntax)ctx.Node;
        if (structDecl.Modifiers.All(m => !m.IsKind(SyntaxKind.PartialKeyword)))
        {
            return null;
        }

        INamedTypeSymbol? symbol = ctx.SemanticModel.GetDeclaredSymbol(structDecl) as INamedTypeSymbol;
        if (symbol == null)
        {
            return null;
        }

        if (symbol.TypeKind != TypeKind.Struct)
        {
            return null;
        }

        if (symbol.IsGenericType || (symbol.ContainingType != null && symbol.ContainingType.IsGenericType))
        {
            return null;
        }

        if (!ImplementsEcsComponent(symbol))
        {
            return null;
        }

        return symbol;
    }

    private static bool ImplementsEcsComponent(INamedTypeSymbol symbol)
    {
        foreach (INamedTypeSymbol iface in symbol.AllInterfaces)
        {
            string fqn = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (fqn == EcsComponentInterfaceFqn)
            {
                return true;
            }
        }
        return false;
    }

    private static void EmitAll(SourceProductionContext spc, Compilation compilation, ImmutableArray<INamedTypeSymbol> components)
    {
        if (components.IsDefaultOrEmpty)
        {
            return;
        }

        var ordered = new List<INamedTypeSymbol>(components.Length);
        for (int i = 0; i < components.Length; i++)
        {
            INamedTypeSymbol t = components[i];
            if (!ordered.Contains(t, SymbolEqualityComparer.Default))
            {
                ordered.Add(t);
            }
        }

        ordered.Sort(static (a, b) =>
        {
            string an = a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string bn = b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.CompareOrdinal(an, bn);
        });

        var emittableComponentTypes = new List<INamedTypeSymbol>(ordered.Count);

        for (int i = 0; i < ordered.Count; i++)
        {
            INamedTypeSymbol componentType = ordered[i];
            if (TryGetEmittableFields(spc, componentType, out List<IFieldSymbol>? fields) && fields.Count != 0)
            {
                emittableComponentTypes.Add(componentType);
            }

            EmitComponent(spc, componentType);
        }

        EmitRegistry(spc, compilation, emittableComponentTypes);
    }

    private static void EmitComponent(SourceProductionContext spc, INamedTypeSymbol componentType)
    {
        var candidates = new List<IFieldSymbol>();
        foreach (ISymbol member in componentType.GetMembers())
        {
            if (member is not IFieldSymbol field)
            {
                continue;
            }

            if (field.IsStatic)
            {
                continue;
            }

            if (HasPropertyAttribute(field))
            {
                candidates.Add(field);
                continue;
            }

            if (field.DeclaredAccessibility == Accessibility.Public && CanAutoIncludeField(field.Type))
            {
                candidates.Add(field);
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var fields = new List<IFieldSymbol>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            IFieldSymbol field = candidates[i];
            PropertyAttr meta = ReadPropertyAttr(field);
            string kindLiteral = GetPropertyKindLiteral(field.Type, meta.KindLiteral);
            if (kindLiteral.Length == 0)
            {
                if (HasPropertyAttribute(field))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.UnsupportedPropertyType,
                        field.Locations.Length > 0 ? field.Locations[0] : Location.None,
                        field.Name,
                        field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    return;
                }

                continue;
            }

            fields.Add(field);
        }

        if (fields.Count == 0)
        {
            return;
        }

        string componentFqn = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string namespaceName = componentType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : componentType.ContainingNamespace.ToDisplayString();

        string className = componentType.Name + "EcsProperties";
        ulong schemaId = Fnv1a64.Compute(componentFqn);

        var sb = new StringBuilder(16_384);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using Core;");
        sb.AppendLine("using Property;");
        sb.AppendLine("using Property.Runtime;");
        sb.AppendLine();

        if (namespaceName.Length != 0)
        {
            sb.Append("namespace ").Append(namespaceName).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("public static class ").Append(className).AppendLine();
        sb.AppendLine("{");
        sb.Append("\tpublic const ulong SchemaId = 0x").Append(schemaId.ToString("X16", CultureInfo.InvariantCulture)).AppendLine("UL;");
        sb.Append("\tpublic const int PropertyCount = ").Append(fields.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        sb.AppendLine();

        for (int i = 0; i < fields.Count; i++)
        {
            ulong propertyId = Fnv1a64.Compute(componentFqn + "." + fields[i].Name);
            sb.Append("\tpublic const ulong ").Append(fields[i].Name).Append("Id = 0x").Append(propertyId.ToString("X16", CultureInfo.InvariantCulture)).AppendLine("UL;");
        }

        sb.AppendLine();
        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append("\tprivate static StringHandle _name").Append(fields[i].Name).AppendLine(";");
            sb.Append("\tprivate static StringHandle _group").Append(fields[i].Name).AppendLine(";");
            sb.Append("\tprivate static PropertyInfo _info").Append(fields[i].Name).AppendLine(";");
        }

        sb.AppendLine();
        sb.AppendLine("\tpublic static void RegisterNames()");
        sb.AppendLine("\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            PropertyAttr meta = ReadPropertyAttr(field);
            string displayName = meta.Name.Length != 0 ? meta.Name : field.Name;
            string groupName = meta.Group;

            sb.Append("\t\t_name").Append(field.Name).Append(" = \"").Append(Escape(displayName)).AppendLine("\";");
            sb.Append("\t\t_group").Append(field.Name).Append(" = \"").Append(Escape(groupName)).AppendLine("\";");

            string kindLiteral = GetPropertyKindLiteral(field.Type, meta.KindLiteral);

            sb.Append("\t\t_info").Append(field.Name).Append(" = new PropertyInfo(")
                .Append(field.Name).Append("Id, _name").Append(field.Name).Append(", ")
                .Append(kindLiteral).Append(", _group").Append(field.Name).Append(", ")
                .Append(meta.Order.ToString(CultureInfo.InvariantCulture)).Append(", ")
                .Append(FormatFloat(meta.Min)).Append(", ")
                .Append(FormatFloat(meta.Max)).Append(", ")
                .Append(FormatFloat(meta.Step)).Append(", ")
                .Append(meta.FlagsLiteral).Append(", ")
                .Append("false, false, 0UL, (ushort)0, (ushort)0);")
                .AppendLine();
        }
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryGetInfoByIndex(int index, out PropertyInfo info)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(":");
            sb.Append("\t\t\t\tinfo = _info").Append(fields[i].Name).AppendLine(";");
            sb.AppendLine("\t\t\t\treturn true;");
        }
        sb.AppendLine("\t\t\tdefault:");
        sb.AppendLine("\t\t\t\tinfo = default;");
        sb.AppendLine("\t\t\t\treturn false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryGetPropertyIdByIndex(int index, out ulong propertyId)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": propertyId = ").Append(fields[i].Name).AppendLine("Id; return true;");
        }
        sb.AppendLine("\t\t\tdefault: propertyId = 0UL; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryGetIndexByPropertyId(ulong propertyId, out int index)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (propertyId)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append("\t\t\tcase ").Append(fields[i].Name).Append("Id: index = ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: index = -1; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadFloat(ref ").Append(componentFqn).AppendLine(" component, int index, out float value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Float")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteFloat(ref ").Append(componentFqn).AppendLine(" component, int index, float value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Float")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadInt(ref ").Append(componentFqn).AppendLine(" component, int index, out int value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Int")
            {
                continue;
            }
            if (field.Type.TypeKind == TypeKind.Enum)
            {
                sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = (int)component.").Append(field.Name).AppendLine("; return true;");
            }
            else
            {
                sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
            }
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteInt(ref ").Append(componentFqn).AppendLine(" component, int index, int value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Int")
            {
                continue;
            }
            if (field.Type.TypeKind == TypeKind.Enum)
            {
                string enumFqn = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).Append(" = (").Append(enumFqn).AppendLine(")value; return true;");
            }
            else
            {
                sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
            }
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadBool(ref ").Append(componentFqn).AppendLine(" component, int index, out bool value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Bool")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteBool(ref ").Append(componentFqn).AppendLine(" component, int index, bool value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Bool")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadVec2(ref ").Append(componentFqn).AppendLine(" component, int index, out global::System.Numerics.Vector2 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Vec2")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteVec2(ref ").Append(componentFqn).AppendLine(" component, int index, global::System.Numerics.Vector2 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Vec2")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadVec3(ref ").Append(componentFqn).AppendLine(" component, int index, out global::System.Numerics.Vector3 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Vec3")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteVec3(ref ").Append(componentFqn).AppendLine(" component, int index, global::System.Numerics.Vector3 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Vec3")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadVec4(ref ").Append(componentFqn).AppendLine(" component, int index, out global::System.Numerics.Vector4 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Vec4")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteVec4(ref ").Append(componentFqn).AppendLine(" component, int index, global::System.Numerics.Vector4 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Vec4")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadColor32(ref ").Append(componentFqn).AppendLine(" component, int index, out global::Core.Color32 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Color32")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteColor32(ref ").Append(componentFqn).AppendLine(" component, int index, global::Core.Color32 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Color32")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadStringHandle(ref ").Append(componentFqn).AppendLine(" component, int index, out global::Core.StringHandle value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.StringHandle")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteStringHandle(ref ").Append(componentFqn).AppendLine(" component, int index, global::Core.StringHandle value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.StringHandle")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadFixed64(ref ").Append(componentFqn).AppendLine(" component, int index, out global::FixedMath.Fixed64 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Fixed64")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteFixed64(ref ").Append(componentFqn).AppendLine(" component, int index, global::FixedMath.Fixed64 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Fixed64")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadFixed64Vec2(ref ").Append(componentFqn).AppendLine(" component, int index, out global::FixedMath.Fixed64Vec2 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Fixed64Vec2")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteFixed64Vec2(ref ").Append(componentFqn).AppendLine(" component, int index, global::FixedMath.Fixed64Vec2 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Fixed64Vec2")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryReadFixed64Vec3(ref ").Append(componentFqn).AppendLine(" component, int index, out global::FixedMath.Fixed64Vec3 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Fixed64Vec3")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": value = component.").Append(field.Name).AppendLine("; return true;");
        }
        sb.AppendLine("\t\t\tdefault: value = default; return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine();
        sb.AppendLine("\tpublic static bool TryWriteFixed64Vec3(ref ").Append(componentFqn).AppendLine(" component, int index, global::FixedMath.Fixed64Vec3 value)");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tswitch (index)");
        sb.AppendLine("\t\t{");
        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            string kindLiteral = GetPropertyKindLiteral(field.Type, ReadPropertyAttr(field).KindLiteral);
            if (kindLiteral != "global::Property.PropertyKind.Fixed64Vec3")
            {
                continue;
            }
            sb.Append("\t\t\tcase ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": component.").Append(field.Name).AppendLine(" = value; return true;");
        }
        sb.AppendLine("\t\t\tdefault: return false;");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");

        sb.AppendLine("}");

        spc.AddSource(componentType.Name + ".EcsProperties." + schemaId.ToString("X16", CultureInfo.InvariantCulture) + ".g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        EmitEditProxy(spc, componentType, fields, schemaId);
    }

    private static void EmitEditProxy(SourceProductionContext spc, INamedTypeSymbol componentType, List<IFieldSymbol> fields, ulong schemaId)
    {
        string namespaceName = componentType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : componentType.ContainingNamespace.ToDisplayString();

        string componentName = componentType.Name;
        string accessorName = TrimComponentSuffix(componentName);
        string editClassName = componentName + "EcsEdit";
        string propsClassName = componentName + "EcsProperties";

        var sb = new StringBuilder(16_384);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using DerpLib.Ecs;");
        sb.AppendLine("using DerpLib.Ecs.Editor;");
        sb.AppendLine();

        if (namespaceName.Length != 0)
        {
            sb.Append("namespace ").Append(namespaceName).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("public static class ").Append(editClassName).AppendLine();
        sb.AppendLine("{");

        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append("    private const ushort ").Append(fields[i].Name).Append("Index = ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        }

        sb.AppendLine();
        sb.Append("    public readonly struct ").Append(componentName).AppendLine("EditProxy");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly EntityHandle _entity;");
        sb.AppendLine("        private readonly EcsEditMode _mode;");
        sb.AppendLine();
        sb.Append("        internal ").Append(componentName).AppendLine("EditProxy(EntityHandle entity, EcsEditMode mode)");
        sb.AppendLine("        {");
            sb.AppendLine("            _entity = entity;");
        sb.AppendLine("            _mode = mode;");
        sb.AppendLine("        }");
        sb.AppendLine();

        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol field = fields[i];
            PropertyAttr meta = ReadPropertyAttr(field);
            string kindLiteral = GetPropertyKindLiteral(field.Type, meta.KindLiteral);
            if (!TryGetEditorSetter(kindLiteral, out string setterName, out string valueTypeFqn))
            {
                continue;
            }

            bool isEnumInt = kindLiteral == "global::Property.PropertyKind.Int" && field.Type.TypeKind == TypeKind.Enum;
            string propertyValueType = isEnumInt
                ? field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : valueTypeFqn;

            sb.Append("        public ").Append(propertyValueType).Append(' ').Append(field.Name).AppendLine();
            sb.AppendLine("        {");
            sb.AppendLine("            set");
            sb.AppendLine("            {");
            sb.Append("                var address = new EcsEditorPropertyAddress(_entity, ").Append(propsClassName).Append(".SchemaId, ").Append(field.Name).Append("Index, ").Append(propsClassName).Append('.').Append(field.Name).AppendLine("Id);");
            sb.Append("                EditorContext.Enqueue(EcsEditorCommand.").Append(setterName).Append("(_mode, in address, ");
            if (isEnumInt)
            {
                sb.Append("(int)value");
            }
            else
            {
                sb.Append("value");
            }
            sb.Append("));").AppendLine();
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource(componentType.Name + ".EcsEdit." + schemaId.ToString("X16", CultureInfo.InvariantCulture) + ".g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static bool TryGetEditorSetter(string kindLiteral, out string setterName, out string valueTypeFqn)
    {
        if (kindLiteral == "global::Property.PropertyKind.Float")
        {
            setterName = "SetFloat";
            valueTypeFqn = "float";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Int")
        {
            setterName = "SetInt";
            valueTypeFqn = "int";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Bool")
        {
            setterName = "SetBool";
            valueTypeFqn = "bool";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Vec2")
        {
            setterName = "SetVec2";
            valueTypeFqn = "global::System.Numerics.Vector2";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Vec3")
        {
            setterName = "SetVec3";
            valueTypeFqn = "global::System.Numerics.Vector3";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Vec4")
        {
            setterName = "SetVec4";
            valueTypeFqn = "global::System.Numerics.Vector4";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Color32")
        {
            setterName = "SetColor32";
            valueTypeFqn = "global::Core.Color32";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.StringHandle")
        {
            setterName = "SetStringHandle";
            valueTypeFqn = "global::Core.StringHandle";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Fixed64")
        {
            setterName = "SetFixed64";
            valueTypeFqn = "global::FixedMath.Fixed64";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Fixed64Vec2")
        {
            setterName = "SetFixed64Vec2";
            valueTypeFqn = "global::FixedMath.Fixed64Vec2";
            return true;
        }
        if (kindLiteral == "global::Property.PropertyKind.Fixed64Vec3")
        {
            setterName = "SetFixed64Vec3";
            valueTypeFqn = "global::FixedMath.Fixed64Vec3";
            return true;
        }

        setterName = string.Empty;
        valueTypeFqn = string.Empty;
        return false;
    }

    private static string TrimComponentSuffix(string typeName)
    {
        const string suffix = "Component";
        if (typeName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return typeName.Substring(0, typeName.Length - suffix.Length);
        }

        return typeName;
    }

    private static bool TryGetEmittableFields(SourceProductionContext spc, INamedTypeSymbol componentType, out List<IFieldSymbol> fields)
    {
        var candidates = new List<IFieldSymbol>();
        foreach (ISymbol member in componentType.GetMembers())
        {
            if (member is not IFieldSymbol field)
            {
                continue;
            }

            if (field.IsStatic)
            {
                continue;
            }

            if (HasPropertyAttribute(field))
            {
                candidates.Add(field);
                continue;
            }

            if (field.DeclaredAccessibility == Accessibility.Public && CanAutoIncludeField(field.Type))
            {
                candidates.Add(field);
            }
        }

        fields = new List<IFieldSymbol>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            IFieldSymbol field = candidates[i];
            PropertyAttr meta = ReadPropertyAttr(field);
            string kindLiteral = GetPropertyKindLiteral(field.Type, meta.KindLiteral);
            if (kindLiteral.Length == 0)
            {
                if (HasPropertyAttribute(field))
                {
                    fields = new List<IFieldSymbol>(0);
                    return false;
                }

                continue;
            }

            fields.Add(field);
        }

        return true;
    }

    private static void EmitRegistry(SourceProductionContext spc, Compilation compilation, List<INamedTypeSymbol> emittableComponentTypes)
    {
        if (emittableComponentTypes.Count == 0)
        {
            return;
        }

        var propsTypes = new List<string>(emittableComponentTypes.Count);
        for (int i = 0; i < emittableComponentTypes.Count; i++)
        {
            INamedTypeSymbol componentType = emittableComponentTypes[i];
            if (componentType.ContainingNamespace.IsGlobalNamespace)
            {
                propsTypes.Add("global::" + componentType.Name + "EcsProperties");
            }
            else
            {
                propsTypes.Add("global::" + componentType.ContainingNamespace.ToDisplayString() + "." + componentType.Name + "EcsProperties");
            }
        }

        propsTypes.Sort(StringComparer.Ordinal);

        var sb = new StringBuilder(4096);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace DerpLib.Ecs.Editor.Generated;");
        sb.AppendLine();
        sb.AppendLine("public static class EcsEditorPropertyRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    public static void RegisterAll()");
        sb.AppendLine("    {");
        for (int i = 0; i < propsTypes.Count; i++)
        {
            sb.Append("        ").Append(propsTypes[i]).AppendLine(".RegisterNames();");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("DerpLib.Ecs.Editor.Generated.EcsEditorPropertyRegistry.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

        EmitAutoInit(spc, compilation);
    }

    private static void EmitAutoInit(SourceProductionContext spc, Compilation compilation)
    {
        bool hasModuleInitializer = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute") != null;
        if (!hasModuleInitializer)
        {
            var sbAttr = new StringBuilder(512);
            sbAttr.AppendLine("// <auto-generated/>");
            sbAttr.AppendLine("#nullable enable");
            sbAttr.AppendLine("using System;");
            sbAttr.AppendLine();
            sbAttr.AppendLine("namespace System.Runtime.CompilerServices;");
            sbAttr.AppendLine();
            sbAttr.AppendLine("[AttributeUsage(AttributeTargets.Method, Inherited = false)]");
            sbAttr.AppendLine("internal sealed class ModuleInitializerAttribute : Attribute");
            sbAttr.AppendLine("{");
            sbAttr.AppendLine("}");
            spc.AddSource("DerpLib.Ecs.Editor.Generated.ModuleInitializerAttribute.g.cs", SourceText.From(sbAttr.ToString(), Encoding.UTF8));
        }

        var sb = new StringBuilder(512);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace DerpLib.Ecs.Editor.Generated;");
        sb.AppendLine();
        sb.AppendLine("internal static class EcsEditorPropertyAutoInit");
        sb.AppendLine("{");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void Init()");
        sb.AppendLine("    {");
        sb.AppendLine("        EcsEditorPropertyRegistry.RegisterAll();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        spc.AddSource("DerpLib.Ecs.Editor.Generated.EcsEditorPropertyAutoInit.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static bool HasPropertyAttribute(IFieldSymbol field)
    {
        foreach (AttributeData attr in field.GetAttributes())
        {
            if (attr.AttributeClass == null)
            {
                continue;
            }
            if (attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == PropertyAttributeFqn)
            {
                return true;
            }
        }
        return false;
    }

    private static PropertyAttr ReadPropertyAttr(IFieldSymbol field)
    {
        PropertyAttr meta = PropertyAttr.CreateDefault();
        foreach (AttributeData attr in field.GetAttributes())
        {
            if (attr.AttributeClass == null)
            {
                continue;
            }
            if (attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != PropertyAttributeFqn)
            {
                continue;
            }

            foreach (KeyValuePair<string, TypedConstant> kvp in attr.NamedArguments)
            {
                string name = kvp.Key;
                TypedConstant value = kvp.Value;
                switch (name)
                {
                    case "Name":
                        meta.Name = value.Value as string ?? string.Empty;
                        break;
                    case "Group":
                        meta.Group = value.Value as string ?? string.Empty;
                        break;
                    case "Order":
                        meta.Order = value.Value is int i ? i : 0;
                        break;
                    case "Min":
                        meta.Min = value.Value is float f0 ? f0 : float.NaN;
                        break;
                    case "Max":
                        meta.Max = value.Value is float f1 ? f1 : float.NaN;
                        break;
                    case "Step":
                        meta.Step = value.Value is float f2 ? f2 : float.NaN;
                        break;
                    case "Flags":
                        meta.FlagsLiteral = FormatEnumCast(value);
                        break;
                    case "Kind":
                        meta.KindLiteral = FormatPropertyKindLiteral(value);
                        break;
                }
            }
            return meta;
        }

        return meta;
    }

    private static string FormatPropertyKindLiteral(TypedConstant value)
    {
        if (value.Value == null)
        {
            return PropertyKindFqn + ".Auto";
        }

        ulong raw;
        object v = value.Value;
        if (v is byte b) raw = b;
        else if (v is sbyte sb) raw = unchecked((ulong)sb);
        else if (v is short s) raw = unchecked((ulong)s);
        else if (v is ushort us) raw = us;
        else if (v is int i) raw = unchecked((ulong)i);
        else if (v is uint ui) raw = ui;
        else if (v is long l) raw = unchecked((ulong)l);
        else if (v is ulong ul) raw = ul;
        else return PropertyKindFqn + ".Auto";

        return raw switch
        {
            0 => "global::Property.PropertyKind.Auto",
            1 => "global::Property.PropertyKind.Float",
            2 => "global::Property.PropertyKind.Int",
            3 => "global::Property.PropertyKind.Bool",
            4 => "global::Property.PropertyKind.Vec2",
            5 => "global::Property.PropertyKind.Vec3",
            6 => "global::Property.PropertyKind.Vec4",
            7 => "global::Property.PropertyKind.Color32",
            8 => "global::Property.PropertyKind.StringHandle",
            9 => "global::Property.PropertyKind.Fixed64",
            10 => "global::Property.PropertyKind.Fixed64Vec2",
            11 => "global::Property.PropertyKind.Fixed64Vec3",
            12 => "global::Property.PropertyKind.PrefabRef",
            13 => "global::Property.PropertyKind.ShapeRef",
            14 => "global::Property.PropertyKind.List",
            15 => "global::Property.PropertyKind.Trigger",
            _ => "(" + PropertyKindFqn + ")" + raw.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatEnumCast(TypedConstant value)
    {
        if (value.Type == null)
        {
            return string.Empty;
        }

        string enumTypeFqn = value.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        ulong raw = 0UL;
        object? v = value.Value;
        if (v is byte b) raw = b;
        else if (v is sbyte sb) raw = unchecked((ulong)sb);
        else if (v is short s) raw = unchecked((ulong)s);
        else if (v is ushort us) raw = us;
        else if (v is int i) raw = unchecked((ulong)i);
        else if (v is uint ui) raw = ui;
        else if (v is long l) raw = unchecked((ulong)l);
        else if (v is ulong ul) raw = ul;
        else return string.Empty;

        return "(" + enumTypeFqn + ")" + raw.ToString(CultureInfo.InvariantCulture);
    }

    private static PropertyKindInfo GetPropertyKindInfo(ITypeSymbol type, string requestedKindLiteral, SourceProductionContext spc, IFieldSymbol field)
    {
        string kindLiteral = GetPropertyKindLiteral(type, requestedKindLiteral);
        if (kindLiteral.Length == 0)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnsupportedPropertyType,
                field.Locations.Length > 0 ? field.Locations[0] : Location.None,
                field.Name,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

            return default;
        }

        return new PropertyKindInfo(kindLiteral);
    }

    private static string GetPropertyKindLiteral(ITypeSymbol type, string requestedKindLiteral)
    {
        if (!TryGetPropertyKindLiteralForType(type, out string expectedKindLiteral))
        {
            return string.Empty;
        }

        if (requestedKindLiteral.Length != 0 && requestedKindLiteral != PropertyKindFqn + ".Auto")
        {
            return requestedKindLiteral == expectedKindLiteral ? requestedKindLiteral : string.Empty;
        }

        return expectedKindLiteral;
    }

    private static bool CanAutoIncludeField(ITypeSymbol type)
    {
        return TryGetPropertyKindLiteralForType(type, out _);
    }

    private static bool TryGetPropertyKindLiteralForType(ITypeSymbol type, out string kindLiteral)
    {
        if (type.TypeKind == TypeKind.Enum &&
            type is INamedTypeSymbol enumType &&
            enumType.EnumUnderlyingType != null)
        {
            switch (enumType.EnumUnderlyingType.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                    kindLiteral = "global::Property.PropertyKind.Int";
                    return true;
            }
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Single:
                kindLiteral = "global::Property.PropertyKind.Float";
                return true;
            case SpecialType.System_Int32:
                kindLiteral = "global::Property.PropertyKind.Int";
                return true;
            case SpecialType.System_Boolean:
                kindLiteral = "global::Property.PropertyKind.Bool";
                return true;
        }

        string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (typeName)
        {
            case "global::System.Numerics.Vector2":
                kindLiteral = "global::Property.PropertyKind.Vec2";
                return true;
            case "global::System.Numerics.Vector3":
                kindLiteral = "global::Property.PropertyKind.Vec3";
                return true;
            case "global::System.Numerics.Vector4":
                kindLiteral = "global::Property.PropertyKind.Vec4";
                return true;
            case "global::Core.Color32":
                kindLiteral = "global::Property.PropertyKind.Color32";
                return true;
            case "global::Core.StringHandle":
                kindLiteral = "global::Property.PropertyKind.StringHandle";
                return true;
            case "global::FixedMath.Fixed64":
                kindLiteral = "global::Property.PropertyKind.Fixed64";
                return true;
            case "global::FixedMath.Fixed64Vec2":
                kindLiteral = "global::Property.PropertyKind.Fixed64Vec2";
                return true;
            case "global::FixedMath.Fixed64Vec3":
                kindLiteral = "global::Property.PropertyKind.Fixed64Vec3";
                return true;
            default:
                kindLiteral = string.Empty;
                return false;
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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

    private struct PropertyAttr
    {
        public string Name;
        public string Group;
        public int Order;
        public float Min;
        public float Max;
        public float Step;
        public string FlagsLiteral;
        public string KindLiteral;

        public static PropertyAttr CreateDefault()
        {
            return new PropertyAttr
            {
                Name = string.Empty,
                Group = string.Empty,
                Order = 0,
                Min = float.NaN,
                Max = float.NaN,
                Step = float.NaN,
                FlagsLiteral = "global::Property.PropertyFlags.None",
                KindLiteral = PropertyKindFqn + ".Auto"
            };
        }
    }

    private readonly struct PropertyKindInfo
    {
        public readonly string KindLiteral;

        public PropertyKindInfo(string kindLiteral)
        {
            KindLiteral = kindLiteral;
        }

        public bool IsValid => KindLiteral.Length != 0;
    }
}
