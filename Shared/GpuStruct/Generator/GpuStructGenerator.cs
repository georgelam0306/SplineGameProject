using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace GpuStruct.Generator;

/// <summary>
/// Source generator for [GpuStruct] attribute.
/// Generates std430-aligned struct layouts using explicit field offsets.
/// </summary>
[Generator]
public sealed class GpuStructGenerator : IIncrementalGenerator
{
    private const string GpuStructAttributeName = "GpuStruct.GpuStructAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var structs = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: GpuStructAttributeName,
            predicate: static (n, _) => n is StructDeclarationSyntax,
            transform: static (ctx, _) => Transform(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(structs, static (spc, modelObj) =>
        {
            var model = (GpuStructModel)modelObj!;

            // Report diagnostics
            foreach (var diagnostic in model.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }

            if (model.HasErrors)
            {
                return;
            }

            try
            {
                var source = GenerateSource(model);
                var hint = model.FullName
                    .Replace("global::", "")
                    .Replace(".", "_") + ".GpuStruct.g.cs";
                spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.GeneratorError,
                    Location.None,
                    ex.ToString()));
            }
        });
    }

    private static GpuStructModel? Transform(GeneratorAttributeSyntaxContext ctx)
    {
        var structDecl = (StructDeclarationSyntax)ctx.TargetNode;
        var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

        var diagnostics = new List<Diagnostic>();

        // Check for partial keyword
        if (!structDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.StructMustBePartial,
                structDecl.Identifier.GetLocation(),
                typeSymbol.Name));
        }

        // Find partial properties
        var properties = new List<(string Name, string TypeName, ITypeSymbol Type, Location Location)>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IPropertySymbol property && property.IsPartialDefinition)
            {
                var typeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // Check for unsupported types
                if (Std430LayoutCalculator.IsUnsupportedType(typeName) ||
                    Std430LayoutCalculator.IsUnsupportedType(property.Type.Name))
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.UnsupportedType,
                        property.Locations.FirstOrDefault() ?? Location.None,
                        property.Name,
                        typeName));
                    continue;
                }

                // Check if type is known
                var layout = Std430LayoutCalculator.GetTypeLayout(typeName);
                if (layout == null)
                {
                    // Try with just the type name
                    layout = Std430LayoutCalculator.GetTypeLayout(property.Type.Name);
                }

                if (layout == null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.UnknownType,
                        property.Locations.FirstOrDefault() ?? Location.None,
                        property.Name,
                        typeName));
                    continue;
                }

                properties.Add((property.Name, typeName, property.Type, property.Locations.FirstOrDefault() ?? Location.None));
            }
        }

        if (properties.Count == 0 && diagnostics.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.NoPartialProperties,
                structDecl.Identifier.GetLocation(),
                typeSymbol.Name));
        }

        // Calculate layout
        var fieldInputs = properties.Select(p => (p.Name, p.TypeName)).ToList();
        var (fieldLayouts, totalSize, maxAlignment) = Std430LayoutCalculator.CalculateLayout(fieldInputs);

        var model = new GpuStructModel(
            typeSymbol.Name,
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            typeSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            fieldLayouts,
            totalSize,
            maxAlignment,
            diagnostics);

        return model;
    }

    private static string GenerateSource(GpuStructModel model)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        // Struct with explicit layout
        sb.AppendLine($"[StructLayout(LayoutKind.Explicit, Size = {model.TotalSize})]");
        sb.AppendLine($"partial struct {model.Name}");
        sb.AppendLine("{");

        // Generate backing fields with FieldOffset
        foreach (var field in model.Fields)
        {
            if (field.IsUnknown) continue;

            var backingFieldName = $"__{field.FieldName}";
            sb.AppendLine($"    [FieldOffset({field.Offset})]");
            sb.AppendLine($"    private {field.TypeName} {backingFieldName};");
            sb.AppendLine();
        }

        // Generate property implementations
        foreach (var field in model.Fields)
        {
            if (field.IsUnknown) continue;

            var backingFieldName = $"__{field.FieldName}";
            sb.AppendLine($"    public partial {field.TypeName} {field.FieldName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        get => {backingFieldName};");
            sb.AppendLine($"        set => {backingFieldName} = value;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Generate size constant
        sb.AppendLine($"    /// <summary>Size of this struct in bytes (std430 layout).</summary>");
        sb.AppendLine($"    public const int SizeInBytes = {model.TotalSize};");
        sb.AppendLine();

        // Generate alignment constant
        sb.AppendLine($"    /// <summary>Alignment requirement in bytes (std430 layout).</summary>");
        sb.AppendLine($"    public const int Alignment = {model.MaxAlignment};");

        sb.AppendLine("}");

        return sb.ToString();
    }
}

/// <summary>
/// Model for a GpuStruct.
/// </summary>
internal sealed class GpuStructModel
{
    public string Name { get; }
    public string FullName { get; }
    public string Namespace { get; }
    public IReadOnlyList<FieldLayout> Fields { get; }
    public int TotalSize { get; }
    public int MaxAlignment { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public GpuStructModel(
        string name,
        string fullName,
        string ns,
        IReadOnlyList<FieldLayout> fields,
        int totalSize,
        int maxAlignment,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        Name = name;
        FullName = fullName;
        Namespace = ns;
        Fields = fields;
        TotalSize = totalSize;
        MaxAlignment = maxAlignment;
        Diagnostics = diagnostics;
    }
}
