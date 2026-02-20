using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DerpLib.Ecs.Editor.Generator;

[Generator]
public sealed class EcsEditorResizableVarHeapBakingGenerator : IIncrementalGenerator
{
    private const string EcsComponentInterfaceFqn = "global::DerpLib.Ecs.IEcsComponent";
    private const string EditorResizableAttributeFqn = "global::DerpLib.Ecs.Editor.EditorResizableAttribute";
    private const string ListHandleFqn = "global::DerpLib.Ecs.ListHandle<T>";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<string?> domain = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) =>
            {
                if (provider.GlobalOptions.TryGetValue("build_property.DerpEcsDomain", out string? value))
                {
                    return value;
                }

                return null;
            });

        IncrementalValuesProvider<INamedTypeSymbol> componentTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is StructDeclarationSyntax s && s.Modifiers.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, _) => TryGetComponentType(ctx))
            .Where(static t => t is not null)!
            .Select(static (t, _) => t!);

        context.RegisterSourceOutput(componentTypes.Collect().Combine(domain), static (spc, input) =>
        {
            try
            {
                (ImmutableArray<INamedTypeSymbol> components, string? domainValue) = input;
                if (!string.Equals(domainValue?.Trim(), "View", StringComparison.Ordinal))
                {
                    return;
                }

                EmitAll(spc, components);
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
        if (symbol == null || symbol.TypeKind != TypeKind.Struct)
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

    private static void EmitAll(SourceProductionContext spc, ImmutableArray<INamedTypeSymbol> components)
    {
        if (components.IsDefaultOrEmpty)
        {
            return;
        }

        var ordered = new List<INamedTypeSymbol>(components.Length);
        for (int i = 0; i < components.Length; i++)
        {
            INamedTypeSymbol t = components[i];
            if (!ContainsSymbol(ordered, t))
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

        for (int i = 0; i < ordered.Count; i++)
        {
            EmitComponent(spc, ordered[i]);
        }
    }

    private static bool ContainsSymbol(List<INamedTypeSymbol> list, INamedTypeSymbol symbol)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(list[i], symbol))
            {
                return true;
            }
        }
        return false;
    }

    private static void EmitComponent(SourceProductionContext spc, INamedTypeSymbol componentType)
    {
        var resizableFields = new List<(IFieldSymbol Field, ITypeSymbol ElementType)>();

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

            if (!HasEditorResizableAttribute(field))
            {
                continue;
            }

            if (!TryGetListHandleElementType(field.Type, out ITypeSymbol? elementType))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UnsupportedEditorResizableFieldType,
                    field.Locations.Length > 0 ? field.Locations[0] : Location.None,
                    field.Name,
                    field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return;
            }

            if (!elementType!.IsUnmanagedType)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UnsupportedEditorResizableFieldType,
                    field.Locations.Length > 0 ? field.Locations[0] : Location.None,
                    field.Name,
                    field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return;
            }

            resizableFields.Add((field, elementType));
        }

        if (resizableFields.Count == 0)
        {
            return;
        }

        string namespaceName = componentType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : componentType.ContainingNamespace.ToDisplayString();

        string typeName = componentType.Name;
        string accessibility = FormatAccessibility(componentType.DeclaredAccessibility);

        var sb = new StringBuilder(8_192);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using DerpLib.Ecs;");
        sb.AppendLine();

        if (namespaceName.Length != 0)
        {
            sb.Append("namespace ").Append(namespaceName).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(accessibility).Append(" partial struct ").Append(typeName).AppendLine();
        sb.AppendLine("{");

        sb.Append("    public bool TryBakeEditorResizableFields(");
        sb.Append("EcsVarHeapBuilder heapBuilder");
        for (int i = 0; i < resizableFields.Count; i++)
        {
            string elementTypeFqn = resizableFields[i].ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string parameterName = char.ToLowerInvariant(resizableFields[i].Field.Name[0]) + resizableFields[i].Field.Name.Substring(1) + "Editor";
            sb.Append(", List<").Append(elementTypeFqn).Append(">? ").Append(parameterName);
        }
        sb.AppendLine(")");
        sb.AppendLine("    {");
        sb.AppendLine("        if (heapBuilder == null)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new ArgumentNullException(nameof(heapBuilder));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        bool ok = true;");
        for (int i = 0; i < resizableFields.Count; i++)
        {
            string fieldName = resizableFields[i].Field.Name;
            string parameterName = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1) + "Editor";
            sb.Append("\t\tok &= TryBake").Append(fieldName).AppendLine("(heapBuilder, " + parameterName + ");");
        }
        sb.AppendLine("        return ok;");
        sb.AppendLine("    }");

        for (int i = 0; i < resizableFields.Count; i++)
        {
            string fieldName = resizableFields[i].Field.Name;
            string elementTypeFqn = resizableFields[i].ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string parameterName = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1) + "Editor";

            sb.AppendLine();
            sb.Append("    public bool TryBake").Append(fieldName).AppendLine("(EcsVarHeapBuilder heapBuilder, List<" + elementTypeFqn + ">? " + parameterName + ")");
            sb.AppendLine("    {");
            sb.Append("\t\tList<").Append(elementTypeFqn).Append(">? list = ").Append(parameterName).AppendLine(";");
            sb.AppendLine("\t\tif (list == null || list.Count == 0)");
            sb.AppendLine("\t\t{");
            sb.Append("\t\t\t").Append(fieldName).AppendLine(" = ListHandle<" + elementTypeFqn + ">.Invalid;");
            sb.AppendLine("\t\t\treturn true;");
            sb.AppendLine("\t\t}");
            sb.AppendLine();
            sb.AppendLine("\t\tReadOnlySpan<" + elementTypeFqn + "> span = CollectionsMarshal.AsSpan(list);");
            sb.AppendLine("\t\t" + fieldName + " = heapBuilder.Add(span);");
            sb.AppendLine("\t\treturn " + fieldName + ".IsValid;");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        spc.AddSource(typeName + ".EcsEditorResizableVarHeap.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static bool HasEditorResizableAttribute(IFieldSymbol field)
    {
        foreach (AttributeData attr in field.GetAttributes())
        {
            if (attr.AttributeClass == null)
            {
                continue;
            }

            if (attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EditorResizableAttributeFqn)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetListHandleElementType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;
        if (type is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
        {
            return false;
        }

        string listDefinition = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!string.Equals(listDefinition, ListHandleFqn, StringComparison.Ordinal))
        {
            return false;
        }

        elementType = named.TypeArguments[0];
        return true;
    }

    private static string FormatAccessibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => "internal"
        };
    }
}
