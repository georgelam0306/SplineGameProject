using Derp.Doc.Model;

namespace Derp.Doc.Tables;

internal static class SchemaLinkedTableSynchronizer
{
    private enum SyncVisitState
    {
        Unvisited = 0,
        Visiting = 1,
        Visited = 2,
    }

    public static void Synchronize(DocProject project)
    {
        var tableById = new Dictionary<string, DocTable>(project.Tables.Count, StringComparer.Ordinal);
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            tableById[project.Tables[tableIndex].Id] = project.Tables[tableIndex];
        }

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (table.IsSchemaLinked || table.IsInherited)
            {
                continue;
            }

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                table.Columns[columnIndex].IsInherited = false;
            }
        }

        var visitStateByTableId = new Dictionary<string, SyncVisitState>(project.Tables.Count, StringComparer.Ordinal);
        var activePathTableIds = new List<string>(project.Tables.Count);
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (!table.IsSchemaLinked && !table.IsInherited)
            {
                continue;
            }

            SynchronizeTable(table, tableById, visitStateByTableId, activePathTableIds);
        }
    }

    private static void SynchronizeTable(
        DocTable table,
        Dictionary<string, DocTable> tableById,
        Dictionary<string, SyncVisitState> visitStateByTableId,
        List<string> activePathTableIds)
    {
        if (!visitStateByTableId.TryGetValue(table.Id, out SyncVisitState visitState))
        {
            visitState = SyncVisitState.Unvisited;
        }

        if (visitState == SyncVisitState.Visited)
        {
            return;
        }

        if (visitState == SyncVisitState.Visiting)
        {
            string cyclePath = BuildDependencyCyclePath(activePathTableIds, tableById, table.Id);
            throw new InvalidOperationException("Table schema dependency cycle detected: " + cyclePath);
        }

        visitStateByTableId[table.Id] = SyncVisitState.Visiting;
        activePathTableIds.Add(table.Id);

        if (table.IsSchemaLinked && table.IsInherited)
        {
            throw new InvalidOperationException(
                "Table '" + table.Name + "' cannot be both schema-linked and inherited.");
        }

        bool isSchemaLinked = table.IsSchemaLinked;
        string sourceTableId = isSchemaLinked
            ? table.SchemaSourceTableId ?? ""
            : table.InheritanceSourceTableId ?? "";
        if (string.IsNullOrWhiteSpace(sourceTableId) || !tableById.TryGetValue(sourceTableId, out DocTable? sourceTable))
        {
            string dependencyKind = isSchemaLinked ? "schema-linked" : "inherited";
            throw new InvalidOperationException(
                dependencyKind + " table '" + table.Name + "' references missing source table id '" + sourceTableId + "'.");
        }

        if (sourceTable.IsSchemaLinked || sourceTable.IsInherited)
        {
            SynchronizeTable(sourceTable, tableById, visitStateByTableId, activePathTableIds);
        }

        if (isSchemaLinked)
        {
            CopySchemaFromSource(table, sourceTable);
        }
        else
        {
            CopyInheritanceSchemaFromSource(table, sourceTable);
        }

        activePathTableIds.RemoveAt(activePathTableIds.Count - 1);
        visitStateByTableId[table.Id] = SyncVisitState.Visited;
    }

    private static void CopySchemaFromSource(DocTable targetTable, DocTable sourceTable)
    {
        Dictionary<string, string> columnIdRemap = BuildColumnIdRemap(targetTable.Columns, sourceTable.Columns);
        if (columnIdRemap.Count > 0)
        {
            RemapTableCellDataToSourceSchema(targetTable, columnIdRemap);
        }

        var clonedColumns = new List<DocColumn>(sourceTable.Columns.Count);
        var validColumnIds = new HashSet<string>(sourceTable.Columns.Count, StringComparer.Ordinal);
        for (int columnIndex = 0; columnIndex < sourceTable.Columns.Count; columnIndex++)
        {
            DocColumn sourceColumn = sourceTable.Columns[columnIndex];
            clonedColumns.Add(CloneColumn(sourceColumn));
            validColumnIds.Add(sourceColumn.Id);
        }

        targetTable.Columns.Clear();
        targetTable.Columns.AddRange(clonedColumns);

        TrimTableCellDataToSchema(targetTable, validColumnIds);
        EnsureTableCellDataForSchema(targetTable, clonedColumns);
    }

    private static void CopyInheritanceSchemaFromSource(DocTable targetTable, DocTable sourceTable)
    {
        var inheritedColumnsBeforeSync = new List<DocColumn>(targetTable.Columns.Count);
        var localColumnsBeforeSync = new List<DocColumn>(targetTable.Columns.Count);
        for (int columnIndex = 0; columnIndex < targetTable.Columns.Count; columnIndex++)
        {
            DocColumn existingColumn = targetTable.Columns[columnIndex];
            if (existingColumn.IsInherited)
            {
                inheritedColumnsBeforeSync.Add(existingColumn);
            }
            else
            {
                localColumnsBeforeSync.Add(existingColumn);
            }
        }

        Dictionary<string, string> inheritedColumnIdRemap = BuildColumnIdRemap(inheritedColumnsBeforeSync, sourceTable.Columns);
        Dictionary<string, string> localCollisionRemap = BuildInheritanceCollisionRemap(
            targetTable,
            sourceTable,
            localColumnsBeforeSync,
            out HashSet<string> localCollisionColumnIds);
        Dictionary<string, string> combinedColumnIdRemap = MergeColumnIdRemaps(inheritedColumnIdRemap, localCollisionRemap);
        if (combinedColumnIdRemap.Count > 0)
        {
            RemapTableCellDataToSourceSchema(targetTable, combinedColumnIdRemap);
            RemapTableKeysToSourceSchema(targetTable.Keys, combinedColumnIdRemap);
        }

        if (localCollisionColumnIds.Count > 0)
        {
            for (int columnIndex = localColumnsBeforeSync.Count - 1; columnIndex >= 0; columnIndex--)
            {
                DocColumn localColumn = localColumnsBeforeSync[columnIndex];
                if (localCollisionColumnIds.Contains(localColumn.Id))
                {
                    localColumnsBeforeSync.RemoveAt(columnIndex);
                }
            }
        }

        int estimatedColumnCount = sourceTable.Columns.Count + localColumnsBeforeSync.Count;
        var mergedColumns = new List<DocColumn>(estimatedColumnCount);
        var validColumnIds = new HashSet<string>(estimatedColumnCount, StringComparer.Ordinal);

        for (int columnIndex = 0; columnIndex < sourceTable.Columns.Count; columnIndex++)
        {
            DocColumn inheritedSourceColumn = sourceTable.Columns[columnIndex];
            DocColumn inheritedClone = CloneColumn(inheritedSourceColumn);
            inheritedClone.IsInherited = true;
            mergedColumns.Add(inheritedClone);
            validColumnIds.Add(inheritedClone.Id);
        }

        for (int columnIndex = 0; columnIndex < localColumnsBeforeSync.Count; columnIndex++)
        {
            DocColumn localSourceColumn = localColumnsBeforeSync[columnIndex];
            DocColumn localClone = CloneColumn(localSourceColumn);
            localClone.IsInherited = false;
            mergedColumns.Add(localClone);
            validColumnIds.Add(localClone.Id);
        }

        targetTable.Columns.Clear();
        targetTable.Columns.AddRange(mergedColumns);

        TrimTableCellDataToSchema(targetTable, validColumnIds);
        EnsureTableCellDataForSchema(targetTable, mergedColumns);
        TrimTableKeysToSchema(targetTable, validColumnIds);
        InheritPrimaryKeyFromSource(targetTable, sourceTable, validColumnIds);
    }

    private static Dictionary<string, string> MergeColumnIdRemaps(
        Dictionary<string, string> firstRemap,
        Dictionary<string, string> secondRemap)
    {
        if (firstRemap.Count == 0 && secondRemap.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (secondRemap.Count == 0)
        {
            return new Dictionary<string, string>(firstRemap, StringComparer.Ordinal);
        }

        if (firstRemap.Count == 0)
        {
            return new Dictionary<string, string>(secondRemap, StringComparer.Ordinal);
        }

        var mergedRemap = new Dictionary<string, string>(firstRemap.Count + secondRemap.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> remapEntry in firstRemap)
        {
            mergedRemap[remapEntry.Key] = remapEntry.Value;
        }

        foreach (KeyValuePair<string, string> remapEntry in secondRemap)
        {
            if (mergedRemap.TryGetValue(remapEntry.Key, out string? existingTargetColumnId) &&
                !string.Equals(existingTargetColumnId, remapEntry.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Conflicting column id remap for column id '" + remapEntry.Key + "'.");
            }

            mergedRemap[remapEntry.Key] = remapEntry.Value;
        }

        return mergedRemap;
    }

    private static Dictionary<string, string> BuildInheritanceCollisionRemap(
        DocTable targetTable,
        DocTable sourceTable,
        IReadOnlyList<DocColumn> localColumns,
        out HashSet<string> collidedLocalColumnIds)
    {
        collidedLocalColumnIds = new HashSet<string>(StringComparer.Ordinal);
        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (localColumns.Count <= 0 || sourceTable.Columns.Count <= 0)
        {
            return remap;
        }

        var sourceColumnsById = new Dictionary<string, DocColumn>(sourceTable.Columns.Count, StringComparer.Ordinal);
        var sourceColumnsByName = new Dictionary<string, DocColumn>(sourceTable.Columns.Count, StringComparer.OrdinalIgnoreCase);
        for (int sourceColumnIndex = 0; sourceColumnIndex < sourceTable.Columns.Count; sourceColumnIndex++)
        {
            DocColumn sourceColumn = sourceTable.Columns[sourceColumnIndex];
            if (!string.IsNullOrWhiteSpace(sourceColumn.Id))
            {
                sourceColumnsById[sourceColumn.Id] = sourceColumn;
            }

            if (!string.IsNullOrWhiteSpace(sourceColumn.Name) &&
                !sourceColumnsByName.ContainsKey(sourceColumn.Name))
            {
                sourceColumnsByName[sourceColumn.Name] = sourceColumn;
            }
        }

        for (int localColumnIndex = 0; localColumnIndex < localColumns.Count; localColumnIndex++)
        {
            DocColumn localColumn = localColumns[localColumnIndex];
            if (string.IsNullOrWhiteSpace(localColumn.Id))
            {
                continue;
            }

            if (!TryFindInheritanceCollisionSourceColumn(localColumn, sourceColumnsById, sourceColumnsByName, out DocColumn? sourceColumn) ||
                sourceColumn == null)
            {
                continue;
            }

            if (!AreCompatibleForInheritanceMigration(localColumn, sourceColumn))
            {
                throw new InvalidOperationException(
                    "Table '" + targetTable.Name + "' column '" + localColumn.Name + "' collides with inherited column '" +
                    sourceColumn.Name + "' from source table '" + sourceTable.Name +
                    "' but types differ. Rename or remove the local column before enabling inheritance.");
            }

            collidedLocalColumnIds.Add(localColumn.Id);
            if (!string.Equals(localColumn.Id, sourceColumn.Id, StringComparison.Ordinal))
            {
                remap[localColumn.Id] = sourceColumn.Id;
            }
        }

        return remap;
    }

    private static bool TryFindInheritanceCollisionSourceColumn(
        DocColumn localColumn,
        Dictionary<string, DocColumn> sourceColumnsById,
        Dictionary<string, DocColumn> sourceColumnsByName,
        out DocColumn? sourceColumn)
    {
        sourceColumn = null;
        if (!string.IsNullOrWhiteSpace(localColumn.Id) &&
            sourceColumnsById.TryGetValue(localColumn.Id, out DocColumn? sourceColumnById))
        {
            sourceColumn = sourceColumnById;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(localColumn.Name) &&
            sourceColumnsByName.TryGetValue(localColumn.Name, out DocColumn? sourceColumnByName))
        {
            sourceColumn = sourceColumnByName;
            return true;
        }

        return false;
    }

    private static bool AreCompatibleForInheritanceMigration(DocColumn localColumn, DocColumn sourceColumn)
    {
        return localColumn.Kind == sourceColumn.Kind &&
               string.Equals(localColumn.ColumnTypeId, sourceColumn.ColumnTypeId, StringComparison.Ordinal);
    }

    private static void RemapTableKeysToSourceSchema(DocTableKeys keys, Dictionary<string, string> columnIdRemap)
    {
        if (columnIdRemap.Count <= 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(keys.PrimaryKeyColumnId) &&
            columnIdRemap.TryGetValue(keys.PrimaryKeyColumnId, out string? remappedPrimaryKeyColumnId))
        {
            keys.PrimaryKeyColumnId = remappedPrimaryKeyColumnId;
        }

        for (int secondaryKeyIndex = 0; secondaryKeyIndex < keys.SecondaryKeys.Count; secondaryKeyIndex++)
        {
            DocSecondaryKey secondaryKey = keys.SecondaryKeys[secondaryKeyIndex];
            if (!string.IsNullOrWhiteSpace(secondaryKey.ColumnId) &&
                columnIdRemap.TryGetValue(secondaryKey.ColumnId, out string? remappedSecondaryKeyColumnId))
            {
                secondaryKey.ColumnId = remappedSecondaryKeyColumnId;
                keys.SecondaryKeys[secondaryKeyIndex] = secondaryKey;
            }
        }
    }

    private static void TrimTableKeysToSchema(DocTable table, HashSet<string> validColumnIds)
    {
        if (!string.IsNullOrWhiteSpace(table.Keys.PrimaryKeyColumnId) &&
            !validColumnIds.Contains(table.Keys.PrimaryKeyColumnId))
        {
            table.Keys.PrimaryKeyColumnId = "";
        }

        var seenSecondaryKeyColumnIds = new HashSet<string>(StringComparer.Ordinal);
        for (int secondaryKeyIndex = table.Keys.SecondaryKeys.Count - 1; secondaryKeyIndex >= 0; secondaryKeyIndex--)
        {
            DocSecondaryKey secondaryKey = table.Keys.SecondaryKeys[secondaryKeyIndex];
            if (string.IsNullOrWhiteSpace(secondaryKey.ColumnId) ||
                !validColumnIds.Contains(secondaryKey.ColumnId) ||
                !seenSecondaryKeyColumnIds.Add(secondaryKey.ColumnId))
            {
                table.Keys.SecondaryKeys.RemoveAt(secondaryKeyIndex);
            }
        }
    }

    private static void InheritPrimaryKeyFromSource(
        DocTable targetTable,
        DocTable sourceTable,
        HashSet<string> validColumnIds)
    {
        if (string.IsNullOrWhiteSpace(sourceTable.Keys.PrimaryKeyColumnId))
        {
            return;
        }

        if (validColumnIds.Contains(sourceTable.Keys.PrimaryKeyColumnId))
        {
            targetTable.Keys.PrimaryKeyColumnId = sourceTable.Keys.PrimaryKeyColumnId;
        }
    }

    private static Dictionary<string, string> BuildColumnIdRemap(
        IReadOnlyList<DocColumn> existingColumns,
        IReadOnlyList<DocColumn> sourceColumns)
    {
        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (existingColumns.Count <= 0 || sourceColumns.Count <= 0)
        {
            return remap;
        }

        var existingById = new Dictionary<string, DocColumn>(existingColumns.Count, StringComparer.Ordinal);
        for (int existingColumnIndex = 0; existingColumnIndex < existingColumns.Count; existingColumnIndex++)
        {
            DocColumn existingColumn = existingColumns[existingColumnIndex];
            if (string.IsNullOrWhiteSpace(existingColumn.Id))
            {
                continue;
            }

            existingById[existingColumn.Id] = existingColumn;
        }

        var usedExistingColumnIds = new HashSet<string>(StringComparer.Ordinal);
        for (int sourceColumnIndex = 0; sourceColumnIndex < sourceColumns.Count; sourceColumnIndex++)
        {
            DocColumn sourceColumn = sourceColumns[sourceColumnIndex];
            if (string.IsNullOrWhiteSpace(sourceColumn.Id))
            {
                continue;
            }

            if (existingById.ContainsKey(sourceColumn.Id))
            {
                usedExistingColumnIds.Add(sourceColumn.Id);
            }
        }

        for (int sourceColumnIndex = 0; sourceColumnIndex < sourceColumns.Count; sourceColumnIndex++)
        {
            DocColumn sourceColumn = sourceColumns[sourceColumnIndex];
            if (string.IsNullOrWhiteSpace(sourceColumn.Id))
            {
                continue;
            }

            if (usedExistingColumnIds.Contains(sourceColumn.Id))
            {
                continue;
            }

            DocColumn? bestMatch = null;
            for (int existingColumnIndex = 0; existingColumnIndex < existingColumns.Count; existingColumnIndex++)
            {
                DocColumn existingColumn = existingColumns[existingColumnIndex];
                if (string.IsNullOrWhiteSpace(existingColumn.Id) ||
                    usedExistingColumnIds.Contains(existingColumn.Id) ||
                    !string.Equals(existingColumn.Name, sourceColumn.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (existingColumn.Kind == sourceColumn.Kind)
                {
                    bestMatch = existingColumn;
                    break;
                }

                if (bestMatch == null)
                {
                    bestMatch = existingColumn;
                }
            }

            if (bestMatch == null)
            {
                continue;
            }

            usedExistingColumnIds.Add(bestMatch.Id);
            if (!string.Equals(bestMatch.Id, sourceColumn.Id, StringComparison.Ordinal))
            {
                remap[bestMatch.Id] = sourceColumn.Id;
            }
        }

        return remap;
    }

    private static void RemapTableCellDataToSourceSchema(DocTable table, Dictionary<string, string> columnIdRemap)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            RemapRowCells(table.Rows[rowIndex], columnIdRemap);
        }

        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta delta = table.VariantDeltas[deltaIndex];
            for (int rowIndex = 0; rowIndex < delta.AddedRows.Count; rowIndex++)
            {
                RemapRowCells(delta.AddedRows[rowIndex], columnIdRemap);
            }

            for (int overrideIndex = 0; overrideIndex < delta.CellOverrides.Count; overrideIndex++)
            {
                DocTableCellOverride cellOverride = delta.CellOverrides[overrideIndex];
                if (columnIdRemap.TryGetValue(cellOverride.ColumnId, out string? remappedColumnId))
                {
                    cellOverride.ColumnId = remappedColumnId;
                    delta.CellOverrides[overrideIndex] = cellOverride;
                }
            }

            DeduplicateVariantCellOverrides(delta);
        }
    }

    private static void RemapRowCells(DocRow row, Dictionary<string, string> columnIdRemap)
    {
        foreach (KeyValuePair<string, string> remapEntry in columnIdRemap)
        {
            if (!row.Cells.TryGetValue(remapEntry.Key, out DocCellValue sourceCellValue))
            {
                continue;
            }

            if (!row.Cells.ContainsKey(remapEntry.Value))
            {
                row.Cells[remapEntry.Value] = sourceCellValue;
            }
        }
    }

    private static void DeduplicateVariantCellOverrides(DocTableVariantDelta delta)
    {
        var seenOverrides = new HashSet<(string RowId, string ColumnId)>();
        for (int overrideIndex = delta.CellOverrides.Count - 1; overrideIndex >= 0; overrideIndex--)
        {
            DocTableCellOverride cellOverride = delta.CellOverrides[overrideIndex];
            var key = (cellOverride.RowId ?? "", cellOverride.ColumnId ?? "");
            if (!seenOverrides.Add(key))
            {
                delta.CellOverrides.RemoveAt(overrideIndex);
            }
        }
    }

    private static void TrimTableCellDataToSchema(DocTable table, HashSet<string> validColumnIds)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            TrimRowToSchema(table.Rows[rowIndex], validColumnIds);
        }

        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta delta = table.VariantDeltas[deltaIndex];

            for (int rowIndex = 0; rowIndex < delta.AddedRows.Count; rowIndex++)
            {
                TrimRowToSchema(delta.AddedRows[rowIndex], validColumnIds);
            }

            for (int overrideIndex = delta.CellOverrides.Count - 1; overrideIndex >= 0; overrideIndex--)
            {
                DocTableCellOverride cellOverride = delta.CellOverrides[overrideIndex];
                if (!validColumnIds.Contains(cellOverride.ColumnId))
                {
                    delta.CellOverrides.RemoveAt(overrideIndex);
                }
            }
        }
    }

    private static void EnsureTableCellDataForSchema(DocTable table, IReadOnlyList<DocColumn> schemaColumns)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            EnsureRowHasSchemaCells(table.Rows[rowIndex], schemaColumns);
        }

        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta delta = table.VariantDeltas[deltaIndex];
            for (int rowIndex = 0; rowIndex < delta.AddedRows.Count; rowIndex++)
            {
                EnsureRowHasSchemaCells(delta.AddedRows[rowIndex], schemaColumns);
            }
        }
    }

    private static void EnsureRowHasSchemaCells(DocRow row, IReadOnlyList<DocColumn> schemaColumns)
    {
        for (int columnIndex = 0; columnIndex < schemaColumns.Count; columnIndex++)
        {
            DocColumn column = schemaColumns[columnIndex];
            if (!row.Cells.TryGetValue(column.Id, out DocCellValue existingCell))
            {
                row.Cells[column.Id] = CreateDefaultCellForColumn(column, row.Id);
                continue;
            }

            if (column.Kind == DocColumnKind.Id && string.IsNullOrWhiteSpace(existingCell.StringValue))
            {
                row.Cells[column.Id] = DocCellValue.Text(row.Id);
            }
        }
    }

    private static DocCellValue CreateDefaultCellForColumn(DocColumn column, string rowId)
    {
        if (column.Kind == DocColumnKind.Id)
        {
            return DocCellValue.Text(rowId);
        }

        return DocCellValue.Default(column);
    }

    private static void TrimRowToSchema(DocRow row, HashSet<string> validColumnIds)
    {
        if (row.Cells.Count <= 0)
        {
            return;
        }

        var invalidColumnIds = new List<string>();
        foreach (var cellEntry in row.Cells)
        {
            if (!validColumnIds.Contains(cellEntry.Key))
            {
                invalidColumnIds.Add(cellEntry.Key);
            }
        }

        for (int invalidIndex = 0; invalidIndex < invalidColumnIds.Count; invalidIndex++)
        {
            row.Cells.Remove(invalidColumnIds[invalidIndex]);
        }
    }

    private static DocColumn CloneColumn(DocColumn sourceColumn)
    {
        return new DocColumn
        {
            Id = sourceColumn.Id,
            Name = sourceColumn.Name,
            Kind = sourceColumn.Kind,
            ColumnTypeId = sourceColumn.ColumnTypeId,
            PluginSettingsJson = sourceColumn.PluginSettingsJson,
            Width = sourceColumn.Width,
            Options = sourceColumn.Options != null ? new List<string>(sourceColumn.Options) : null,
            FormulaExpression = sourceColumn.FormulaExpression,
            RelationTableId = sourceColumn.RelationTableId,
            TableRefBaseTableId = sourceColumn.TableRefBaseTableId,
            RowRefTableRefColumnId = sourceColumn.RowRefTableRefColumnId,
            RelationTargetMode = sourceColumn.RelationTargetMode,
            RelationTableVariantId = sourceColumn.RelationTableVariantId,
            RelationDisplayColumnId = sourceColumn.RelationDisplayColumnId,
            IsHidden = sourceColumn.IsHidden,
            IsProjected = sourceColumn.IsProjected,
            IsInherited = sourceColumn.IsInherited,
            ExportType = sourceColumn.ExportType,
            NumberMin = sourceColumn.NumberMin,
            NumberMax = sourceColumn.NumberMax,
            ExportEnumName = sourceColumn.ExportEnumName,
            ExportIgnore = sourceColumn.ExportIgnore,
            SubtableId = sourceColumn.SubtableId,
            SubtableDisplayRendererId = sourceColumn.SubtableDisplayRendererId,
            SubtableDisplayCellWidth = sourceColumn.SubtableDisplayCellWidth,
            SubtableDisplayCellHeight = sourceColumn.SubtableDisplayCellHeight,
            SubtableDisplayPreviewQuality = sourceColumn.SubtableDisplayPreviewQuality,
            FormulaEvalScopes = sourceColumn.FormulaEvalScopes,
            ModelPreviewSettings = sourceColumn.ModelPreviewSettings?.Clone(),
        };
    }

    private static string BuildDependencyCyclePath(
        List<string> activePathTableIds,
        Dictionary<string, DocTable> tableById,
        string cycleStartTableId)
    {
        int startIndex = -1;
        for (int pathIndex = 0; pathIndex < activePathTableIds.Count; pathIndex++)
        {
            if (string.Equals(activePathTableIds[pathIndex], cycleStartTableId, StringComparison.Ordinal))
            {
                startIndex = pathIndex;
                break;
            }
        }

        if (startIndex < 0)
        {
            startIndex = 0;
        }

        var segments = new List<string>(activePathTableIds.Count - startIndex + 1);
        for (int pathIndex = startIndex; pathIndex < activePathTableIds.Count; pathIndex++)
        {
            string tableId = activePathTableIds[pathIndex];
            segments.Add(tableById.TryGetValue(tableId, out DocTable? table) ? table.Name : tableId);
        }

        segments.Add(tableById.TryGetValue(cycleStartTableId, out DocTable? startTable)
            ? startTable.Name
            : cycleStartTableId);

        return string.Join(" -> ", segments);
    }
}
