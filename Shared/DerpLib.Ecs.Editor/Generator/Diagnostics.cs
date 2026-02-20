using Microsoft.CodeAnalysis;

namespace DerpLib.Ecs.Editor.Generator;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor GeneratorError = new(
        id: "DERPECS_EDITOR0000",
        title: "Derp.Ecs.Editor generator error",
        messageFormat: "{0}",
        category: "Derp.Ecs.Editor",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        id: "DERPECS_EDITOR0001",
        title: "Unsupported property field type",
        messageFormat: "Field '{0}' has unsupported property type '{1}'",
        category: "Derp.Ecs.Editor",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedEditorResizableFieldType = new(
        id: "DERPECS_EDITOR0010",
        title: "Unsupported [EditorResizable] field type",
        messageFormat: "Field '{0}' marked [EditorResizable] has unsupported type '{1}'",
        category: "Derp.Ecs.Editor",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
