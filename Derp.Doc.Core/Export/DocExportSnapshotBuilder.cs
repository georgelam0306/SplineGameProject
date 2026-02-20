using Derp.Doc.Model;
using Derp.Doc.Tables;

namespace Derp.Doc.Export;

internal static class DocExportSnapshotBuilder
{
    public static DocProject BuildBase(DocProject sourceProject)
    {
        var snapshot = CloneProject(sourceProject);
        SchemaLinkedTableSynchronizer.Synchronize(snapshot);

        // Derived tables do not support variant deltas.
        for (int tableIndex = 0; tableIndex < snapshot.Tables.Count; tableIndex++)
        {
            if (snapshot.Tables[tableIndex].IsDerived)
            {
                snapshot.Tables[tableIndex].VariantDeltas.Clear();
            }
        }

        return snapshot;
    }

    public static DocProject BuildWithTableVariant(DocProject sourceProject, string tableId, int variantId)
    {
        var snapshot = CloneProject(sourceProject);
        SchemaLinkedTableSynchronizer.Synchronize(snapshot);

        for (int tableIndex = 0; tableIndex < snapshot.Tables.Count; tableIndex++)
        {
            DocTable table = snapshot.Tables[tableIndex];
            if (table.IsDerived)
            {
                table.VariantDeltas.Clear();
                continue;
            }

            if (string.Equals(table.Id, tableId, StringComparison.Ordinal))
            {
                MaterializeBaseTableRows(table, variantId);
            }
        }

        return snapshot;
    }

    private static void MaterializeBaseTableRows(DocTable table, int variantId)
    {
        if (!TryGetDeltaForVariant(table, variantId, out DocTableVariantDelta? delta))
        {
            return;
        }

        DocTableVariantDelta variantDelta = delta!;
        var materializedRows = new List<DocRow>(table.Rows.Count + variantDelta.AddedRows.Count);
        var materializedRowById = new Dictionary<string, DocRow>(StringComparer.Ordinal);
        var deletedBaseRowIds = new HashSet<string>(variantDelta.DeletedBaseRowIds, StringComparer.Ordinal);
        var validColumnIds = BuildColumnIdSet(table);

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow row = table.Rows[rowIndex];
            if (deletedBaseRowIds.Contains(row.Id))
            {
                continue;
            }

            TrimRowToSchema(row, validColumnIds);
            materializedRows.Add(row);
            materializedRowById[row.Id] = row;
        }

        for (int addedRowIndex = 0; addedRowIndex < variantDelta.AddedRows.Count; addedRowIndex++)
        {
            DocRow addedRow = CloneRow(variantDelta.AddedRows[addedRowIndex]);
            TrimRowToSchema(addedRow, validColumnIds);
            materializedRows.Add(addedRow);
            materializedRowById[addedRow.Id] = addedRow;
        }

        for (int overrideIndex = 0; overrideIndex < variantDelta.CellOverrides.Count; overrideIndex++)
        {
            DocTableCellOverride cellOverride = variantDelta.CellOverrides[overrideIndex];
            if (!validColumnIds.Contains(cellOverride.ColumnId) ||
                !materializedRowById.TryGetValue(cellOverride.RowId, out DocRow? row))
            {
                continue;
            }

            row.Cells[cellOverride.ColumnId] = cellOverride.Value.Clone();
        }

        table.Rows = materializedRows;
    }

    private static bool TryGetDeltaForVariant(DocTable table, int variantId, out DocTableVariantDelta? delta)
    {
        delta = null;
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return false;
        }

        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta current = table.VariantDeltas[deltaIndex];
            if (current.VariantId == variantId)
            {
                delta = current;
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildColumnIdSet(DocTable table)
    {
        var ids = new HashSet<string>(table.Columns.Count, StringComparer.Ordinal);
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            ids.Add(table.Columns[columnIndex].Id);
        }

        return ids;
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

    private static DocProject CloneProject(DocProject sourceProject)
    {
        var clonedProject = new DocProject
        {
            Name = sourceProject.Name,
            UiState = CloneProjectUiState(sourceProject.UiState),
            PluginSettingsByKey = new Dictionary<string, string>(sourceProject.PluginSettingsByKey, StringComparer.Ordinal),
            Folders = new List<DocFolder>(sourceProject.Folders.Count),
            Tables = new List<DocTable>(sourceProject.Tables.Count),
            Documents = new List<DocDocument>(sourceProject.Documents.Count),
        };

        for (int folderIndex = 0; folderIndex < sourceProject.Folders.Count; folderIndex++)
        {
            clonedProject.Folders.Add(sourceProject.Folders[folderIndex].Clone());
        }

        for (int tableIndex = 0; tableIndex < sourceProject.Tables.Count; tableIndex++)
        {
            clonedProject.Tables.Add(CloneTable(sourceProject.Tables[tableIndex]));
        }

        for (int documentIndex = 0; documentIndex < sourceProject.Documents.Count; documentIndex++)
        {
            clonedProject.Documents.Add(CloneDocument(sourceProject.Documents[documentIndex]));
        }

        return clonedProject;
    }

    private static DocProjectUiState CloneProjectUiState(DocProjectUiState sourceUiState)
    {
        return new DocProjectUiState
        {
            TableFolderExpandedById = new Dictionary<string, bool>(sourceUiState.TableFolderExpandedById, StringComparer.Ordinal),
            DocumentFolderExpandedById = new Dictionary<string, bool>(sourceUiState.DocumentFolderExpandedById, StringComparer.Ordinal),
        };
    }

    private static DocTable CloneTable(DocTable sourceTable)
    {
        var clonedTable = new DocTable
        {
            Id = sourceTable.Id,
            Name = sourceTable.Name,
            FolderId = sourceTable.FolderId,
            FileName = sourceTable.FileName,
            DerivedConfig = sourceTable.IsDerived ? sourceTable.DerivedConfig!.Clone() : null,
            ExportConfig = sourceTable.ExportConfig != null ? sourceTable.ExportConfig.Clone() : null,
            Keys = sourceTable.Keys.Clone(),
            Views = new List<DocView>(sourceTable.Views.Count),
            Variables = new List<DocTableVariable>(sourceTable.Variables.Count),
            SchemaSourceTableId = sourceTable.SchemaSourceTableId,
            InheritanceSourceTableId = sourceTable.InheritanceSourceTableId,
            SystemKey = sourceTable.SystemKey,
            IsSystemSchemaLocked = sourceTable.IsSystemSchemaLocked,
            IsSystemDataLocked = sourceTable.IsSystemDataLocked,
            ParentTableId = sourceTable.ParentTableId,
            ParentRowColumnId = sourceTable.ParentRowColumnId,
            PluginTableTypeId = sourceTable.PluginTableTypeId,
            PluginOwnerColumnTypeId = sourceTable.PluginOwnerColumnTypeId,
            IsPluginSchemaLocked = sourceTable.IsPluginSchemaLocked,
            Variants = new List<DocTableVariant>(sourceTable.Variants.Count),
            Rows = new List<DocRow>(sourceTable.Rows.Count),
            VariantDeltas = new List<DocTableVariantDelta>(sourceTable.VariantDeltas.Count),
        };

        for (int columnIndex = 0; columnIndex < sourceTable.Columns.Count; columnIndex++)
        {
            clonedTable.Columns.Add(CloneColumn(sourceTable.Columns[columnIndex]));
        }

        for (int variantIndex = 0; variantIndex < sourceTable.Variants.Count; variantIndex++)
        {
            clonedTable.Variants.Add(sourceTable.Variants[variantIndex].Clone());
        }

        for (int rowIndex = 0; rowIndex < sourceTable.Rows.Count; rowIndex++)
        {
            clonedTable.Rows.Add(CloneRow(sourceTable.Rows[rowIndex]));
        }

        for (int variantDeltaIndex = 0; variantDeltaIndex < sourceTable.VariantDeltas.Count; variantDeltaIndex++)
        {
            clonedTable.VariantDeltas.Add(sourceTable.VariantDeltas[variantDeltaIndex].Clone());
        }

        for (int viewIndex = 0; viewIndex < sourceTable.Views.Count; viewIndex++)
        {
            clonedTable.Views.Add(sourceTable.Views[viewIndex].Clone());
        }

        for (int variableIndex = 0; variableIndex < sourceTable.Variables.Count; variableIndex++)
        {
            clonedTable.Variables.Add(sourceTable.Variables[variableIndex].Clone());
        }

        return clonedTable;
    }

    private static DocRow CloneRow(DocRow sourceRow)
    {
        var clone = new DocRow
        {
            Id = sourceRow.Id,
            Cells = new Dictionary<string, DocCellValue>(sourceRow.Cells.Count, StringComparer.Ordinal),
        };

        foreach (var kvp in sourceRow.Cells)
        {
            clone.Cells[kvp.Key] = kvp.Value.Clone();
        }

        return clone;
    }

    private static DocDocument CloneDocument(DocDocument sourceDocument)
    {
        var clone = new DocDocument
        {
            Id = sourceDocument.Id,
            Title = sourceDocument.Title,
            FolderId = sourceDocument.FolderId,
            FileName = sourceDocument.FileName,
            Blocks = new List<DocBlock>(sourceDocument.Blocks.Count),
        };

        for (int blockIndex = 0; blockIndex < sourceDocument.Blocks.Count; blockIndex++)
        {
            clone.Blocks.Add(sourceDocument.Blocks[blockIndex].Clone());
        }

        return clone;
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
}
