using Derp.Doc.Model;
using Derp.Doc.Plugins;

namespace Derp.Doc.Export;

internal readonly struct ExportColumnModel
{
    public ExportColumnModel(
        DocColumn sourceColumn,
        string fieldName,
        string fieldTypeName,
        ExportFieldKind fieldKind,
        int fieldSizeBytes,
        ExportEnumModel? enumModel)
    {
        SourceColumn = sourceColumn;
        FieldName = fieldName;
        FieldTypeName = fieldTypeName;
        FieldKind = fieldKind;
        FieldSizeBytes = fieldSizeBytes;
        EnumModel = enumModel;
    }

    public DocColumn SourceColumn { get; }
    public string FieldName { get; }
    public string FieldTypeName { get; }
    public ExportFieldKind FieldKind { get; }
    public int FieldSizeBytes { get; }
    public ExportEnumModel? EnumModel { get; }

    public static ExportColumnModel? Create(
        DocTable table,
        string structName,
        DocColumn column,
        string fieldName,
        HashSet<string> keyColumns,
        List<ExportDiagnostic> diagnostics)
    {
        if (table.IsSubtable &&
            !string.IsNullOrWhiteSpace(table.ParentTableId) &&
            !string.IsNullOrWhiteSpace(table.ParentRowColumnId) &&
            string.Equals(column.Id, table.ParentRowColumnId, StringComparison.Ordinal))
        {
            return new ExportColumnModel(
                column,
                fieldName,
                "int",
                ExportFieldKind.SubtableParentForeignKeyInt32,
                4,
                null);
        }

        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            if (ColumnExportProviderRegistry.TryCreateExportColumnModel(
                    columnTypeId,
                    table,
                    structName,
                    column,
                    fieldName,
                    keyColumns,
                    diagnostics,
                    out var pluginColumnModel))
            {
                return pluginColumnModel;
            }

            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/type/missing-provider",
                $"No export provider is registered for column type id '{columnTypeId}' on '{table.Name}.{column.Name}'.",
                TableId: table.Id,
                ColumnId: column.Id));
            return null;
        }

        switch (column.Kind)
        {
            case DocColumnKind.Id:
            case DocColumnKind.Text:
            case DocColumnKind.TextureAsset:
            case DocColumnKind.MeshAsset:
            case DocColumnKind.AudioAsset:
            case DocColumnKind.UiAsset:
            case DocColumnKind.TableRef:
                return new ExportColumnModel(column, fieldName, "StringHandle", ExportFieldKind.StringHandle, 4, null);
            case DocColumnKind.Checkbox:
                return new ExportColumnModel(column, fieldName, "byte", ExportFieldKind.Byte, 1, null);
            case DocColumnKind.Number:
            case DocColumnKind.Formula:
            {
                var mapping = string.IsNullOrWhiteSpace(column.ExportType) ? "Fixed64" : column.ExportType!;
                if (string.Equals(mapping, "int", StringComparison.OrdinalIgnoreCase))
                {
                    return new ExportColumnModel(column, fieldName, "int", ExportFieldKind.Int32, 4, null);
                }
                if (string.Equals(mapping, "float", StringComparison.OrdinalIgnoreCase))
                {
                    return new ExportColumnModel(column, fieldName, "float", ExportFieldKind.Float32, 4, null);
                }
                if (string.Equals(mapping, "double", StringComparison.OrdinalIgnoreCase))
                {
                    return new ExportColumnModel(column, fieldName, "double", ExportFieldKind.Float64, 8, null);
                }
                if (string.Equals(mapping, "fixed32", StringComparison.OrdinalIgnoreCase))
                {
                    return new ExportColumnModel(column, fieldName, "Fixed32", ExportFieldKind.Fixed32, 4, null);
                }
                if (string.Equals(mapping, "fixed64", StringComparison.OrdinalIgnoreCase))
                {
                    return new ExportColumnModel(column, fieldName, "Fixed64", ExportFieldKind.Fixed64, 8, null);
                }

                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/type/invalid-number-mapping",
                    $"Invalid export type mapping '{mapping}' for '{table.Name}.{column.Name}'. Expected: int, float, double, Fixed32, Fixed64.",
                    TableId: table.Id,
                    ColumnId: column.Id));
                return null;
            }
            case DocColumnKind.Spline:
            {
                return new ExportColumnModel(column, fieldName, "SplineHandle", ExportFieldKind.SplineHandle, 4, null);
            }
            case DocColumnKind.Vec2:
            {
                if (string.Equals(columnTypeId, DocColumnTypeIds.Vec2Fixed32, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(columnTypeId, DocColumnTypeIds.Vec2Float32, StringComparison.OrdinalIgnoreCase))
                {
                    return new ExportColumnModel(column, fieldName, "Fixed32Vec2", ExportFieldKind.Fixed32Vec2, 8, null);
                }

                return new ExportColumnModel(column, fieldName, "Fixed64Vec2", ExportFieldKind.Fixed64Vec2, 16, null);
            }
            case DocColumnKind.Vec3:
            {
                if (string.Equals(columnTypeId, DocColumnTypeIds.Vec3Fixed32, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(columnTypeId, DocColumnTypeIds.Vec3Float32, StringComparison.OrdinalIgnoreCase))
                {
                    return new ExportColumnModel(column, fieldName, "Fixed32Vec3", ExportFieldKind.Fixed32Vec3, 12, null);
                }

                return new ExportColumnModel(column, fieldName, "Fixed64Vec3", ExportFieldKind.Fixed64Vec3, 24, null);
            }
            case DocColumnKind.Vec4:
            {
                if (string.Equals(columnTypeId, DocColumnTypeIds.Vec4Fixed32, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(columnTypeId, DocColumnTypeIds.Vec4Float32, StringComparison.OrdinalIgnoreCase))
                {
                    return new ExportColumnModel(column, fieldName, "Fixed32Vec4", ExportFieldKind.Fixed32Vec4, 16, null);
                }

                return new ExportColumnModel(column, fieldName, "Fixed64Vec4", ExportFieldKind.Fixed64Vec4, 32, null);
            }
            case DocColumnKind.Color:
            {
                if (DocColumnTypeIdMapper.IsHdrColorTypeId(columnTypeId))
                {
                    return new ExportColumnModel(column, fieldName, "Fixed64Vec4", ExportFieldKind.Fixed64Vec4, 32, null);
                }

                return new ExportColumnModel(column, fieldName, "Color32", ExportFieldKind.Color32, 4, null);
            }
            case DocColumnKind.Select:
            {
                var options = column.Options ?? new List<string>();
                bool isKey = keyColumns.Contains(column.Id);

                string enumName = !string.IsNullOrWhiteSpace(column.ExportEnumName)
                    ? CSharpIdentifier.Sanitize(column.ExportEnumName, "Enum")
                    : CSharpIdentifier.Sanitize(structName + CSharpIdentifier.ToPascalCase(column.Name), "Enum");

                var enumModel = new ExportEnumModel(enumName, table, column, options, isKey);
                return new ExportColumnModel(column, fieldName, enumName, ExportFieldKind.Enum, enumModel.StorageSizeBytes, enumModel);
            }
            case DocColumnKind.Relation:
            {
                // Foreign keys are exported as int (PK of target table).
                return new ExportColumnModel(column, fieldName, "int", ExportFieldKind.ForeignKeyInt32, 4, null);
            }
            default:
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/type/unsupported-column-kind",
                    $"Unsupported column kind '{column.Kind}' for export: '{table.Name}.{column.Name}'.",
                    TableId: table.Id,
                    ColumnId: column.Id));
                return null;
            }
        }
    }
}
