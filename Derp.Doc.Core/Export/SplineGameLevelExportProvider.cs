using System.Buffers.Binary;
using System.Text.Json;
using Core;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using DerpDoc.Runtime;

namespace Derp.Doc.Export;

internal sealed class SplineGameLevelExportProvider : IColumnExportProvider
{
    public string ColumnTypeId => SplineGameLevelIds.ColumnTypeId;

    public bool TryCreateExportColumnModel(
        DocTable table,
        string structName,
        DocColumn column,
        string fieldName,
        HashSet<string> keyColumns,
        List<ExportDiagnostic> diagnostics,
        out ExportColumnModel exportColumnModel)
    {
        exportColumnModel = new ExportColumnModel(
            column,
            fieldName,
            "StringHandle",
            ExportFieldKind.StringHandle,
            4,
            null);
        return true;
    }

    public bool TryWriteField(
        ExportTableModel tableModel,
        ExportColumnModel columnModel,
        DocRow row,
        DocCellValue cell,
        Dictionary<string, Dictionary<string, int>> primaryKeyValueByTableId,
        Dictionary<string, uint> stringIdByValue,
        byte[] recordBytes,
        ref int offset,
        List<ExportDiagnostic> diagnostics)
    {
        string payloadJson = BuildPayloadJson(tableModel.Project, columnModel.SourceColumn, row.Id);
        uint stringId = 0;
        if (!string.IsNullOrEmpty(payloadJson))
        {
            if (!TryGetOrAddStringId(
                    payloadJson,
                    stringIdByValue,
                    diagnostics,
                    tableModel.Table.Id,
                    columnModel.SourceColumn.Id,
                    out stringId))
            {
                stringId = 0;
            }
        }

        BinaryPrimitives.WriteUInt32LittleEndian(recordBytes.AsSpan(offset, 4), stringId);
        offset += 4;
        return true;
    }

    private static string BuildPayloadJson(DocProject project, DocColumn sourceColumn, string parentRowId)
    {
        if (string.IsNullOrWhiteSpace(sourceColumn.SubtableId))
        {
            return "";
        }

        DocTable? levelTable = FindTableById(project, sourceColumn.SubtableId);
        if (levelTable == null ||
            !TryResolveLevelSchema(levelTable, out LevelSchema levelSchema) ||
            !TryResolvePointsTable(project, levelSchema, out DocTable pointsTable) ||
            !TryResolveEntitiesTable(project, levelSchema, out DocTable entitiesTable) ||
            !TryResolvePointsSchema(pointsTable, out PointsSchema pointsSchema) ||
            !TryResolveEntitiesSchema(entitiesTable, out EntitiesSchema entitiesSchema) ||
            !TryFindLevelRow(levelTable, levelSchema, parentRowId, out DocRow levelRow))
        {
            return "";
        }

        string levelRowId = levelRow.Id;
        var payloadPoints = new List<SplinePointPayload>(pointsTable.Rows.Count);
        for (int rowIndex = 0; rowIndex < pointsTable.Rows.Count; rowIndex++)
        {
            DocRow pointRow = pointsTable.Rows[rowIndex];
            if (pointsSchema.ParentRowColumn != null &&
                !string.Equals(pointRow.GetCell(pointsSchema.ParentRowColumn).StringValue ?? "", levelRowId, StringComparison.Ordinal))
            {
                continue;
            }

            DocCellValue positionCell = pointRow.GetCell(pointsSchema.PositionColumn);
            DocCellValue tangentInCell = pointRow.GetCell(pointsSchema.TangentInColumn);
            DocCellValue tangentOutCell = pointRow.GetCell(pointsSchema.TangentOutColumn);
            payloadPoints.Add(new SplinePointPayload
            {
                RowId = pointRow.Id,
                Order = pointRow.GetCell(pointsSchema.OrderColumn).NumberValue,
                PositionX = positionCell.XValue,
                PositionY = positionCell.YValue,
                TangentInX = tangentInCell.XValue,
                TangentInY = tangentInCell.YValue,
                TangentOutX = tangentOutCell.XValue,
                TangentOutY = tangentOutCell.YValue,
            });
        }

        payloadPoints.Sort(static (left, right) =>
        {
            int orderComparison = left.Order.CompareTo(right.Order);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            return string.Compare(left.RowId, right.RowId, StringComparison.Ordinal);
        });

        var payloadEntities = new List<PlacedEntityPayload>(entitiesTable.Rows.Count);
        for (int rowIndex = 0; rowIndex < entitiesTable.Rows.Count; rowIndex++)
        {
            DocRow entityRow = entitiesTable.Rows[rowIndex];
            if (entitiesSchema.ParentRowColumn != null &&
                !string.Equals(entityRow.GetCell(entitiesSchema.ParentRowColumn).StringValue ?? "", levelRowId, StringComparison.Ordinal))
            {
                continue;
            }

            DocCellValue positionCell = entityRow.GetCell(entitiesSchema.PositionColumn);
            payloadEntities.Add(new PlacedEntityPayload
            {
                RowId = entityRow.Id,
                Order = entityRow.GetCell(entitiesSchema.OrderColumn).NumberValue,
                ParamT = entityRow.GetCell(entitiesSchema.ParamTColumn).NumberValue,
                PositionX = positionCell.XValue,
                PositionY = positionCell.YValue,
                EntityTableId = entityRow.GetCell(entitiesSchema.EntityTableColumn).StringValue ?? "",
                EntityRowId = entityRow.GetCell(entitiesSchema.EntityRowIdColumn).StringValue ?? "",
                DataJson = entityRow.GetCell(entitiesSchema.DataJsonColumn).StringValue ?? "",
            });
        }

        payloadEntities.Sort(static (left, right) =>
        {
            int orderComparison = left.Order.CompareTo(right.Order);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            int paramTComparison = left.ParamT.CompareTo(right.ParamT);
            if (paramTComparison != 0)
            {
                return paramTComparison;
            }

            return string.Compare(left.RowId, right.RowId, StringComparison.Ordinal);
        });

        if (payloadPoints.Count <= 0 && payloadEntities.Count <= 0)
        {
            return "";
        }

        string entityToolsTableId = levelSchema.EntityToolsTableColumn != null
            ? levelRow.GetCell(levelSchema.EntityToolsTableColumn).StringValue ?? ""
            : "";
        var payload = new SplineGameLevelPayload
        {
            Version = 2,
            LevelRowId = levelRow.Id,
            EntityToolsTableId = entityToolsTableId,
            Points = payloadPoints,
            Entities = payloadEntities,
        };
        return JsonSerializer.Serialize(payload);
    }

    private static bool TryGetOrAddStringId(
        string value,
        Dictionary<string, uint> stringIdByValue,
        List<ExportDiagnostic> diagnostics,
        string tableId,
        string columnId,
        out uint stringId)
    {
        if (stringIdByValue.TryGetValue(value, out stringId))
        {
            return true;
        }

        stringId = StringRegistry.ComputeStableId(value);
        foreach (KeyValuePair<string, uint> pair in stringIdByValue)
        {
            if (pair.Value == stringId &&
                !string.Equals(pair.Key, value, StringComparison.Ordinal))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/strings/id-collision",
                    $"String ID collision for SplineGame payload values '{pair.Key}' and '{value}'.",
                    TableId: tableId,
                    ColumnId: columnId));
                stringId = 0;
                return false;
            }
        }

        stringIdByValue[value] = stringId;
        return true;
    }

    private static DocTable? FindTableById(DocProject project, string tableId)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (string.Equals(table.Id, tableId, StringComparison.Ordinal))
            {
                return table;
            }
        }

        return null;
    }

    private static DocColumn? FindColumnById(DocTable table, string columnId)
    {
        if (string.IsNullOrWhiteSpace(columnId))
        {
            return null;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (string.Equals(column.Id, columnId, StringComparison.Ordinal))
            {
                return column;
            }
        }

        return null;
    }

    private static bool TryResolveLevelSchema(DocTable table, out LevelSchema schema)
    {
        DocColumn? parentRowColumn = FindColumnById(table, table.ParentRowColumnId ?? SplineGameLevelIds.ParentRowIdColumnId);
        DocColumn? pointsSubtableColumn = FindColumnById(table, SplineGameLevelIds.PointsSubtableColumnId);
        DocColumn? entitiesSubtableColumn = FindColumnById(table, SplineGameLevelIds.EntitiesSubtableColumnId);
        DocColumn? entityToolsTableColumn = FindColumnById(table, SplineGameLevelIds.EntityToolsTableColumnId);
        if (pointsSubtableColumn == null || entitiesSubtableColumn == null)
        {
            schema = default;
            return false;
        }

        schema = new LevelSchema(
            parentRowColumn,
            pointsSubtableColumn,
            entitiesSubtableColumn,
            entityToolsTableColumn);
        return true;
    }

    private static bool TryResolvePointsSchema(DocTable table, out PointsSchema schema)
    {
        DocColumn? parentRowColumn = FindColumnById(table, table.ParentRowColumnId ?? SplineGameLevelIds.PointsParentRowIdColumnId);
        DocColumn? orderColumn = FindColumnById(table, SplineGameLevelIds.PointsOrderColumnId);
        DocColumn? positionColumn = FindColumnById(table, SplineGameLevelIds.PointsPositionColumnId);
        DocColumn? tangentInColumn = FindColumnById(table, SplineGameLevelIds.PointsTangentInColumnId);
        DocColumn? tangentOutColumn = FindColumnById(table, SplineGameLevelIds.PointsTangentOutColumnId);
        if (orderColumn == null ||
            positionColumn == null ||
            tangentInColumn == null ||
            tangentOutColumn == null)
        {
            schema = default;
            return false;
        }

        schema = new PointsSchema(
            parentRowColumn,
            orderColumn,
            positionColumn,
            tangentInColumn,
            tangentOutColumn);
        return true;
    }

    private static bool TryResolveEntitiesSchema(DocTable table, out EntitiesSchema schema)
    {
        DocColumn? parentRowColumn = FindColumnById(table, table.ParentRowColumnId ?? SplineGameLevelIds.EntitiesParentRowIdColumnId);
        DocColumn? orderColumn = FindColumnById(table, SplineGameLevelIds.EntitiesOrderColumnId);
        DocColumn? paramTColumn = FindColumnById(table, SplineGameLevelIds.EntitiesParamTColumnId);
        DocColumn? positionColumn = FindColumnById(table, SplineGameLevelIds.EntitiesPositionColumnId);
        DocColumn? entityTableColumn = FindColumnById(table, SplineGameLevelIds.EntitiesTableRefColumnId);
        DocColumn? entityRowIdColumn = FindColumnById(table, SplineGameLevelIds.EntitiesRowIdColumnId);
        DocColumn? dataJsonColumn = FindColumnById(table, SplineGameLevelIds.EntitiesDataJsonColumnId);
        if (orderColumn == null ||
            paramTColumn == null ||
            positionColumn == null ||
            entityTableColumn == null ||
            entityRowIdColumn == null ||
            dataJsonColumn == null)
        {
            schema = default;
            return false;
        }

        schema = new EntitiesSchema(
            parentRowColumn,
            orderColumn,
            paramTColumn,
            positionColumn,
            entityTableColumn,
            entityRowIdColumn,
            dataJsonColumn);
        return true;
    }

    private static bool TryResolvePointsTable(DocProject project, LevelSchema schema, out DocTable table)
    {
        table = null!;
        string pointsSubtableId = schema.PointsSubtableColumn.SubtableId ?? "";
        if (string.IsNullOrWhiteSpace(pointsSubtableId))
        {
            return false;
        }

        DocTable? resolvedTable = FindTableById(project, pointsSubtableId);
        if (resolvedTable == null)
        {
            return false;
        }

        table = resolvedTable;
        return true;
    }

    private static bool TryResolveEntitiesTable(DocProject project, LevelSchema schema, out DocTable table)
    {
        table = null!;
        string entitiesSubtableId = schema.EntitiesSubtableColumn.SubtableId ?? "";
        if (string.IsNullOrWhiteSpace(entitiesSubtableId))
        {
            return false;
        }

        DocTable? resolvedTable = FindTableById(project, entitiesSubtableId);
        if (resolvedTable == null)
        {
            return false;
        }

        table = resolvedTable;
        return true;
    }

    private static bool TryFindLevelRow(
        DocTable levelTable,
        LevelSchema schema,
        string parentRowId,
        out DocRow row)
    {
        for (int rowIndex = 0; rowIndex < levelTable.Rows.Count; rowIndex++)
        {
            DocRow candidate = levelTable.Rows[rowIndex];
            if (schema.ParentRowColumn != null &&
                !string.Equals(candidate.GetCell(schema.ParentRowColumn).StringValue ?? "", parentRowId, StringComparison.Ordinal))
            {
                continue;
            }

            row = candidate;
            return true;
        }

        row = null!;
        return false;
    }

    private sealed class SplineGameLevelPayload
    {
        public int Version { get; set; }
        public string LevelRowId { get; set; } = "";
        public string EntityToolsTableId { get; set; } = "";
        public List<SplinePointPayload> Points { get; set; } = new();
        public List<PlacedEntityPayload> Entities { get; set; } = new();
    }

    private sealed class SplinePointPayload
    {
        public string RowId { get; set; } = "";
        public double Order { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double TangentInX { get; set; }
        public double TangentInY { get; set; }
        public double TangentOutX { get; set; }
        public double TangentOutY { get; set; }
    }

    private sealed class PlacedEntityPayload
    {
        public string RowId { get; set; } = "";
        public double Order { get; set; }
        public double ParamT { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public string EntityTableId { get; set; } = "";
        public string EntityRowId { get; set; } = "";
        public string DataJson { get; set; } = "";
    }

    private readonly record struct LevelSchema(
        DocColumn? ParentRowColumn,
        DocColumn PointsSubtableColumn,
        DocColumn EntitiesSubtableColumn,
        DocColumn? EntityToolsTableColumn);

    private readonly record struct PointsSchema(
        DocColumn? ParentRowColumn,
        DocColumn OrderColumn,
        DocColumn PositionColumn,
        DocColumn TangentInColumn,
        DocColumn TangentOutColumn);

    private readonly record struct EntitiesSchema(
        DocColumn? ParentRowColumn,
        DocColumn OrderColumn,
        DocColumn ParamTColumn,
        DocColumn PositionColumn,
        DocColumn EntityTableColumn,
        DocColumn EntityRowIdColumn,
        DocColumn DataJsonColumn);
}
