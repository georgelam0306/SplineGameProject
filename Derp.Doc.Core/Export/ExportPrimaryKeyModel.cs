using Derp.Doc.Model;

namespace Derp.Doc.Export;

internal sealed class ExportPrimaryKeyModel
{
    private readonly Dictionary<string, int>? _mappedKeyByRowId;

    private ExportPrimaryKeyModel(
        DocColumn column,
        ExportFieldKind kind,
        ExportEnumModel? enumModel,
        Dictionary<string, int>? mappedKeyByRowId)
    {
        Column = column;
        Kind = kind;
        EnumModel = enumModel;
        _mappedKeyByRowId = mappedKeyByRowId;
    }

    public DocColumn Column { get; }
    public ExportFieldKind Kind { get; }
    public ExportEnumModel? EnumModel { get; }

    public static ExportPrimaryKeyModel? TryCreate(ExportTableModel tableModel, DocColumn pkColumn, List<ExportDiagnostic> diagnostics)
    {
        if (pkColumn.ExportIgnore)
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/keys/pk-ignored",
                $"Primary key column '{tableModel.Table.Name}.{pkColumn.Name}' cannot be ExportIgnore.",
                TableId: tableModel.Table.Id,
                ColumnId: pkColumn.Id));
            return null;
        }

        if (pkColumn.Kind == DocColumnKind.Number)
        {
            // PK defaults to int if not specified.
            var mapping = string.IsNullOrWhiteSpace(pkColumn.ExportType) ? "int" : pkColumn.ExportType!;
            if (!string.Equals(mapping, "int", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/keys/pk-type",
                    $"Primary key column '{tableModel.Table.Name}.{pkColumn.Name}' must be exported as int.",
                    TableId: tableModel.Table.Id,
                    ColumnId: pkColumn.Id));
                return null;
            }

            return new ExportPrimaryKeyModel(pkColumn, ExportFieldKind.Int32, enumModel: null, mappedKeyByRowId: null);
        }

        if (pkColumn.Kind == DocColumnKind.Select)
        {
            var options = pkColumn.Options ?? new List<string>();
            string enumName = !string.IsNullOrWhiteSpace(pkColumn.ExportEnumName)
                ? CSharpIdentifier.Sanitize(pkColumn.ExportEnumName, "Enum")
                : CSharpIdentifier.Sanitize(tableModel.StructName + CSharpIdentifier.ToPascalCase(pkColumn.Name), "Enum");
            var enumModel = new ExportEnumModel(enumName, tableModel.Table, pkColumn, options, isKey: true);
            return new ExportPrimaryKeyModel(pkColumn, ExportFieldKind.Enum, enumModel, mappedKeyByRowId: null);
        }

        if (pkColumn.Kind == DocColumnKind.Id)
        {
            if (!TryBuildStableUuidKeyMap(tableModel, pkColumn, diagnostics, out var mappedKeyByRowId))
            {
                return null;
            }

            return new ExportPrimaryKeyModel(pkColumn, ExportFieldKind.Int32, enumModel: null, mappedKeyByRowId);
        }

        diagnostics.Add(new ExportDiagnostic(
            ExportDiagnosticSeverity.Error,
            "export/keys/pk-kind",
            $"Primary key column kind '{pkColumn.Kind}' is not supported in V1. Use Number (int), Select, or Id.",
            TableId: tableModel.Table.Id,
            ColumnId: pkColumn.Id));
        return null;
    }

    public int GetRowKey(DocRow row, List<ExportDiagnostic> diagnostics, string tableId)
    {
        if (_mappedKeyByRowId != null)
        {
            if (_mappedKeyByRowId.TryGetValue(row.Id, out int mappedKey))
            {
                return mappedKey;
            }

            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/keys/pk-id-mapping-missing",
                $"Primary key mapping is missing for row '{row.Id}' in tableId '{tableId}'.",
                tableId,
                Column.Id));
            return -1;
        }

        var cell = row.GetCell(Column);
        if (Kind == ExportFieldKind.Int32)
        {
            if (!DocExportPipeline.TryConvertNumberToInt(cell.NumberValue, out int v))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/keys/pk-int",
                    $"Primary key must be an integer for tableId '{tableId}'.",
                    tableId,
                    Column.Id));
                return -1;
            }

            return v;
        }

        if (Kind == ExportFieldKind.Enum && EnumModel != null)
        {
            return EnumModel.GetValue(cell.StringValue ?? "", diagnostics, tableId, Column.Id);
        }

        return -1;
    }

    private static bool TryBuildStableUuidKeyMap(
        ExportTableModel tableModel,
        DocColumn pkColumn,
        List<ExportDiagnostic> diagnostics,
        out Dictionary<string, int> keyByRowId)
    {
        var table = tableModel.Table;
        var entries = new List<UuidKeyEntry>(table.Rows.Count);
        bool hasErrors = false;

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow row = table.Rows[rowIndex];
            string raw = row.GetCell(pkColumn).StringValue ?? "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/keys/pk-id-empty",
                    $"Primary key Id must be non-empty in table '{table.Name}' row '{row.Id}'.",
                    TableId: table.Id,
                    ColumnId: pkColumn.Id));
                hasErrors = true;
                continue;
            }

            if (!Guid.TryParse(raw, out Guid uuid))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/keys/pk-id-invalid",
                    $"Primary key Id value '{raw}' is not a valid UUID in table '{table.Name}' row '{row.Id}'.",
                    TableId: table.Id,
                    ColumnId: pkColumn.Id));
                hasErrors = true;
                continue;
            }

            entries.Add(new UuidKeyEntry(row.Id, uuid.ToString("N")));
        }

        entries.Sort(static (left, right) =>
        {
            int idComparison = string.Compare(left.CanonicalUuid, right.CanonicalUuid, StringComparison.Ordinal);
            if (idComparison != 0)
            {
                return idComparison;
            }

            return string.Compare(left.RowId, right.RowId, StringComparison.Ordinal);
        });

        keyByRowId = new Dictionary<string, int>(table.Rows.Count, StringComparer.Ordinal);
        string previousCanonicalUuid = "";
        string previousRowId = "";
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            UuidKeyEntry entry = entries[entryIndex];
            if (entryIndex > 0 &&
                string.Equals(previousCanonicalUuid, entry.CanonicalUuid, StringComparison.Ordinal))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/key/pk-duplicate",
                    $"Duplicate primary key UUID '{entry.CanonicalUuid}' in table '{table.Name}' (rows '{previousRowId}' and '{entry.RowId}').",
                    TableId: table.Id,
                    ColumnId: pkColumn.Id));
                hasErrors = true;
                continue;
            }

            keyByRowId[entry.RowId] = entryIndex;
            previousCanonicalUuid = entry.CanonicalUuid;
            previousRowId = entry.RowId;
        }

        if (hasErrors)
        {
            keyByRowId.Clear();
            return false;
        }

        return true;
    }

    private readonly struct UuidKeyEntry
    {
        public UuidKeyEntry(string rowId, string canonicalUuid)
        {
            RowId = rowId;
            CanonicalUuid = canonicalUuid;
        }

        public string RowId { get; }

        public string CanonicalUuid { get; }
    }
}
