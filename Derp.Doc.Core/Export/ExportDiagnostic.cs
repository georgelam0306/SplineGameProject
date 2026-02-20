namespace Derp.Doc.Export;

public readonly record struct ExportDiagnostic(
    ExportDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? TableId = null,
    string? ColumnId = null);

