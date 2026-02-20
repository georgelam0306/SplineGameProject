using Derp.Doc.Model;

namespace Derp.Doc.Export;

internal sealed class ExportEnumModel
{
    private readonly List<string> _options;

    public ExportEnumModel(string enumName, DocTable table, DocColumn column, List<string> options, bool isKey)
    {
        EnumName = enumName;
        TableId = table.Id;
        ColumnId = column.Id;
        ColumnName = column.Name;
        _options = options;
        IsKey = isKey;

        int count = options.Count + (IsKey ? 0 : 1);
        StorageSizeBytes = count <= byte.MaxValue ? 1 : 2;
        UnderlyingTypeName = StorageSizeBytes == 1 ? "byte" : "ushort";
        ValueOffset = IsKey ? 0 : 1;
    }

    public string EnumName { get; }
    public string UnderlyingTypeName { get; }
    public int StorageSizeBytes { get; }
    public int ValueOffset { get; }
    public bool IsKey { get; }

    public string TableId { get; }
    public string ColumnId { get; }
    public string ColumnName { get; }

    public IReadOnlyList<string> Options => _options;

    public int GetValue(string raw, List<ExportDiagnostic> diagnostics, string tableId, string columnId)
    {
        if (string.IsNullOrEmpty(raw))
        {
            if (IsKey)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/enum/missing-key",
                    $"Key Select value is required for tableId '{tableId}', columnId '{columnId}'.",
                    tableId,
                    columnId));
                return 0;
            }

            return 0;
        }

        for (int i = 0; i < _options.Count; i++)
        {
            if (string.Equals(_options[i], raw, StringComparison.Ordinal))
            {
                return i + ValueOffset;
            }
        }

        diagnostics.Add(new ExportDiagnostic(
            ExportDiagnosticSeverity.Error,
            "export/enum/invalid-value",
            $"Invalid Select value '{raw}' for tableId '{tableId}', columnId '{columnId}'.",
            tableId,
            columnId));
        return 0;
    }
}

