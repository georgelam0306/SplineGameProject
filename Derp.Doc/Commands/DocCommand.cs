using Derp.Doc.Model;

namespace Derp.Doc.Commands;

/// <summary>
/// Discriminated command type for all document mutations.
/// Each command stores before/after state for reversibility.
/// </summary>
internal enum DocCommandKind
{
    // Table commands
    SetCell,
    AddRow,
    RemoveRow,
    MoveRow,
    AddColumn,
    RemoveColumn,
    AddTable,
    RemoveTable,
    RenameTable,
    RenameColumn,
    MoveColumn,
    SetColumnWidth,
    SetColumnFormula,
    SetColumnPluginSettings,
    SetColumnRelation,
    SetColumnOptions,
    SetColumnModelPreview,
    SetColumnHidden,
    SetColumnExportIgnore,
    SetColumnExportType,
    SetColumnNumberSettings,
    SetColumnExportEnumName,
    SetColumnSubtableDisplay,

    // Export config + keys (Phase 5)
    SetTableExportConfig,
    SetTableKeys,
    SetTableSchemaSource,
    SetTableInheritanceSource,

    // Derived table commands
    SetDerivedConfig,
    SetDerivedBaseTable,
    AddDerivedStep,
    RemoveDerivedStep,
    UpdateDerivedStep,
    ReorderDerivedStep,
    AddDerivedProjection,
    RemoveDerivedProjection,
    UpdateDerivedProjection,
    ReorderDerivedProjection,

    // View commands (Phase 6)
    AddView,
    RemoveView,
    RenameView,
    UpdateViewConfig,
    AddTableVariant,
    AddTableVariable,
    RemoveTableVariable,
    RenameTableVariable,
    SetTableVariableExpression,
    SetTableVariableType,

    // Folder commands
    AddFolder,
    RemoveFolder,
    RenameFolder,
    MoveFolder,
    SetTableFolder,
    SetDocumentFolder,

    // Document commands
    AddDocument,
    RemoveDocument,
    RenameDocument,
    AddBlock,
    RemoveBlock,
    SetBlockText,
    SetBlockTableReference,
    ChangeBlockType,
    ToggleBlockCheck,
    ToggleSpan,
    SetBlockIndent,
    SetBlockEmbeddedSize,
    SetBlockTableVariant,
    SetBlockTableVariableOverride,
    MoveBlock,

    // Workspace snapshot command (used for external MCP mutations)
    ReplaceProjectSnapshot,
}

internal sealed class DocCommand
{
    public DocCommandKind Kind { get; init; }

    // Common references
    public string TableId { get; init; } = "";

    // SetCell
    public string RowId { get; init; } = "";
    public string ColumnId { get; init; } = "";
    public int TableVariantId { get; set; } = DocTableVariant.BaseVariantId;
    public DocCellValue OldCellValue { get; init; }
    public DocCellValue NewCellValue { get; init; }

    // AddRow / RemoveRow
    public int RowIndex { get; init; }
    public int TargetRowIndex { get; init; }
    public DocRow? RowSnapshot { get; init; }
    public List<DocTableCellOverride>? RemovedVariantCellOverrides { get; set; }

    // AddColumn / RemoveColumn
    public int ColumnIndex { get; init; }
    public int TargetColumnIndex { get; init; }
    public DocColumn? ColumnSnapshot { get; init; }
    public Dictionary<string, DocCellValue>? ColumnCellSnapshots { get; init; } // rowId -> cellValue

    // AddTable / RemoveTable
    public int TableIndex { get; init; }
    public DocTable? TableSnapshot { get; init; }

    // Rename
    public string OldName { get; init; } = "";
    public string NewName { get; init; } = "";
    public float OldColumnWidth { get; init; }
    public float NewColumnWidth { get; init; }
    public string OldFormulaExpression { get; init; } = "";
    public string NewFormulaExpression { get; init; } = "";
    public string? OldPluginSettingsJson { get; init; }
    public string? NewPluginSettingsJson { get; init; }
    public string? OldRelationTableId { get; init; }
    public string? NewRelationTableId { get; init; }
    public DocRelationTargetMode OldRelationTargetMode { get; init; } = DocRelationTargetMode.ExternalTable;
    public DocRelationTargetMode NewRelationTargetMode { get; init; } = DocRelationTargetMode.ExternalTable;
    public int OldRelationTableVariantId { get; init; }
    public int NewRelationTableVariantId { get; init; }
    public string? OldRelationDisplayColumnId { get; init; }
    public string? NewRelationDisplayColumnId { get; init; }
    public List<string>? OldOptionsSnapshot { get; init; }
    public List<string>? NewOptionsSnapshot { get; init; }
    public DocModelPreviewSettings? OldModelPreviewSettings { get; init; }
    public DocModelPreviewSettings? NewModelPreviewSettings { get; init; }
    public bool OldHidden { get; init; }
    public bool NewHidden { get; init; }
    public bool OldExportIgnore { get; init; }
    public bool NewExportIgnore { get; init; }
    public string? OldExportType { get; init; }
    public string? NewExportType { get; init; }
    public double? OldNumberMin { get; init; }
    public double? NewNumberMin { get; init; }
    public double? OldNumberMax { get; init; }
    public double? NewNumberMax { get; init; }
    public Dictionary<string, double>? OldNumberValuesByRowId { get; init; }
    public Dictionary<string, double>? NewNumberValuesByRowId { get; init; }
    public string? OldExportEnumName { get; init; }
    public string? NewExportEnumName { get; init; }
    public string? OldSubtableDisplayRendererId { get; init; }
    public string? NewSubtableDisplayRendererId { get; init; }
    public float? OldSubtableDisplayCellWidth { get; init; }
    public float? NewSubtableDisplayCellWidth { get; init; }
    public float? OldSubtableDisplayCellHeight { get; init; }
    public float? NewSubtableDisplayCellHeight { get; init; }
    public string? OldSubtableDisplayPluginSettingsJson { get; init; }
    public string? NewSubtableDisplayPluginSettingsJson { get; init; }
    public DocSubtablePreviewQuality? OldSubtableDisplayPreviewQuality { get; init; }
    public DocSubtablePreviewQuality? NewSubtableDisplayPreviewQuality { get; init; }

    public DocTableExportConfig? OldExportConfigSnapshot { get; init; }
    public DocTableExportConfig? NewExportConfigSnapshot { get; init; }
    public DocTableKeys? OldKeysSnapshot { get; init; }
    public DocTableKeys? NewKeysSnapshot { get; init; }
    public string? OldSchemaSourceTableId { get; init; }
    public string? NewSchemaSourceTableId { get; init; }
    public string? OldInheritanceSourceTableId { get; init; }
    public string? NewInheritanceSourceTableId { get; init; }

    // Derived table commands
    public DocDerivedConfig? OldDerivedConfig { get; init; }
    public DocDerivedConfig? NewDerivedConfig { get; init; }
    public string? OldBaseTableId { get; init; }
    public string? NewBaseTableId { get; init; }
    public int StepIndex { get; init; }
    public int TargetStepIndex { get; init; }
    public DerivedStep? StepSnapshot { get; init; }
    public DerivedStep? OldStepSnapshot { get; init; }
    public int ProjectionIndex { get; init; }
    public int TargetProjectionIndex { get; init; }
    public DerivedProjection? ProjectionSnapshot { get; init; }
    public DerivedProjection? OldProjectionSnapshot { get; init; }

    // View commands (Phase 6)
    public string ViewId { get; init; } = "";
    public int ViewIndex { get; init; }
    public DocView? ViewSnapshot { get; init; }
    public DocView? OldViewSnapshot { get; init; }
    public int TableVariantIndex { get; init; }
    public DocTableVariant? TableVariantSnapshot { get; init; }
    public string TableVariableId { get; init; } = "";
    public int TableVariableIndex { get; init; }
    public DocTableVariable? TableVariableSnapshot { get; init; }
    public string OldTableVariableExpression { get; init; } = "";
    public string NewTableVariableExpression { get; init; } = "";
    public DocColumnKind OldTableVariableKind { get; init; }
    public DocColumnKind NewTableVariableKind { get; init; }
    public string? OldTableVariableTypeId { get; init; }
    public string? NewTableVariableTypeId { get; init; }

    // Folder commands
    public string FolderId { get; init; } = "";
    public int FolderIndex { get; init; }
    public DocFolder? FolderSnapshot { get; init; }
    public string? OldParentFolderId { get; init; }
    public string? NewParentFolderId { get; init; }
    public string? OldFolderId { get; init; }
    public string? NewFolderId { get; init; }

    // Document commands
    public string DocumentId { get; init; } = "";

    // AddDocument / RemoveDocument
    public int DocumentIndex { get; init; }
    public DocDocument? DocumentSnapshot { get; init; }

    // AddBlock / RemoveBlock
    public string BlockId { get; init; } = "";
    public int BlockIndex { get; init; }
    public DocBlock? BlockSnapshot { get; init; }

    // SetBlockText
    public string OldBlockText { get; init; } = "";
    public string NewBlockText { get; init; } = "";
    public List<RichSpan>? OldSpans { get; init; }
    public List<RichSpan>? NewSpans { get; init; }
    public string OldTableId { get; init; } = "";
    public string NewTableId { get; init; } = "";

    // ChangeBlockType
    public DocBlockType OldBlockType { get; init; }
    public DocBlockType NewBlockType { get; init; }

    // ToggleBlockCheck
    public bool OldChecked { get; init; }
    public bool NewChecked { get; init; }

    // ToggleSpan
    public int SpanStart { get; init; }
    public int SpanLength { get; init; }
    public RichSpanStyle SpanStyle { get; init; }

    // SetBlockIndent
    public int OldIndentLevel { get; init; }
    public int NewIndentLevel { get; init; }

    // SetBlockEmbeddedSize
    public float OldEmbeddedWidth { get; init; }
    public float NewEmbeddedWidth { get; init; }
    public float OldEmbeddedHeight { get; init; }
    public float NewEmbeddedHeight { get; init; }
    public int OldBlockTableVariantId { get; init; }
    public int NewBlockTableVariantId { get; init; }
    public string OldBlockTableVariableExpression { get; init; } = "";
    public string NewBlockTableVariableExpression { get; init; } = "";

    // MoveBlock
    public int TargetBlockIndex { get; init; }

    // ReplaceProjectSnapshot
    public DocProject? OldProjectSnapshot { get; init; }
    public DocProject? NewProjectSnapshot { get; init; }

    public void Execute(DocProject project)
    {
        switch (Kind)
        {
            case DocCommandKind.SetCell:
                ExecuteSetCell(project);
                break;
            case DocCommandKind.AddRow:
                ExecuteAddRow(project);
                break;
            case DocCommandKind.RemoveRow:
                ExecuteRemoveRow(project);
                break;
            case DocCommandKind.MoveRow:
                ExecuteMoveRow(project, RowIndex, TargetRowIndex);
                break;
            case DocCommandKind.AddColumn:
                ExecuteAddColumn(project);
                break;
            case DocCommandKind.RemoveColumn:
                ExecuteRemoveColumn(project);
                break;
            case DocCommandKind.AddTable:
                ExecuteAddTable(project);
                break;
            case DocCommandKind.RemoveTable:
                ExecuteRemoveTable(project);
                break;
            case DocCommandKind.RenameTable:
                ExecuteRenameTable(project);
                break;
            case DocCommandKind.RenameColumn:
                ExecuteRenameColumn(project);
                break;
            case DocCommandKind.MoveColumn:
                ExecuteMoveColumn(project, ColumnIndex, TargetColumnIndex);
                break;
            case DocCommandKind.SetColumnWidth:
                ExecuteSetColumnWidth(project, NewColumnWidth);
                break;
            case DocCommandKind.SetColumnFormula:
                ExecuteSetColumnFormula(project, NewFormulaExpression);
                break;
            case DocCommandKind.SetColumnPluginSettings:
                ExecuteSetColumnPluginSettings(project, NewPluginSettingsJson);
                break;
            case DocCommandKind.SetColumnRelation:
                ExecuteSetColumnRelation(
                    project,
                    NewRelationTableId,
                    NewRelationTargetMode,
                    NewRelationTableVariantId,
                    NewRelationDisplayColumnId);
                break;
            case DocCommandKind.SetColumnOptions:
                ExecuteSetColumnOptions(project, NewOptionsSnapshot);
                break;
            case DocCommandKind.SetColumnModelPreview:
                ExecuteSetColumnModelPreview(project, NewModelPreviewSettings);
                break;
            case DocCommandKind.SetColumnHidden:
                ExecuteSetColumnHidden(project, NewHidden);
                break;
            case DocCommandKind.SetColumnExportIgnore:
                ExecuteSetColumnExportIgnore(project, NewExportIgnore);
                break;
            case DocCommandKind.SetColumnExportType:
                ExecuteSetColumnExportType(project, NewExportType);
                break;
            case DocCommandKind.SetColumnNumberSettings:
                ExecuteSetColumnNumberSettings(project, NewExportType, NewNumberMin, NewNumberMax, NewNumberValuesByRowId);
                break;
            case DocCommandKind.SetColumnExportEnumName:
                ExecuteSetColumnExportEnumName(project, NewExportEnumName);
                break;
            case DocCommandKind.SetColumnSubtableDisplay:
                ExecuteSetColumnSubtableDisplay(
                    project,
                    NewSubtableDisplayRendererId,
                    NewSubtableDisplayCellWidth,
                    NewSubtableDisplayCellHeight,
                    NewSubtableDisplayPluginSettingsJson,
                    NewSubtableDisplayPreviewQuality);
                break;
            case DocCommandKind.SetTableExportConfig:
                ExecuteSetTableExportConfig(project, NewExportConfigSnapshot);
                break;
            case DocCommandKind.SetTableKeys:
                ExecuteSetTableKeys(project, NewKeysSnapshot);
                break;
            case DocCommandKind.SetTableSchemaSource:
                ExecuteSetTableSchemaSource(project, NewSchemaSourceTableId);
                break;
            case DocCommandKind.SetTableInheritanceSource:
                ExecuteSetTableInheritanceSource(project, NewInheritanceSourceTableId);
                break;
            case DocCommandKind.SetDerivedConfig:
                ExecuteSetDerivedConfig(project, NewDerivedConfig);
                break;
            case DocCommandKind.SetDerivedBaseTable:
                ExecuteSetDerivedBaseTable(project, NewBaseTableId);
                break;
            case DocCommandKind.AddDerivedStep:
                ExecuteAddDerivedStep(project);
                break;
            case DocCommandKind.RemoveDerivedStep:
                ExecuteRemoveDerivedStep(project);
                break;
            case DocCommandKind.UpdateDerivedStep:
                ExecuteUpdateDerivedStep(project, StepSnapshot);
                break;
            case DocCommandKind.ReorderDerivedStep:
                ExecuteReorderDerivedStep(project, StepIndex, TargetStepIndex);
                break;
            case DocCommandKind.AddDerivedProjection:
                ExecuteAddDerivedProjection(project);
                break;
            case DocCommandKind.RemoveDerivedProjection:
                ExecuteRemoveDerivedProjection(project);
                break;
            case DocCommandKind.UpdateDerivedProjection:
                ExecuteUpdateDerivedProjection(project, ProjectionSnapshot);
                break;
            case DocCommandKind.ReorderDerivedProjection:
                ExecuteReorderDerivedProjection(project, ProjectionIndex, TargetProjectionIndex);
                break;
            case DocCommandKind.AddView:
                ExecuteAddView(project);
                break;
            case DocCommandKind.RemoveView:
                ExecuteRemoveView(project);
                break;
            case DocCommandKind.RenameView:
                ExecuteRenameView(project, NewName);
                break;
            case DocCommandKind.UpdateViewConfig:
                ExecuteUpdateViewConfig(project, ViewSnapshot);
                break;
            case DocCommandKind.AddTableVariant:
                ExecuteAddTableVariant(project);
                break;
            case DocCommandKind.AddTableVariable:
                ExecuteAddTableVariable(project);
                break;
            case DocCommandKind.RemoveTableVariable:
                ExecuteRemoveTableVariable(project);
                break;
            case DocCommandKind.RenameTableVariable:
                ExecuteRenameTableVariable(project, NewName);
                break;
            case DocCommandKind.SetTableVariableExpression:
                ExecuteSetTableVariableExpression(project, NewTableVariableExpression);
                break;
            case DocCommandKind.SetTableVariableType:
                ExecuteSetTableVariableType(project, NewTableVariableKind, NewTableVariableTypeId);
                break;
            case DocCommandKind.AddFolder:
                ExecuteAddFolder(project);
                break;
            case DocCommandKind.RemoveFolder:
                ExecuteRemoveFolder(project);
                break;
            case DocCommandKind.RenameFolder:
                ExecuteRenameFolder(project, NewName);
                break;
            case DocCommandKind.MoveFolder:
                ExecuteMoveFolder(project, NewParentFolderId);
                break;
            case DocCommandKind.SetTableFolder:
                ExecuteSetTableFolder(project, NewFolderId);
                break;
            case DocCommandKind.SetDocumentFolder:
                ExecuteSetDocumentFolder(project, NewFolderId);
                break;
            case DocCommandKind.AddDocument:
                ExecuteAddDocument(project);
                break;
            case DocCommandKind.RemoveDocument:
                ExecuteRemoveDocument(project);
                break;
            case DocCommandKind.RenameDocument:
                ExecuteRenameDocument(project);
                break;
            case DocCommandKind.AddBlock:
                ExecuteAddBlock(project);
                break;
            case DocCommandKind.RemoveBlock:
                ExecuteRemoveBlock(project);
                break;
            case DocCommandKind.SetBlockText:
                ExecuteSetBlockText(project);
                break;
            case DocCommandKind.SetBlockTableReference:
                ExecuteSetBlockTableReference(project, NewTableId);
                break;
            case DocCommandKind.ChangeBlockType:
                ExecuteChangeBlockType(project);
                break;
            case DocCommandKind.ToggleBlockCheck:
                ExecuteToggleBlockCheck(project, NewChecked);
                break;
            case DocCommandKind.ToggleSpan:
                ExecuteToggleSpan(project);
                break;
            case DocCommandKind.SetBlockIndent:
                ExecuteSetBlockIndent(project, NewIndentLevel);
                break;
            case DocCommandKind.SetBlockEmbeddedSize:
                ExecuteSetBlockEmbeddedSize(project, NewEmbeddedWidth, NewEmbeddedHeight);
                break;
            case DocCommandKind.SetBlockTableVariant:
                ExecuteSetBlockTableVariant(project, NewBlockTableVariantId);
                break;
            case DocCommandKind.SetBlockTableVariableOverride:
                ExecuteSetBlockTableVariableOverride(project, NewBlockTableVariableExpression);
                break;
            case DocCommandKind.MoveBlock:
                ExecuteMoveBlock(project, BlockIndex, TargetBlockIndex);
                break;
            case DocCommandKind.ReplaceProjectSnapshot:
                ExecuteReplaceProjectSnapshot(project, NewProjectSnapshot);
                break;
        }
    }

    public void Undo(DocProject project)
    {
        switch (Kind)
        {
            case DocCommandKind.SetCell:
                UndoSetCell(project);
                break;
            case DocCommandKind.AddRow:
                UndoAddRow(project);
                break;
            case DocCommandKind.RemoveRow:
                UndoRemoveRow(project);
                break;
            case DocCommandKind.MoveRow:
                UndoMoveRow(project);
                break;
            case DocCommandKind.AddColumn:
                UndoAddColumn(project);
                break;
            case DocCommandKind.RemoveColumn:
                UndoRemoveColumn(project);
                break;
            case DocCommandKind.AddTable:
                UndoAddTable(project);
                break;
            case DocCommandKind.RemoveTable:
                UndoRemoveTable(project);
                break;
            case DocCommandKind.RenameTable:
                UndoRenameTable(project);
                break;
            case DocCommandKind.RenameColumn:
                UndoRenameColumn(project);
                break;
            case DocCommandKind.MoveColumn:
                UndoMoveColumn(project);
                break;
            case DocCommandKind.SetColumnWidth:
                ExecuteSetColumnWidth(project, OldColumnWidth);
                break;
            case DocCommandKind.SetColumnFormula:
                ExecuteSetColumnFormula(project, OldFormulaExpression);
                break;
            case DocCommandKind.SetColumnPluginSettings:
                ExecuteSetColumnPluginSettings(project, OldPluginSettingsJson);
                break;
            case DocCommandKind.SetColumnRelation:
                ExecuteSetColumnRelation(
                    project,
                    OldRelationTableId,
                    OldRelationTargetMode,
                    OldRelationTableVariantId,
                    OldRelationDisplayColumnId);
                break;
            case DocCommandKind.SetColumnOptions:
                ExecuteSetColumnOptions(project, OldOptionsSnapshot);
                break;
            case DocCommandKind.SetColumnModelPreview:
                ExecuteSetColumnModelPreview(project, OldModelPreviewSettings);
                break;
            case DocCommandKind.SetColumnHidden:
                ExecuteSetColumnHidden(project, OldHidden);
                break;
            case DocCommandKind.SetColumnExportIgnore:
                ExecuteSetColumnExportIgnore(project, OldExportIgnore);
                break;
            case DocCommandKind.SetColumnExportType:
                ExecuteSetColumnExportType(project, OldExportType);
                break;
            case DocCommandKind.SetColumnNumberSettings:
                ExecuteSetColumnNumberSettings(project, OldExportType, OldNumberMin, OldNumberMax, OldNumberValuesByRowId);
                break;
            case DocCommandKind.SetColumnExportEnumName:
                ExecuteSetColumnExportEnumName(project, OldExportEnumName);
                break;
            case DocCommandKind.SetColumnSubtableDisplay:
                ExecuteSetColumnSubtableDisplay(
                    project,
                    OldSubtableDisplayRendererId,
                    OldSubtableDisplayCellWidth,
                    OldSubtableDisplayCellHeight,
                    OldSubtableDisplayPluginSettingsJson,
                    OldSubtableDisplayPreviewQuality);
                break;
            case DocCommandKind.SetTableExportConfig:
                ExecuteSetTableExportConfig(project, OldExportConfigSnapshot);
                break;
            case DocCommandKind.SetTableKeys:
                ExecuteSetTableKeys(project, OldKeysSnapshot);
                break;
            case DocCommandKind.SetTableSchemaSource:
                ExecuteSetTableSchemaSource(project, OldSchemaSourceTableId);
                break;
            case DocCommandKind.SetTableInheritanceSource:
                ExecuteSetTableInheritanceSource(project, OldInheritanceSourceTableId);
                break;
            case DocCommandKind.SetDerivedConfig:
                ExecuteSetDerivedConfig(project, OldDerivedConfig);
                break;
            case DocCommandKind.SetDerivedBaseTable:
                ExecuteSetDerivedBaseTable(project, OldBaseTableId);
                break;
            case DocCommandKind.AddDerivedStep:
                UndoAddDerivedStep(project);
                break;
            case DocCommandKind.RemoveDerivedStep:
                UndoRemoveDerivedStep(project);
                break;
            case DocCommandKind.UpdateDerivedStep:
                ExecuteUpdateDerivedStep(project, OldStepSnapshot);
                break;
            case DocCommandKind.ReorderDerivedStep:
                ExecuteReorderDerivedStep(project, TargetStepIndex, StepIndex);
                break;
            case DocCommandKind.AddDerivedProjection:
                UndoAddDerivedProjection(project);
                break;
            case DocCommandKind.RemoveDerivedProjection:
                UndoRemoveDerivedProjection(project);
                break;
            case DocCommandKind.UpdateDerivedProjection:
                ExecuteUpdateDerivedProjection(project, OldProjectionSnapshot);
                break;
            case DocCommandKind.ReorderDerivedProjection:
                ExecuteReorderDerivedProjection(project, TargetProjectionIndex, ProjectionIndex);
                break;
            case DocCommandKind.AddView:
                UndoAddView(project);
                break;
            case DocCommandKind.RemoveView:
                UndoRemoveView(project);
                break;
            case DocCommandKind.RenameView:
                ExecuteRenameView(project, OldName);
                break;
            case DocCommandKind.UpdateViewConfig:
                ExecuteUpdateViewConfig(project, OldViewSnapshot);
                break;
            case DocCommandKind.AddTableVariant:
                UndoAddTableVariant(project);
                break;
            case DocCommandKind.AddTableVariable:
                UndoAddTableVariable(project);
                break;
            case DocCommandKind.RemoveTableVariable:
                UndoRemoveTableVariable(project);
                break;
            case DocCommandKind.RenameTableVariable:
                ExecuteRenameTableVariable(project, OldName);
                break;
            case DocCommandKind.SetTableVariableExpression:
                ExecuteSetTableVariableExpression(project, OldTableVariableExpression);
                break;
            case DocCommandKind.SetTableVariableType:
                ExecuteSetTableVariableType(project, OldTableVariableKind, OldTableVariableTypeId);
                break;
            case DocCommandKind.AddFolder:
                UndoAddFolder(project);
                break;
            case DocCommandKind.RemoveFolder:
                UndoRemoveFolder(project);
                break;
            case DocCommandKind.RenameFolder:
                ExecuteRenameFolder(project, OldName);
                break;
            case DocCommandKind.MoveFolder:
                ExecuteMoveFolder(project, OldParentFolderId);
                break;
            case DocCommandKind.SetTableFolder:
                ExecuteSetTableFolder(project, OldFolderId);
                break;
            case DocCommandKind.SetDocumentFolder:
                ExecuteSetDocumentFolder(project, OldFolderId);
                break;
            case DocCommandKind.AddDocument:
                UndoAddDocument(project);
                break;
            case DocCommandKind.RemoveDocument:
                UndoRemoveDocument(project);
                break;
            case DocCommandKind.RenameDocument:
                UndoRenameDocument(project);
                break;
            case DocCommandKind.AddBlock:
                UndoAddBlock(project);
                break;
            case DocCommandKind.RemoveBlock:
                UndoRemoveBlock(project);
                break;
            case DocCommandKind.SetBlockText:
                UndoSetBlockText(project);
                break;
            case DocCommandKind.SetBlockTableReference:
                ExecuteSetBlockTableReference(project, OldTableId);
                break;
            case DocCommandKind.ChangeBlockType:
                UndoChangeBlockType(project);
                break;
            case DocCommandKind.ToggleBlockCheck:
                ExecuteToggleBlockCheck(project, OldChecked);
                break;
            case DocCommandKind.ToggleSpan:
                UndoToggleSpan(project);
                break;
            case DocCommandKind.SetBlockIndent:
                ExecuteSetBlockIndent(project, OldIndentLevel);
                break;
            case DocCommandKind.SetBlockEmbeddedSize:
                ExecuteSetBlockEmbeddedSize(project, OldEmbeddedWidth, OldEmbeddedHeight);
                break;
            case DocCommandKind.SetBlockTableVariant:
                ExecuteSetBlockTableVariant(project, OldBlockTableVariantId);
                break;
            case DocCommandKind.SetBlockTableVariableOverride:
                ExecuteSetBlockTableVariableOverride(project, OldBlockTableVariableExpression);
                break;
            case DocCommandKind.MoveBlock:
                ExecuteMoveBlock(project, TargetBlockIndex, BlockIndex);
                break;
            case DocCommandKind.ReplaceProjectSnapshot:
                ExecuteReplaceProjectSnapshot(project, OldProjectSnapshot);
                break;
        }
    }

    private DocTable? FindTable(DocProject project) => project.Tables.Find(t => t.Id == TableId);
    private static int FindTableVariantIndex(DocTable table, int variantId)
    {
        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            if (table.Variants[variantIndex].Id == variantId)
            {
                return variantIndex;
            }
        }

        return -1;
    }

    private static void ExecuteReplaceProjectSnapshot(DocProject project, DocProject? projectSnapshot)
    {
        if (projectSnapshot == null)
        {
            return;
        }

        project.Name = projectSnapshot.Name;
        project.UiState = CloneUiState(projectSnapshot.UiState);
        project.PluginSettingsByKey = new Dictionary<string, string>(projectSnapshot.PluginSettingsByKey, StringComparer.Ordinal);

        project.Folders.Clear();
        for (int folderIndex = 0; folderIndex < projectSnapshot.Folders.Count; folderIndex++)
        {
            project.Folders.Add(projectSnapshot.Folders[folderIndex].Clone());
        }

        project.Tables.Clear();
        for (int tableIndex = 0; tableIndex < projectSnapshot.Tables.Count; tableIndex++)
        {
            project.Tables.Add(CloneTable(projectSnapshot.Tables[tableIndex]));
        }

        project.Documents.Clear();
        for (int documentIndex = 0; documentIndex < projectSnapshot.Documents.Count; documentIndex++)
        {
            project.Documents.Add(CloneDocument(projectSnapshot.Documents[documentIndex]));
        }
    }

    private static DocProjectUiState CloneUiState(DocProjectUiState sourceUiState)
    {
        return new DocProjectUiState
        {
            TableFolderExpandedById = new Dictionary<string, bool>(sourceUiState.TableFolderExpandedById, StringComparer.Ordinal),
            DocumentFolderExpandedById = new Dictionary<string, bool>(sourceUiState.DocumentFolderExpandedById, StringComparer.Ordinal),
        };
    }

    private static DocTable CloneTable(DocTable sourceTable)
    {
        var clone = new DocTable
        {
            Id = sourceTable.Id,
            Name = sourceTable.Name,
            FolderId = sourceTable.FolderId,
            FileName = sourceTable.FileName,
            SchemaSourceTableId = sourceTable.SchemaSourceTableId,
            InheritanceSourceTableId = sourceTable.InheritanceSourceTableId,
            SystemKey = sourceTable.SystemKey,
            IsSystemSchemaLocked = sourceTable.IsSystemSchemaLocked,
            IsSystemDataLocked = sourceTable.IsSystemDataLocked,
            DerivedConfig = sourceTable.DerivedConfig?.Clone(),
            ExportConfig = sourceTable.ExportConfig?.Clone(),
            Keys = sourceTable.Keys.Clone(),
            ParentTableId = sourceTable.ParentTableId,
            ParentRowColumnId = sourceTable.ParentRowColumnId,
            PluginTableTypeId = sourceTable.PluginTableTypeId,
            PluginOwnerColumnTypeId = sourceTable.PluginOwnerColumnTypeId,
            IsPluginSchemaLocked = sourceTable.IsPluginSchemaLocked,
            Columns = new List<DocColumn>(sourceTable.Columns.Count),
            Rows = new List<DocRow>(sourceTable.Rows.Count),
            Views = new List<DocView>(sourceTable.Views.Count),
            Variables = new List<DocTableVariable>(sourceTable.Variables.Count),
            Variants = new List<DocTableVariant>(sourceTable.Variants.Count),
            VariantDeltas = new List<DocTableVariantDelta>(sourceTable.VariantDeltas.Count),
        };

        for (int columnIndex = 0; columnIndex < sourceTable.Columns.Count; columnIndex++)
        {
            clone.Columns.Add(CloneColumn(sourceTable.Columns[columnIndex]));
        }

        for (int rowIndex = 0; rowIndex < sourceTable.Rows.Count; rowIndex++)
        {
            clone.Rows.Add(CloneRow(sourceTable.Rows[rowIndex]));
        }

        for (int viewIndex = 0; viewIndex < sourceTable.Views.Count; viewIndex++)
        {
            clone.Views.Add(sourceTable.Views[viewIndex].Clone());
        }

        for (int variableIndex = 0; variableIndex < sourceTable.Variables.Count; variableIndex++)
        {
            clone.Variables.Add(sourceTable.Variables[variableIndex].Clone());
        }

        for (int variantIndex = 0; variantIndex < sourceTable.Variants.Count; variantIndex++)
        {
            clone.Variants.Add(sourceTable.Variants[variantIndex].Clone());
        }

        for (int variantDeltaIndex = 0; variantDeltaIndex < sourceTable.VariantDeltas.Count; variantDeltaIndex++)
        {
            clone.VariantDeltas.Add(sourceTable.VariantDeltas[variantDeltaIndex].Clone());
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

    private static DocRow CloneRow(DocRow sourceRow)
    {
        var clone = new DocRow
        {
            Id = sourceRow.Id,
            Cells = new Dictionary<string, DocCellValue>(sourceRow.Cells.Count),
        };

        foreach (var cellEntry in sourceRow.Cells)
        {
            clone.Cells[cellEntry.Key] = cellEntry.Value.Clone();
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

    private static void CleanViewColumnReferences(DocTable table, string columnId)
    {
        for (int vi = 0; vi < table.Views.Count; vi++)
        {
            var view = table.Views[vi];
            view.VisibleColumnIds?.RemoveAll(id => string.Equals(id, columnId, StringComparison.Ordinal));
            view.Filters.RemoveAll(f => string.Equals(f.ColumnId, columnId, StringComparison.Ordinal));
            view.Sorts.RemoveAll(s => string.Equals(s.ColumnId, columnId, StringComparison.Ordinal));
            if (string.Equals(view.GroupByColumnId, columnId, StringComparison.Ordinal))
                view.GroupByColumnId = null;
            if (string.Equals(view.CalendarDateColumnId, columnId, StringComparison.Ordinal))
                view.CalendarDateColumnId = null;
            if (string.Equals(view.ChartCategoryColumnId, columnId, StringComparison.Ordinal))
                view.ChartCategoryColumnId = null;
            if (string.Equals(view.ChartValueColumnId, columnId, StringComparison.Ordinal))
                view.ChartValueColumnId = null;
        }
    }

    private static void CleanViewVariableReferences(DocTable table, string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return;
        }

        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            var view = table.Views[viewIndex];
            var groupByBinding = view.GroupByColumnBinding;
            ClearBindingIfReferencesVariable(ref groupByBinding, variableName);
            view.GroupByColumnBinding = groupByBinding;

            var calendarDateBinding = view.CalendarDateColumnBinding;
            ClearBindingIfReferencesVariable(ref calendarDateBinding, variableName);
            view.CalendarDateColumnBinding = calendarDateBinding;

            var chartKindBinding = view.ChartKindBinding;
            ClearBindingIfReferencesVariable(ref chartKindBinding, variableName);
            view.ChartKindBinding = chartKindBinding;

            var chartCategoryBinding = view.ChartCategoryColumnBinding;
            ClearBindingIfReferencesVariable(ref chartCategoryBinding, variableName);
            view.ChartCategoryColumnBinding = chartCategoryBinding;

            var chartValueBinding = view.ChartValueColumnBinding;
            ClearBindingIfReferencesVariable(ref chartValueBinding, variableName);
            view.ChartValueColumnBinding = chartValueBinding;

            for (int sortIndex = 0; sortIndex < view.Sorts.Count; sortIndex++)
            {
                var sort = view.Sorts[sortIndex];
                var sortColumnBinding = sort.ColumnIdBinding;
                ClearBindingIfReferencesVariable(ref sortColumnBinding, variableName);
                sort.ColumnIdBinding = sortColumnBinding;

                var sortDescendingBinding = sort.DescendingBinding;
                ClearBindingIfReferencesVariable(ref sortDescendingBinding, variableName);
                sort.DescendingBinding = sortDescendingBinding;
            }

            for (int filterIndex = 0; filterIndex < view.Filters.Count; filterIndex++)
            {
                var filter = view.Filters[filterIndex];
                var filterColumnBinding = filter.ColumnIdBinding;
                ClearBindingIfReferencesVariable(ref filterColumnBinding, variableName);
                filter.ColumnIdBinding = filterColumnBinding;

                var filterOpBinding = filter.OpBinding;
                ClearBindingIfReferencesVariable(ref filterOpBinding, variableName);
                filter.OpBinding = filterOpBinding;

                var filterValueBinding = filter.ValueBinding;
                ClearBindingIfReferencesVariable(ref filterValueBinding, variableName);
                filter.ValueBinding = filterValueBinding;
            }
        }
    }

    private static void RenameViewVariableReferences(DocTable table, string oldVariableName, string newVariableName)
    {
        if (string.IsNullOrWhiteSpace(oldVariableName) ||
            string.IsNullOrWhiteSpace(newVariableName))
        {
            return;
        }

        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            var view = table.Views[viewIndex];
            var groupByBinding = view.GroupByColumnBinding;
            RenameBindingVariable(ref groupByBinding, oldVariableName, newVariableName);
            view.GroupByColumnBinding = groupByBinding;

            var calendarDateBinding = view.CalendarDateColumnBinding;
            RenameBindingVariable(ref calendarDateBinding, oldVariableName, newVariableName);
            view.CalendarDateColumnBinding = calendarDateBinding;

            var chartKindBinding = view.ChartKindBinding;
            RenameBindingVariable(ref chartKindBinding, oldVariableName, newVariableName);
            view.ChartKindBinding = chartKindBinding;

            var chartCategoryBinding = view.ChartCategoryColumnBinding;
            RenameBindingVariable(ref chartCategoryBinding, oldVariableName, newVariableName);
            view.ChartCategoryColumnBinding = chartCategoryBinding;

            var chartValueBinding = view.ChartValueColumnBinding;
            RenameBindingVariable(ref chartValueBinding, oldVariableName, newVariableName);
            view.ChartValueColumnBinding = chartValueBinding;

            for (int sortIndex = 0; sortIndex < view.Sorts.Count; sortIndex++)
            {
                var sort = view.Sorts[sortIndex];
                var sortColumnBinding = sort.ColumnIdBinding;
                RenameBindingVariable(ref sortColumnBinding, oldVariableName, newVariableName);
                sort.ColumnIdBinding = sortColumnBinding;

                var sortDescendingBinding = sort.DescendingBinding;
                RenameBindingVariable(ref sortDescendingBinding, oldVariableName, newVariableName);
                sort.DescendingBinding = sortDescendingBinding;
            }

            for (int filterIndex = 0; filterIndex < view.Filters.Count; filterIndex++)
            {
                var filter = view.Filters[filterIndex];
                var filterColumnBinding = filter.ColumnIdBinding;
                RenameBindingVariable(ref filterColumnBinding, oldVariableName, newVariableName);
                filter.ColumnIdBinding = filterColumnBinding;

                var filterOpBinding = filter.OpBinding;
                RenameBindingVariable(ref filterOpBinding, oldVariableName, newVariableName);
                filter.OpBinding = filterOpBinding;

                var filterValueBinding = filter.ValueBinding;
                RenameBindingVariable(ref filterValueBinding, oldVariableName, newVariableName);
                filter.ValueBinding = filterValueBinding;
            }
        }
    }

    private static void ClearBindingIfReferencesVariable(ref DocViewBinding? binding, string variableName)
    {
        if (binding == null)
        {
            return;
        }

        if (string.Equals(binding.VariableName, variableName, StringComparison.OrdinalIgnoreCase))
        {
            binding.VariableName = "";
        }

        if (binding.IsEmpty)
        {
            binding = null;
        }
    }

    private static void RenameBindingVariable(
        ref DocViewBinding? binding,
        string oldVariableName,
        string newVariableName)
    {
        if (binding == null)
        {
            return;
        }

        if (string.Equals(binding.VariableName, oldVariableName, StringComparison.OrdinalIgnoreCase))
        {
            binding.VariableName = newVariableName;
        }
    }

    private void ExecuteSetCell(DocProject project)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        DocCellValue normalizedNewCellValue = NormalizeCellValueForColumn(table, ColumnId, NewCellValue);

        if (TableVariantId == DocTableVariant.BaseVariantId)
        {
            var row = table.Rows.Find(rowCandidate => rowCandidate.Id == RowId);
            row?.SetCell(ColumnId, normalizedNewCellValue);
            return;
        }

        if (string.IsNullOrWhiteSpace(RowId) || string.IsNullOrWhiteSpace(ColumnId))
        {
            return;
        }

        DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, TableVariantId);
        int addedRowIndex = FindAddedVariantRowIndex(variantDelta, RowId);
        if (addedRowIndex >= 0)
        {
            variantDelta.AddedRows[addedRowIndex].SetCell(ColumnId, normalizedNewCellValue);
            return;
        }

        if (variantDelta.DeletedBaseRowIds.Contains(RowId))
        {
            return;
        }

        int baseRowIndex = FindBaseRowIndexById(table, RowId);
        if (baseRowIndex < 0)
        {
            return;
        }

        SetVariantCellOverride(variantDelta, RowId, ColumnId, normalizedNewCellValue);
    }

    private void UndoSetCell(DocProject project)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        DocCellValue normalizedOldCellValue = NormalizeCellValueForColumn(table, ColumnId, OldCellValue);

        if (TableVariantId == DocTableVariant.BaseVariantId)
        {
            var row = table.Rows.Find(rowCandidate => rowCandidate.Id == RowId);
            row?.SetCell(ColumnId, normalizedOldCellValue);
            return;
        }

        if (string.IsNullOrWhiteSpace(RowId) || string.IsNullOrWhiteSpace(ColumnId))
        {
            return;
        }

        DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, TableVariantId);
        int addedRowIndex = FindAddedVariantRowIndex(variantDelta, RowId);
        if (addedRowIndex >= 0)
        {
            variantDelta.AddedRows[addedRowIndex].SetCell(ColumnId, normalizedOldCellValue);
            return;
        }

        if (variantDelta.DeletedBaseRowIds.Contains(RowId))
        {
            return;
        }

        int baseRowIndex = FindBaseRowIndexById(table, RowId);
        if (baseRowIndex < 0)
        {
            return;
        }

        SetVariantCellOverride(variantDelta, RowId, ColumnId, normalizedOldCellValue);
    }

    private static DocCellValue NormalizeCellValueForColumn(DocTable table, string columnId, DocCellValue value)
    {
        if (string.IsNullOrWhiteSpace(columnId))
        {
            return value;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn candidateColumn = table.Columns[columnIndex];
            if (!string.Equals(candidateColumn.Id, columnId, StringComparison.Ordinal))
            {
                continue;
            }

            return DocCellValueNormalizer.NormalizeForColumn(candidateColumn, value);
        }

        return value;
    }

    private void ExecuteAddRow(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || RowSnapshot == null)
        {
            return;
        }

        EnsureAutoIdCellsForRow(table, RowSnapshot);

        if (TableVariantId == DocTableVariant.BaseVariantId)
        {
            table.Rows.Insert(RowIndex, RowSnapshot);
            return;
        }

        if (table.IsDerived)
        {
            return;
        }

        DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, TableVariantId);
        if (FindAddedVariantRowIndex(variantDelta, RowSnapshot.Id) >= 0)
        {
            return;
        }

        if (FindBaseRowIndexById(table, RowSnapshot.Id) >= 0)
        {
            return;
        }

        DocRow addedVariantRow = CloneRow(RowSnapshot);
        EnsureAutoIdCellsForRow(table, addedVariantRow);
        variantDelta.AddedRows.Add(addedVariantRow);
    }

    private void UndoAddRow(DocProject project)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        if (TableVariantId == DocTableVariant.BaseVariantId)
        {
            if (RowIndex < table.Rows.Count)
            {
                table.Rows.RemoveAt(RowIndex);
            }
            return;
        }

        if (RowSnapshot == null)
        {
            return;
        }

        DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, TableVariantId);
        int addedRowIndex = FindAddedVariantRowIndex(variantDelta, RowSnapshot.Id);
        if (addedRowIndex >= 0)
        {
            variantDelta.AddedRows.RemoveAt(addedRowIndex);
            RemoveVariantCellOverridesForRow(variantDelta, RowSnapshot.Id);
        }
    }

    private void ExecuteRemoveRow(DocProject project)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        if (TableVariantId == DocTableVariant.BaseVariantId)
        {
            if (RowIndex < table.Rows.Count)
            {
                table.Rows.RemoveAt(RowIndex);
            }
            return;
        }

        if (table.IsDerived)
        {
            return;
        }

        string rowId = !string.IsNullOrWhiteSpace(RowId)
            ? RowId
            : RowSnapshot?.Id ?? "";
        if (string.IsNullOrWhiteSpace(rowId))
        {
            return;
        }

        DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, TableVariantId);
        RemovedVariantCellOverrides?.Clear();
        int addedRowIndex = FindAddedVariantRowIndex(variantDelta, rowId);
        if (addedRowIndex >= 0)
        {
            var removedOverrides = RemovedVariantCellOverrides;
            CaptureVariantCellOverridesForRow(variantDelta, rowId, ref removedOverrides);
            RemovedVariantCellOverrides = removedOverrides;
            variantDelta.AddedRows.RemoveAt(addedRowIndex);
            RemoveVariantCellOverridesForRow(variantDelta, rowId);
            return;
        }

        if (FindBaseRowIndexById(table, rowId) >= 0)
        {
            if (!variantDelta.DeletedBaseRowIds.Contains(rowId))
            {
                variantDelta.DeletedBaseRowIds.Add(rowId);
            }

            var removedOverrides = RemovedVariantCellOverrides;
            CaptureVariantCellOverridesForRow(variantDelta, rowId, ref removedOverrides);
            RemovedVariantCellOverrides = removedOverrides;
            RemoveVariantCellOverridesForRow(variantDelta, rowId);
        }
    }

    private void UndoRemoveRow(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || RowSnapshot == null)
        {
            return;
        }

        if (TableVariantId == DocTableVariant.BaseVariantId)
        {
            table.Rows.Insert(RowIndex, RowSnapshot);
            return;
        }

        string rowId = RowSnapshot.Id;
        DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, TableVariantId);

        if (FindBaseRowIndexById(table, rowId) >= 0)
        {
            variantDelta.DeletedBaseRowIds.Remove(rowId);
            RestoreVariantCellOverrides(variantDelta, RemovedVariantCellOverrides);
            return;
        }

        if (FindAddedVariantRowIndex(variantDelta, rowId) >= 0)
        {
            return;
        }

        variantDelta.AddedRows.Add(CloneRow(RowSnapshot));
        RestoreVariantCellOverrides(variantDelta, RemovedVariantCellOverrides);
    }

    private void ExecuteMoveRow(DocProject project, int fromIndex, int toIndex)
    {
        var table = FindTable(project);
        if (table == null || fromIndex < 0 || fromIndex >= table.Rows.Count)
        {
            return;
        }

        var row = table.Rows[fromIndex];
        table.Rows.RemoveAt(fromIndex);

        int insertAt = toIndex > fromIndex ? toIndex - 1 : toIndex;
        insertAt = Math.Clamp(insertAt, 0, table.Rows.Count);
        table.Rows.Insert(insertAt, row);
    }

    private void UndoMoveRow(DocProject project)
    {
        int insertedIndex = TargetRowIndex > RowIndex ? TargetRowIndex - 1 : TargetRowIndex;
        int undoTargetIndex = RowIndex > insertedIndex ? RowIndex + 1 : RowIndex;
        ExecuteMoveRow(project, insertedIndex, undoTargetIndex);
    }

    private static DocTableVariantDelta GetOrCreateVariantDelta(DocTable table, int variantId)
    {
        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta candidateDelta = table.VariantDeltas[deltaIndex];
            if (candidateDelta.VariantId == variantId)
            {
                return candidateDelta;
            }
        }

        var createdDelta = new DocTableVariantDelta
        {
            VariantId = variantId,
        };
        table.VariantDeltas.Add(createdDelta);
        return createdDelta;
    }

    private static int FindAddedVariantRowIndex(DocTableVariantDelta variantDelta, string rowId)
    {
        for (int rowIndex = 0; rowIndex < variantDelta.AddedRows.Count; rowIndex++)
        {
            if (string.Equals(variantDelta.AddedRows[rowIndex].Id, rowId, StringComparison.Ordinal))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static int FindBaseRowIndexById(DocTable table, string rowId)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            if (string.Equals(table.Rows[rowIndex].Id, rowId, StringComparison.Ordinal))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static void SetVariantCellOverride(
        DocTableVariantDelta variantDelta,
        string rowId,
        string columnId,
        DocCellValue value)
    {
        int existingOverrideIndex = FindVariantCellOverrideIndex(variantDelta, rowId, columnId);
        if (existingOverrideIndex >= 0)
        {
            variantDelta.CellOverrides[existingOverrideIndex].Value = value.Clone();
            return;
        }

        variantDelta.CellOverrides.Add(new DocTableCellOverride
        {
            RowId = rowId,
            ColumnId = columnId,
            Value = value.Clone(),
        });
    }

    private static void EnsureAutoIdCellsForRow(DocTable table, DocRow row)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Id)
            {
                continue;
            }

            string existingValue = row.GetCell(column).StringValue ?? "";
            if (!string.IsNullOrWhiteSpace(existingValue))
            {
                continue;
            }

            row.SetCell(column.Id, DocCellValue.Text(row.Id));
        }
    }

    private static int FindVariantCellOverrideIndex(
        DocTableVariantDelta variantDelta,
        string rowId,
        string columnId)
    {
        for (int overrideIndex = 0; overrideIndex < variantDelta.CellOverrides.Count; overrideIndex++)
        {
            DocTableCellOverride cellOverride = variantDelta.CellOverrides[overrideIndex];
            if (string.Equals(cellOverride.RowId, rowId, StringComparison.Ordinal) &&
                string.Equals(cellOverride.ColumnId, columnId, StringComparison.Ordinal))
            {
                return overrideIndex;
            }
        }

        return -1;
    }

    private static void RemoveVariantCellOverridesForRow(DocTableVariantDelta variantDelta, string rowId)
    {
        for (int overrideIndex = variantDelta.CellOverrides.Count - 1; overrideIndex >= 0; overrideIndex--)
        {
            if (string.Equals(variantDelta.CellOverrides[overrideIndex].RowId, rowId, StringComparison.Ordinal))
            {
                variantDelta.CellOverrides.RemoveAt(overrideIndex);
            }
        }
    }

    private static void CaptureVariantCellOverridesForRow(
        DocTableVariantDelta variantDelta,
        string rowId,
        ref List<DocTableCellOverride>? outOverrides)
    {
        if (outOverrides == null)
        {
            outOverrides = new List<DocTableCellOverride>(4);
        }

        for (int overrideIndex = 0; overrideIndex < variantDelta.CellOverrides.Count; overrideIndex++)
        {
            DocTableCellOverride cellOverride = variantDelta.CellOverrides[overrideIndex];
            if (string.Equals(cellOverride.RowId, rowId, StringComparison.Ordinal))
            {
                outOverrides.Add(cellOverride.Clone());
            }
        }
    }

    private static void RestoreVariantCellOverrides(
        DocTableVariantDelta variantDelta,
        List<DocTableCellOverride>? overrides)
    {
        if (overrides == null || overrides.Count <= 0)
        {
            return;
        }

        for (int overrideIndex = 0; overrideIndex < overrides.Count; overrideIndex++)
        {
            DocTableCellOverride cellOverride = overrides[overrideIndex];
            SetVariantCellOverride(variantDelta, cellOverride.RowId, cellOverride.ColumnId, cellOverride.Value);
        }
    }

    private void ExecuteAddColumn(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || ColumnSnapshot == null) return;

        table.Columns.Insert(ColumnIndex, ColumnSnapshot);

        // Add default cells to all rows
        foreach (var row in table.Rows)
        {
            if (ColumnSnapshot.Kind == DocColumnKind.Id)
            {
                row.SetCell(ColumnSnapshot.Id, DocCellValue.Text(row.Id));
            }
            else
            {
                row.SetCell(ColumnSnapshot.Id, DocCellValue.Default(ColumnSnapshot));
            }
        }
    }

    private void UndoAddColumn(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || ColumnSnapshot == null) return;

        if (ColumnIndex < table.Columns.Count)
            table.Columns.RemoveAt(ColumnIndex);

        // Remove cells from all rows
        foreach (var row in table.Rows)
            row.Cells.Remove(ColumnSnapshot.Id);
    }

    private void ExecuteRemoveColumn(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || ColumnSnapshot == null) return;

        if (ColumnIndex < table.Columns.Count)
            table.Columns.RemoveAt(ColumnIndex);

        foreach (var row in table.Rows)
            row.Cells.Remove(ColumnSnapshot.Id);

        // Clean up view references to the removed column
        CleanViewColumnReferences(table, ColumnSnapshot.Id);
    }

    private void UndoRemoveColumn(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || ColumnSnapshot == null) return;

        table.Columns.Insert(ColumnIndex, ColumnSnapshot);

        // Restore cell values
        if (ColumnCellSnapshots != null)
        {
            foreach (var row in table.Rows)
            {
                if (ColumnCellSnapshots.TryGetValue(row.Id, out var cellValue))
                    row.SetCell(ColumnSnapshot.Id, cellValue);
            }
        }
    }

    private void ExecuteAddTable(DocProject project)
    {
        if (TableSnapshot == null)
        {
            return;
        }

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            if (string.Equals(project.Tables[tableIndex].Id, TableSnapshot.Id, StringComparison.Ordinal))
            {
                return;
            }
        }

        int insertIndex = Math.Clamp(TableIndex, 0, project.Tables.Count);
        project.Tables.Insert(insertIndex, TableSnapshot);
    }

    private void UndoAddTable(DocProject project)
    {
        if (TableSnapshot == null || project.Tables.Count <= 0)
        {
            return;
        }

        if (TableIndex >= 0 &&
            TableIndex < project.Tables.Count &&
            string.Equals(project.Tables[TableIndex].Id, TableSnapshot.Id, StringComparison.Ordinal))
        {
            project.Tables.RemoveAt(TableIndex);
            return;
        }

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            if (string.Equals(project.Tables[tableIndex].Id, TableSnapshot.Id, StringComparison.Ordinal))
            {
                project.Tables.RemoveAt(tableIndex);
                return;
            }
        }
    }

    private void ExecuteRemoveTable(DocProject project)
    {
        if (TableIndex < project.Tables.Count)
            project.Tables.RemoveAt(TableIndex);
    }

    private void UndoRemoveTable(DocProject project)
    {
        if (TableSnapshot != null)
            project.Tables.Insert(TableIndex, TableSnapshot);
    }

    private void ExecuteRenameTable(DocProject project)
    {
        var table = FindTable(project);
        if (table != null) table.Name = NewName;
    }

    private void UndoRenameTable(DocProject project)
    {
        var table = FindTable(project);
        if (table != null) table.Name = OldName;
    }

    private void ExecuteRenameColumn(DocProject project)
    {
        var table = FindTable(project);
        var col = table?.Columns.Find(c => c.Id == ColumnId);
        if (col != null) col.Name = NewName;
    }

    private void UndoRenameColumn(DocProject project)
    {
        var table = FindTable(project);
        var col = table?.Columns.Find(c => c.Id == ColumnId);
        if (col != null) col.Name = OldName;
    }

    private void ExecuteSetColumnWidth(DocProject project, float width)
    {
        var table = FindTable(project);
        var col = table?.Columns.Find(c => c.Id == ColumnId);
        if (col == null)
        {
            return;
        }

        col.Width = width;
    }

    private void ExecuteMoveColumn(DocProject project, int fromIndex, int toIndex)
    {
        var table = FindTable(project);
        if (table == null || fromIndex < 0 || fromIndex >= table.Columns.Count)
        {
            return;
        }

        var column = table.Columns[fromIndex];
        table.Columns.RemoveAt(fromIndex);

        int insertAt = toIndex > fromIndex ? toIndex - 1 : toIndex;
        insertAt = Math.Clamp(insertAt, 0, table.Columns.Count);
        table.Columns.Insert(insertAt, column);

        if (table.IsDerived && table.DerivedConfig != null)
        {
            ReorderDerivedProjectionsFromColumnOrder(table);
        }
    }

    private void UndoMoveColumn(DocProject project)
    {
        int insertedIndex = TargetColumnIndex > ColumnIndex ? TargetColumnIndex - 1 : TargetColumnIndex;
        int undoTargetIndex = ColumnIndex > insertedIndex ? ColumnIndex + 1 : ColumnIndex;
        ExecuteMoveColumn(project, insertedIndex, undoTargetIndex);
    }

    private void ExecuteSetColumnFormula(DocProject project, string formulaExpression)
    {
        var table = FindTable(project);
        var col = table?.Columns.Find(c => c.Id == ColumnId);
        if (col == null)
        {
            return;
        }

        col.FormulaExpression = formulaExpression;
    }

    private void ExecuteSetColumnPluginSettings(DocProject project, string? pluginSettingsJson)
    {
        var table = FindTable(project);
        var col = table?.Columns.Find(c => c.Id == ColumnId);
        if (col == null)
        {
            return;
        }

        col.PluginSettingsJson = string.IsNullOrWhiteSpace(pluginSettingsJson)
            ? null
            : pluginSettingsJson;
    }

    private void ExecuteSetColumnRelation(
        DocProject project,
        string? relationTableId,
        DocRelationTargetMode relationTargetMode,
        int relationTableVariantId,
        string? relationDisplayColumnId)
    {
        var table = FindTable(project);
        var col = table?.Columns.Find(c => c.Id == ColumnId);
        if (col == null)
        {
            return;
        }

        col.RelationTargetMode = relationTargetMode;
        string? resolvedTargetTableId = DocRelationTargetResolver.ResolveTargetTableId(
            table!,
            relationTargetMode,
            relationTableId);
        col.RelationTableId = resolvedTargetTableId;
        col.RelationTableVariantId = resolvedTargetTableId == null ? 0 : relationTableVariantId;
        col.RelationDisplayColumnId = relationDisplayColumnId;
    }

    private void ExecuteSetColumnOptions(DocProject project, List<string>? optionsSnapshot)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (!string.Equals(column.Id, ColumnId, StringComparison.Ordinal))
            {
                continue;
            }

            column.Options = optionsSnapshot == null
                ? null
                : new List<string>(optionsSnapshot);
            return;
        }
    }

    private void ExecuteSetColumnModelPreview(DocProject project, DocModelPreviewSettings? modelPreviewSettings)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (!string.Equals(column.Id, ColumnId, StringComparison.Ordinal))
            {
                continue;
            }

            column.ModelPreviewSettings = modelPreviewSettings?.Clone();
            return;
        }
    }

    private void ExecuteSetColumnHidden(DocProject project, bool hidden)
    {
        var table = FindTable(project);
        var col = table?.Columns.Find(c => c.Id == ColumnId);
        if (col == null)
        {
            return;
        }

        col.IsHidden = hidden;
    }

    private void ExecuteSetColumnExportIgnore(DocProject project, bool exportIgnore)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (string.Equals(col.Id, ColumnId, StringComparison.Ordinal))
            {
                col.ExportIgnore = exportIgnore;
                return;
            }
        }
    }

    private void ExecuteSetColumnExportType(DocProject project, string? exportType)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (string.Equals(col.Id, ColumnId, StringComparison.Ordinal))
            {
                col.ExportType = exportType;
                return;
            }
        }
    }

    private void ExecuteSetColumnNumberSettings(
        DocProject project,
        string? exportType,
        double? numberMin,
        double? numberMax,
        Dictionary<string, double>? numberValuesByRowId)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        DocColumn? column = null;
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var candidateColumn = table.Columns[columnIndex];
            if (string.Equals(candidateColumn.Id, ColumnId, StringComparison.Ordinal))
            {
                column = candidateColumn;
                break;
            }
        }

        if (column == null)
        {
            return;
        }

        column.ExportType = exportType;
        column.NumberMin = numberMin;
        column.NumberMax = numberMax;

        if (numberValuesByRowId == null || numberValuesByRowId.Count <= 0)
        {
            return;
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (!numberValuesByRowId.TryGetValue(row.Id, out double mappedValue))
            {
                continue;
            }

            row.SetCell(column.Id, DocCellValue.Number(mappedValue));
        }
    }

    private void ExecuteSetColumnExportEnumName(DocProject project, string? exportEnumName)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (string.Equals(col.Id, ColumnId, StringComparison.Ordinal))
            {
                col.ExportEnumName = exportEnumName;
                return;
            }
        }
    }

    private void ExecuteSetColumnSubtableDisplay(
        DocProject project,
        string? subtableDisplayRendererId,
        float? subtableDisplayCellWidth,
        float? subtableDisplayCellHeight,
        string? subtableDisplayPluginSettingsJson,
        DocSubtablePreviewQuality? subtableDisplayPreviewQuality)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (!string.Equals(column.Id, ColumnId, StringComparison.Ordinal))
            {
                continue;
            }

            column.SubtableDisplayRendererId = string.IsNullOrWhiteSpace(subtableDisplayRendererId)
                ? null
                : subtableDisplayRendererId;
            column.SubtableDisplayCellWidth = subtableDisplayCellWidth;
            column.SubtableDisplayCellHeight = subtableDisplayCellHeight;
            column.SubtableDisplayPreviewQuality = subtableDisplayPreviewQuality;
            column.PluginSettingsJson = string.IsNullOrWhiteSpace(subtableDisplayPluginSettingsJson)
                ? null
                : subtableDisplayPluginSettingsJson;
            return;
        }
    }

    private void ExecuteSetTableExportConfig(DocProject project, DocTableExportConfig? exportConfigSnapshot)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        table.ExportConfig = exportConfigSnapshot?.Clone();
    }

    private void ExecuteSetTableKeys(DocProject project, DocTableKeys? keysSnapshot)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        table.Keys = keysSnapshot?.Clone() ?? new DocTableKeys();
    }

    private void ExecuteSetTableSchemaSource(DocProject project, string? schemaSourceTableId)
    {
        var table = FindTable(project);
        if (table == null || table.IsDerived)
        {
            return;
        }

        table.SchemaSourceTableId = string.IsNullOrWhiteSpace(schemaSourceTableId)
            ? null
            : schemaSourceTableId;
        if (!string.IsNullOrWhiteSpace(table.SchemaSourceTableId))
        {
            table.InheritanceSourceTableId = null;
        }
    }

    private void ExecuteSetTableInheritanceSource(DocProject project, string? inheritanceSourceTableId)
    {
        var table = FindTable(project);
        if (table == null || table.IsDerived)
        {
            return;
        }

        table.InheritanceSourceTableId = string.IsNullOrWhiteSpace(inheritanceSourceTableId)
            ? null
            : inheritanceSourceTableId;
        if (!string.IsNullOrWhiteSpace(table.InheritanceSourceTableId))
        {
            table.SchemaSourceTableId = null;
        }
    }

    // --- Derived table helpers ---

    private void ExecuteSetDerivedConfig(DocProject project, DocDerivedConfig? config)
    {
        var table = FindTable(project);
        if (table != null)
            table.DerivedConfig = config?.Clone();
    }

    private void ExecuteSetDerivedBaseTable(DocProject project, string? baseTableId)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null)
            table.DerivedConfig.BaseTableId = baseTableId;
    }

    private void ExecuteAddDerivedStep(DocProject project)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && StepSnapshot != null)
            table.DerivedConfig.Steps.Insert(StepIndex, StepSnapshot.Clone());
    }

    private void UndoAddDerivedStep(DocProject project)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && StepIndex < table.DerivedConfig.Steps.Count)
            table.DerivedConfig.Steps.RemoveAt(StepIndex);
    }

    private void ExecuteRemoveDerivedStep(DocProject project)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && StepIndex < table.DerivedConfig.Steps.Count)
            table.DerivedConfig.Steps.RemoveAt(StepIndex);
    }

    private void UndoRemoveDerivedStep(DocProject project)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && StepSnapshot != null)
            table.DerivedConfig.Steps.Insert(StepIndex, StepSnapshot.Clone());
    }

    private void ExecuteUpdateDerivedStep(DocProject project, DerivedStep? snapshot)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && snapshot != null && StepIndex < table.DerivedConfig.Steps.Count)
            table.DerivedConfig.Steps[StepIndex] = snapshot.Clone();
    }

    private void ExecuteReorderDerivedStep(DocProject project, int fromIndex, int toIndex)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig == null) return;
        var steps = table.DerivedConfig.Steps;
        if (fromIndex < 0 || fromIndex >= steps.Count) return;

        var item = steps[fromIndex];
        steps.RemoveAt(fromIndex);
        int insertAt = toIndex > fromIndex ? toIndex - 1 : toIndex;
        insertAt = Math.Clamp(insertAt, 0, steps.Count);
        steps.Insert(insertAt, item);
    }

    private void ExecuteAddDerivedProjection(DocProject project)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && ProjectionSnapshot != null)
        {
            // Explicit adds override suppression.
            RemoveDerivedProjectionSuppression(table.DerivedConfig, ProjectionSnapshot.SourceTableId, ProjectionSnapshot.SourceColumnId);

            table.DerivedConfig.Projections.Insert(ProjectionIndex, ProjectionSnapshot.Clone());

            // Add the projected column to the table if it doesn't already exist
            if (!string.IsNullOrEmpty(ProjectionSnapshot.OutputColumnId) &&
                table.Columns.Find(c => c.Id == ProjectionSnapshot.OutputColumnId) == null)
            {
                table.Columns.Add(ColumnSnapshot ?? CreateProjectedColumn(ProjectionSnapshot));
            }
        }
    }

    private void UndoAddDerivedProjection(DocProject project)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && ProjectionIndex < table.DerivedConfig.Projections.Count)
        {
            var proj = table.DerivedConfig.Projections[ProjectionIndex];
            table.DerivedConfig.Projections.RemoveAt(ProjectionIndex);

            // Remove the projected column
            int colIdx = table.Columns.FindIndex(c => c.Id == proj.OutputColumnId);
            if (colIdx >= 0) table.Columns.RemoveAt(colIdx);
        }
    }

    private void ExecuteRemoveDerivedProjection(DocProject project)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && ProjectionIndex < table.DerivedConfig.Projections.Count)
        {
            var proj = table.DerivedConfig.Projections[ProjectionIndex];
            table.DerivedConfig.Projections.RemoveAt(ProjectionIndex);

            // Remove the projected column
            int colIdx = table.Columns.FindIndex(c => c.Id == proj.OutputColumnId);
            if (colIdx >= 0) table.Columns.RemoveAt(colIdx);

            // Preserve user intent: once removed, keep it removed even if auto-project runs again.
            AddDerivedProjectionSuppression(table.DerivedConfig, proj.SourceTableId, proj.SourceColumnId, proj.OutputColumnId);
        }
    }

    private void UndoRemoveDerivedProjection(DocProject project)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && ProjectionSnapshot != null)
        {
            table.DerivedConfig.Projections.Insert(ProjectionIndex, ProjectionSnapshot.Clone());

            if (!string.IsNullOrEmpty(ProjectionSnapshot.OutputColumnId) &&
                table.Columns.Find(c => c.Id == ProjectionSnapshot.OutputColumnId) == null)
            {
                table.Columns.Add(ColumnSnapshot ?? CreateProjectedColumn(ProjectionSnapshot));
            }

            RemoveDerivedProjectionSuppression(table.DerivedConfig, ProjectionSnapshot.SourceTableId, ProjectionSnapshot.SourceColumnId);
        }
    }

    private void ExecuteUpdateDerivedProjection(DocProject project, DerivedProjection? snapshot)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig != null && snapshot != null && ProjectionIndex < table.DerivedConfig.Projections.Count)
        {
            table.DerivedConfig.Projections[ProjectionIndex] = snapshot.Clone();

            // Keep the projected column name synchronized with rename alias changes.
            var col = table.Columns.Find(c => c.Id == snapshot.OutputColumnId);
            if (col != null)
            {
                if (!string.IsNullOrEmpty(snapshot.RenameAlias))
                {
                    col.Name = snapshot.RenameAlias;
                }
                else
                {
                    col.Name = ResolveDerivedProjectionSourceName(project, snapshot, col.Name);
                }
            }
        }
    }

    private void ExecuteReorderDerivedProjection(DocProject project, int fromIndex, int toIndex)
    {
        var table = FindTable(project);
        if (table?.DerivedConfig == null) return;
        var projections = table.DerivedConfig.Projections;
        if (fromIndex < 0 || fromIndex >= projections.Count) return;

        var item = projections[fromIndex];
        projections.RemoveAt(fromIndex);
        int insertAt = toIndex > fromIndex ? toIndex - 1 : toIndex;
        insertAt = Math.Clamp(insertAt, 0, projections.Count);
        projections.Insert(insertAt, item);
    }

    private static DocColumn CreateProjectedColumn(DerivedProjection proj)
    {
        return new DocColumn
        {
            Id = proj.OutputColumnId,
            Name = string.IsNullOrEmpty(proj.RenameAlias) ? "Projected" : proj.RenameAlias,
            Kind = DocColumnKind.Text,
            ColumnTypeId = Derp.Doc.Plugins.DocColumnTypeIds.Text,
            IsProjected = true,
        };
    }

    private static void ReorderDerivedProjectionsFromColumnOrder(DocTable table)
    {
        var config = table.DerivedConfig;
        if (config == null || config.Projections.Count <= 1)
        {
            return;
        }

        var reordered = new List<DerivedProjection>(config.Projections.Count);

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (!column.IsProjected)
            {
                continue;
            }

            for (int projectionIndex = 0; projectionIndex < config.Projections.Count; projectionIndex++)
            {
                var projection = config.Projections[projectionIndex];
                if (string.Equals(projection.OutputColumnId, column.Id, StringComparison.Ordinal))
                {
                    reordered.Add(projection);
                    break;
                }
            }
        }

        if (reordered.Count != config.Projections.Count)
        {
            return;
        }

        config.Projections.Clear();
        config.Projections.AddRange(reordered);
    }

    private static void AddDerivedProjectionSuppression(DocDerivedConfig config, string sourceTableId, string sourceColumnId, string outputColumnId)
    {
        if (string.IsNullOrEmpty(sourceTableId) || string.IsNullOrEmpty(sourceColumnId))
        {
            return;
        }

        for (int i = 0; i < config.SuppressedProjections.Count; i++)
        {
            var sup = config.SuppressedProjections[i];
            if (string.Equals(sup.SourceTableId, sourceTableId, StringComparison.Ordinal) &&
                string.Equals(sup.SourceColumnId, sourceColumnId, StringComparison.Ordinal))
            {
                return;
            }
        }

        config.SuppressedProjections.Add(new DerivedProjectionSuppression
        {
            SourceTableId = sourceTableId,
            SourceColumnId = sourceColumnId,
            OutputColumnId = string.IsNullOrEmpty(outputColumnId) ? "" : outputColumnId,
        });
    }

    private static void RemoveDerivedProjectionSuppression(DocDerivedConfig config, string sourceTableId, string sourceColumnId)
    {
        if (string.IsNullOrEmpty(sourceTableId) || string.IsNullOrEmpty(sourceColumnId))
        {
            return;
        }

        for (int i = 0; i < config.SuppressedProjections.Count; i++)
        {
            var sup = config.SuppressedProjections[i];
            if (string.Equals(sup.SourceTableId, sourceTableId, StringComparison.Ordinal) &&
                string.Equals(sup.SourceColumnId, sourceColumnId, StringComparison.Ordinal))
            {
                config.SuppressedProjections.RemoveAt(i);
                return;
            }
        }
    }

    private static string ResolveDerivedProjectionSourceName(
        DocProject project,
        DerivedProjection projection,
        string fallbackName)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var sourceTable = project.Tables[tableIndex];
            if (!string.Equals(sourceTable.Id, projection.SourceTableId, StringComparison.Ordinal))
            {
                continue;
            }

            for (int columnIndex = 0; columnIndex < sourceTable.Columns.Count; columnIndex++)
            {
                var sourceColumn = sourceTable.Columns[columnIndex];
                if (string.Equals(sourceColumn.Id, projection.SourceColumnId, StringComparison.Ordinal))
                {
                    return sourceColumn.Name;
                }
            }
        }

        return fallbackName;
    }

    // --- View helpers (Phase 6) ---

    private void ExecuteAddView(DocProject project)
    {
        var table = FindTable(project);
        if (table != null && ViewSnapshot != null)
            table.Views.Insert(ViewIndex, ViewSnapshot.Clone());
    }

    private void UndoAddView(DocProject project)
    {
        var table = FindTable(project);
        if (table != null && ViewIndex < table.Views.Count)
            table.Views.RemoveAt(ViewIndex);
    }

    private void ExecuteRemoveView(DocProject project)
    {
        var table = FindTable(project);
        if (table != null && ViewIndex < table.Views.Count)
            table.Views.RemoveAt(ViewIndex);
    }

    private void UndoRemoveView(DocProject project)
    {
        var table = FindTable(project);
        if (table != null && ViewSnapshot != null)
            table.Views.Insert(ViewIndex, ViewSnapshot.Clone());
    }

    private void ExecuteRenameView(DocProject project, string name)
    {
        var table = FindTable(project);
        if (table == null) return;

        for (int i = 0; i < table.Views.Count; i++)
        {
            if (string.Equals(table.Views[i].Id, ViewId, StringComparison.Ordinal))
            {
                table.Views[i].Name = name;
                return;
            }
        }
    }

    private void ExecuteUpdateViewConfig(DocProject project, DocView? snapshot)
    {
        var table = FindTable(project);
        if (table == null || snapshot == null) return;

        for (int i = 0; i < table.Views.Count; i++)
        {
            if (string.Equals(table.Views[i].Id, ViewId, StringComparison.Ordinal))
            {
                table.Views[i] = snapshot.Clone();
                return;
            }
        }
    }

    private void ExecuteAddTableVariant(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || TableVariantSnapshot == null)
        {
            return;
        }

        int insertIndex = TableVariantIndex;
        if (insertIndex < 0 || insertIndex > table.Variants.Count)
        {
            insertIndex = table.Variants.Count;
        }

        table.Variants.Insert(insertIndex, TableVariantSnapshot.Clone());
    }

    private void UndoAddTableVariant(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || TableVariantSnapshot == null)
        {
            return;
        }

        int variantIndex = FindTableVariantIndex(table, TableVariantSnapshot.Id);
        if (variantIndex >= 0)
        {
            table.Variants.RemoveAt(variantIndex);
        }
    }

    private void ExecuteAddTableVariable(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || TableVariableSnapshot == null)
        {
            return;
        }

        int insertIndex = TableVariableIndex;
        if (insertIndex < 0 || insertIndex > table.Variables.Count)
        {
            insertIndex = table.Variables.Count;
        }

        table.Variables.Insert(insertIndex, TableVariableSnapshot.Clone());
    }

    private void UndoAddTableVariable(DocProject project)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            if (string.Equals(table.Variables[variableIndex].Id, TableVariableId, StringComparison.Ordinal))
            {
                table.Variables.RemoveAt(variableIndex);
                return;
            }
        }

        if (TableVariableIndex >= 0 && TableVariableIndex < table.Variables.Count)
        {
            table.Variables.RemoveAt(TableVariableIndex);
        }
    }

    private void ExecuteRemoveTableVariable(DocProject project)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        string removedVariableName = "";
        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            if (!string.Equals(table.Variables[variableIndex].Id, TableVariableId, StringComparison.Ordinal))
            {
                continue;
            }

            removedVariableName = table.Variables[variableIndex].Name;
            table.Variables.RemoveAt(variableIndex);
            break;
        }

        if (!string.IsNullOrWhiteSpace(removedVariableName))
        {
            CleanViewVariableReferences(table, removedVariableName);
        }
    }

    private void UndoRemoveTableVariable(DocProject project)
    {
        var table = FindTable(project);
        if (table == null || TableVariableSnapshot == null)
        {
            return;
        }

        int insertIndex = TableVariableIndex;
        if (insertIndex < 0 || insertIndex > table.Variables.Count)
        {
            insertIndex = table.Variables.Count;
        }

        table.Variables.Insert(insertIndex, TableVariableSnapshot.Clone());
    }

    private void ExecuteRenameTableVariable(DocProject project, string variableName)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            if (!string.Equals(tableVariable.Id, TableVariableId, StringComparison.Ordinal))
            {
                continue;
            }

            string previousName = tableVariable.Name;
            tableVariable.Name = variableName;
            RenameViewVariableReferences(table, previousName, variableName);
            return;
        }
    }

    private void ExecuteSetTableVariableExpression(DocProject project, string expression)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            if (string.Equals(tableVariable.Id, TableVariableId, StringComparison.Ordinal))
            {
                tableVariable.Expression = expression;
                return;
            }
        }
    }

    private void ExecuteSetTableVariableType(DocProject project, DocColumnKind variableKind, string? variableTypeId)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            if (string.Equals(tableVariable.Id, TableVariableId, StringComparison.Ordinal))
            {
                tableVariable.Kind = variableKind;
                tableVariable.ColumnTypeId = variableTypeId ?? "";
                return;
            }
        }
    }

    // --- Folder helpers ---

    private DocFolder? FindFolder(DocProject project)
    {
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var folder = project.Folders[folderIndex];
            if (string.Equals(folder.Id, FolderId, StringComparison.Ordinal))
            {
                return folder;
            }
        }

        return null;
    }

    private int FindFolderIndex(DocProject project)
    {
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            if (string.Equals(project.Folders[folderIndex].Id, FolderId, StringComparison.Ordinal))
            {
                return folderIndex;
            }
        }

        return -1;
    }

    private void ExecuteAddFolder(DocProject project)
    {
        if (FolderSnapshot == null)
        {
            return;
        }

        int insertIndex = FolderIndex;
        if (insertIndex < 0 || insertIndex > project.Folders.Count)
        {
            insertIndex = project.Folders.Count;
        }

        project.Folders.Insert(insertIndex, FolderSnapshot.Clone());
    }

    private void UndoAddFolder(DocProject project)
    {
        int folderIndex = FindFolderIndex(project);
        if (folderIndex < 0)
        {
            return;
        }

        project.Folders.RemoveAt(folderIndex);
    }

    private void ExecuteRemoveFolder(DocProject project)
    {
        int folderIndex = FindFolderIndex(project);
        if (folderIndex < 0)
        {
            return;
        }

        project.Folders.RemoveAt(folderIndex);
    }

    private void UndoRemoveFolder(DocProject project)
    {
        if (FolderSnapshot == null)
        {
            return;
        }

        int insertIndex = FolderIndex;
        if (insertIndex < 0 || insertIndex > project.Folders.Count)
        {
            insertIndex = project.Folders.Count;
        }

        project.Folders.Insert(insertIndex, FolderSnapshot.Clone());
    }

    private void ExecuteRenameFolder(DocProject project, string name)
    {
        var folder = FindFolder(project);
        if (folder == null)
        {
            return;
        }

        folder.Name = name;
    }

    private void ExecuteMoveFolder(DocProject project, string? newParentFolderId)
    {
        var folder = FindFolder(project);
        if (folder == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(newParentFolderId))
        {
            if (string.Equals(newParentFolderId, folder.Id, StringComparison.Ordinal))
            {
                return;
            }

            DocFolder? newParentFolder = null;
            for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
            {
                var candidateFolder = project.Folders[folderIndex];
                if (string.Equals(candidateFolder.Id, newParentFolderId, StringComparison.Ordinal))
                {
                    newParentFolder = candidateFolder;
                    break;
                }
            }

            if (newParentFolder == null)
            {
                return;
            }

            if (newParentFolder.Scope != folder.Scope)
            {
                return;
            }

            if (IsDescendantFolder(project, folder.Id, newParentFolderId))
            {
                return;
            }
        }

        folder.ParentFolderId = string.IsNullOrWhiteSpace(newParentFolderId) ? null : newParentFolderId;
    }

    private static bool IsDescendantFolder(DocProject project, string ancestorFolderId, string candidateDescendantFolderId)
    {
        string? currentFolderId = candidateDescendantFolderId;
        while (!string.IsNullOrWhiteSpace(currentFolderId))
        {
            if (string.Equals(currentFolderId, ancestorFolderId, StringComparison.Ordinal))
            {
                return true;
            }

            string? parentFolderId = null;
            for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
            {
                var folder = project.Folders[folderIndex];
                if (string.Equals(folder.Id, currentFolderId, StringComparison.Ordinal))
                {
                    parentFolderId = folder.ParentFolderId;
                    break;
                }
            }

            currentFolderId = parentFolderId;
        }

        return false;
    }

    private void ExecuteSetTableFolder(DocProject project, string? folderId)
    {
        var table = FindTable(project);
        if (table == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            DocFolder? folder = null;
            for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
            {
                var candidateFolder = project.Folders[folderIndex];
                if (string.Equals(candidateFolder.Id, folderId, StringComparison.Ordinal))
                {
                    folder = candidateFolder;
                    break;
                }
            }

            if (folder == null || folder.Scope != DocFolderScope.Tables)
            {
                return;
            }
        }

        table.FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
    }

    private void ExecuteSetDocumentFolder(DocProject project, string? folderId)
    {
        var document = FindDocument(project);
        if (document == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            DocFolder? folder = null;
            for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
            {
                var candidateFolder = project.Folders[folderIndex];
                if (string.Equals(candidateFolder.Id, folderId, StringComparison.Ordinal))
                {
                    folder = candidateFolder;
                    break;
                }
            }

            if (folder == null || folder.Scope != DocFolderScope.Documents)
            {
                return;
            }
        }

        document.FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
    }

    // --- Document helpers ---

    private DocDocument? FindDocument(DocProject project) => project.Documents.Find(d => d.Id == DocumentId);
    private DocBlock? FindBlock(DocProject project)
    {
        var doc = FindDocument(project);
        return doc?.Blocks.Find(b => b.Id == BlockId);
    }

    private void ExecuteAddDocument(DocProject project)
    {
        if (DocumentSnapshot != null)
        {
            NormalizeDocumentBlockOrders(DocumentSnapshot);
            project.Documents.Insert(DocumentIndex, DocumentSnapshot);
        }
    }

    private void UndoAddDocument(DocProject project)
    {
        if (DocumentIndex < project.Documents.Count)
            project.Documents.RemoveAt(DocumentIndex);
    }

    private void ExecuteRemoveDocument(DocProject project)
    {
        if (DocumentIndex < project.Documents.Count)
            project.Documents.RemoveAt(DocumentIndex);
    }

    private void UndoRemoveDocument(DocProject project)
    {
        if (DocumentSnapshot != null)
        {
            NormalizeDocumentBlockOrders(DocumentSnapshot);
            project.Documents.Insert(DocumentIndex, DocumentSnapshot);
        }
    }

    private void ExecuteRenameDocument(DocProject project)
    {
        var doc = FindDocument(project);
        if (doc != null) doc.Title = NewName;
    }

    private void UndoRenameDocument(DocProject project)
    {
        var doc = FindDocument(project);
        if (doc != null) doc.Title = OldName;
    }

    private void ExecuteAddBlock(DocProject project)
    {
        var doc = FindDocument(project);
        if (doc != null && BlockSnapshot != null)
        {
            doc.Blocks.Insert(BlockIndex, BlockSnapshot);
            NormalizeDocumentBlockOrders(doc);
        }
    }

    private void UndoAddBlock(DocProject project)
    {
        var doc = FindDocument(project);
        if (doc != null && BlockIndex < doc.Blocks.Count)
        {
            doc.Blocks.RemoveAt(BlockIndex);
            NormalizeDocumentBlockOrders(doc);
        }
    }

    private void ExecuteRemoveBlock(DocProject project)
    {
        var doc = FindDocument(project);
        if (doc != null && BlockIndex < doc.Blocks.Count)
        {
            doc.Blocks.RemoveAt(BlockIndex);
            NormalizeDocumentBlockOrders(doc);
        }
    }

    private void UndoRemoveBlock(DocProject project)
    {
        var doc = FindDocument(project);
        if (doc != null && BlockSnapshot != null)
        {
            doc.Blocks.Insert(BlockIndex, BlockSnapshot);
            NormalizeDocumentBlockOrders(doc);
        }
    }

    private void ExecuteSetBlockText(DocProject project)
    {
        var block = FindBlock(project);
        if (block == null) return;
        block.Text.PlainText = NewBlockText;
        if (NewSpans != null)
        {
            block.Text.Spans.Clear();
            block.Text.Spans.AddRange(NewSpans);
        }
    }

    private void UndoSetBlockText(DocProject project)
    {
        var block = FindBlock(project);
        if (block == null) return;
        block.Text.PlainText = OldBlockText;
        if (OldSpans != null)
        {
            block.Text.Spans.Clear();
            block.Text.Spans.AddRange(OldSpans);
        }
    }

    private void ExecuteSetBlockTableReference(DocProject project, string tableId)
    {
        var block = FindBlock(project);
        if (block == null)
        {
            return;
        }

        block.TableId = tableId;
    }

    private void ExecuteChangeBlockType(DocProject project)
    {
        var block = FindBlock(project);
        if (block != null) block.Type = NewBlockType;
    }

    private void UndoChangeBlockType(DocProject project)
    {
        var block = FindBlock(project);
        if (block != null) block.Type = OldBlockType;
    }

    private void ExecuteToggleBlockCheck(DocProject project, bool value)
    {
        var block = FindBlock(project);
        if (block != null) block.Checked = value;
    }

    private void ExecuteToggleSpan(DocProject project)
    {
        var block = FindBlock(project);
        if (block == null) return;
        block.Text.ToggleStyle(SpanStart, SpanLength, SpanStyle);
    }

    private void UndoToggleSpan(DocProject project)
    {
        // ToggleStyle is its own inverse
        var block = FindBlock(project);
        if (block == null) return;
        block.Text.ToggleStyle(SpanStart, SpanLength, SpanStyle);
    }

    private void ExecuteSetBlockIndent(DocProject project, int level)
    {
        var block = FindBlock(project);
        if (block != null) block.IndentLevel = level;
    }

    private void ExecuteSetBlockEmbeddedSize(DocProject project, float width, float height)
    {
        var block = FindBlock(project);
        if (block == null)
        {
            return;
        }

        block.EmbeddedWidth = width;
        block.EmbeddedHeight = height;
    }

    private void ExecuteSetBlockTableVariant(DocProject project, int tableVariantId)
    {
        var block = FindBlock(project);
        if (block == null)
        {
            return;
        }

        block.TableVariantId = tableVariantId;
    }

    private void ExecuteSetBlockTableVariableOverride(DocProject project, string expression)
    {
        var block = FindBlock(project);
        if (block == null || string.IsNullOrWhiteSpace(TableVariableId))
        {
            return;
        }

        int existingIndex = -1;
        for (int overrideIndex = 0; overrideIndex < block.TableVariableOverrides.Count; overrideIndex++)
        {
            var tableVariableOverride = block.TableVariableOverrides[overrideIndex];
            if (string.Equals(tableVariableOverride.VariableId, TableVariableId, StringComparison.Ordinal))
            {
                existingIndex = overrideIndex;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            if (existingIndex >= 0)
            {
                block.TableVariableOverrides.RemoveAt(existingIndex);
            }

            return;
        }

        if (existingIndex >= 0)
        {
            block.TableVariableOverrides[existingIndex].Expression = expression;
            return;
        }

        block.TableVariableOverrides.Add(new DocBlockTableVariableOverride
        {
            VariableId = TableVariableId,
            Expression = expression,
        });
    }

    private void ExecuteMoveBlock(DocProject project, int fromIndex, int toIndex)
    {
        var doc = FindDocument(project);
        if (doc == null || fromIndex < 0 || fromIndex >= doc.Blocks.Count)
        {
            return;
        }

        var block = doc.Blocks[fromIndex];
        doc.Blocks.RemoveAt(fromIndex);

        int insertAt = toIndex > fromIndex ? toIndex - 1 : toIndex;
        insertAt = Math.Clamp(insertAt, 0, doc.Blocks.Count);
        doc.Blocks.Insert(insertAt, block);
        NormalizeDocumentBlockOrders(doc);
    }

    private static void NormalizeDocumentBlockOrders(DocDocument document)
    {
        string nextOrder = FractionalIndex.Initial();
        for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            document.Blocks[blockIndex].Order = nextOrder;
            nextOrder = FractionalIndex.After(nextOrder);
        }
    }
}
