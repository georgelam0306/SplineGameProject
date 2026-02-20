using Derp.Doc.Model;

namespace Derp.Doc.Export;

internal sealed class ExportSecondaryKeyModel
{
    private ExportSecondaryKeyModel(DocColumn column, bool unique, ExportEnumModel? enumModel, string binaryIndexName)
    {
        Column = column;
        Unique = unique;
        EnumModel = enumModel;
        BinaryIndexName = binaryIndexName;
    }

    public DocColumn Column { get; }
    public bool Unique { get; }
    public ExportEnumModel? EnumModel { get; }
    public string BinaryIndexName { get; }

    public static ExportSecondaryKeyModel? TryCreate(
        ExportTableModel tableModel,
        DocColumn column,
        bool unique,
        List<ExportDiagnostic> diagnostics)
    {
        if (column.ExportIgnore)
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/keys/secondary-ignored",
                $"Secondary key column '{tableModel.Table.Name}.{column.Name}' cannot be ExportIgnore.",
                TableId: tableModel.Table.Id,
                ColumnId: column.Id));
            return null;
        }

        string fieldName = CSharpIdentifier.Sanitize(CSharpIdentifier.ToPascalCase(column.Name), "Key");
        string indexName = tableModel.BinaryTableName + "__sk_" + fieldName + (unique ? "__unique" : "__pairs");

        if (column.Kind == DocColumnKind.Number)
        {
            var mapping = string.IsNullOrWhiteSpace(column.ExportType) ? "int" : column.ExportType!;
            if (!string.Equals(mapping, "int", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/keys/secondary-type",
                    $"Secondary key '{tableModel.Table.Name}.{column.Name}' must be exported as int.",
                    TableId: tableModel.Table.Id,
                    ColumnId: column.Id));
                return null;
            }

            return new ExportSecondaryKeyModel(column, unique, null, indexName);
        }

        if (column.Kind == DocColumnKind.Select)
        {
            var options = column.Options ?? new List<string>();
            string enumName = !string.IsNullOrWhiteSpace(column.ExportEnumName)
                ? CSharpIdentifier.Sanitize(column.ExportEnumName, "Enum")
                : CSharpIdentifier.Sanitize(tableModel.StructName + CSharpIdentifier.ToPascalCase(column.Name), "Enum");
            var enumModel = new ExportEnumModel(enumName, tableModel.Table, column, options, isKey: true);
            return new ExportSecondaryKeyModel(column, unique, enumModel, indexName);
        }

        diagnostics.Add(new ExportDiagnostic(
            ExportDiagnosticSeverity.Error,
            "export/keys/secondary-kind",
            $"Secondary key kind '{column.Kind}' is not supported. Use Number (int) or Select.",
            TableId: tableModel.Table.Id,
            ColumnId: column.Id));
        return null;
    }

    public int GetRowKey(DocRow row, List<ExportDiagnostic> diagnostics, string tableId)
    {
        var cell = row.GetCell(Column);

        if (Column.Kind == DocColumnKind.Number)
        {
            if (!DocExportPipeline.TryConvertNumberToInt(cell.NumberValue, out int v))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/keys/secondary-int",
                    $"Secondary key must be an integer for tableId '{tableId}'.",
                    tableId,
                    Column.Id));
                return 0;
            }

            return v;
        }

        if (Column.Kind == DocColumnKind.Select && EnumModel != null)
        {
            return EnumModel.GetValue(cell.StringValue ?? "", diagnostics, tableId, Column.Id);
        }

        return 0;
    }
}

