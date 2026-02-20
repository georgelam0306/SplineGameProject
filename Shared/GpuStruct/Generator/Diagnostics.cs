using Microsoft.CodeAnalysis;

namespace GpuStruct.Generator;

/// <summary>
/// Diagnostic descriptors for GpuStruct generator.
/// </summary>
internal static class Diagnostics
{
    private const string Category = "GpuStruct";

    public static readonly DiagnosticDescriptor StructMustBePartial = new(
        id: "GPUSTRUCT001",
        title: "GpuStruct must be partial",
        messageFormat: "Struct '{0}' must be declared as partial to use [GpuStruct]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "GPUSTRUCT002",
        title: "Unsupported type in GpuStruct",
        messageFormat: "Property '{0}' has unsupported type '{1}'. Use int instead of bool, or use a supported numeric/vector type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownType = new(
        id: "GPUSTRUCT003",
        title: "Unknown type in GpuStruct",
        messageFormat: "Property '{0}' has unknown type '{1}'. Only scalar types (float, int, uint) and System.Numerics vectors are supported.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoPartialProperties = new(
        id: "GPUSTRUCT004",
        title: "No partial properties in GpuStruct",
        messageFormat: "Struct '{0}' has no partial properties. Add partial properties for the GPU fields.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GeneratorError = new(
        id: "GPUSTRUCT999",
        title: "Generator error",
        messageFormat: "GpuStruct generator error: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
