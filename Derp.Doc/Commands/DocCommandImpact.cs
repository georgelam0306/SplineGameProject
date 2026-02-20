namespace Derp.Doc.Commands;

internal static class DocCommandImpact
{
    [Flags]
    internal enum Flags
    {
        None = 0,
        AffectsTableState = 1 << 0,
        AffectsDocumentState = 1 << 1,
        RequiresFormulaRecalculation = 1 << 2,
    }

    public static Flags GetFlags(DocCommandKind kind)
    {
        return kind switch
        {
            DocCommandKind.SetCell => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.AddRow => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RemoveRow => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.MoveRow => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.AddColumn => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RemoveColumn => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.AddTable => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RemoveTable => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RenameTable => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RenameColumn => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.MoveColumn => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetColumnWidth => Flags.AffectsTableState,
            DocCommandKind.SetColumnFormula => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetColumnPluginSettings => Flags.AffectsTableState,
            DocCommandKind.SetColumnRelation => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetColumnOptions => Flags.AffectsTableState,
            DocCommandKind.SetColumnModelPreview => Flags.AffectsTableState,
            DocCommandKind.SetColumnHidden => Flags.AffectsTableState,
            DocCommandKind.SetColumnExportIgnore => Flags.AffectsTableState,
            DocCommandKind.SetColumnExportType => Flags.AffectsTableState,
            DocCommandKind.SetColumnNumberSettings => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetColumnExportEnumName => Flags.AffectsTableState,
            DocCommandKind.SetColumnSubtableDisplay => Flags.AffectsTableState,
            DocCommandKind.SetTableExportConfig => Flags.AffectsTableState,
            DocCommandKind.SetTableKeys => Flags.AffectsTableState,
            DocCommandKind.SetTableSchemaSource => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetTableInheritanceSource => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetDerivedConfig => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetDerivedBaseTable => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.AddDerivedStep => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RemoveDerivedStep => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.UpdateDerivedStep => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.ReorderDerivedStep => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.AddDerivedProjection => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RemoveDerivedProjection => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.UpdateDerivedProjection => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.ReorderDerivedProjection => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.AddView => Flags.AffectsTableState,
            DocCommandKind.RemoveView => Flags.AffectsTableState,
            DocCommandKind.RenameView => Flags.AffectsTableState,
            DocCommandKind.UpdateViewConfig => Flags.AffectsTableState,
            DocCommandKind.AddTableVariant => Flags.AffectsTableState,
            DocCommandKind.AddTableVariable => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RemoveTableVariable => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RenameTableVariable => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetTableVariableExpression => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetTableVariableType => Flags.AffectsTableState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.AddFolder => Flags.None,
            DocCommandKind.RemoveFolder => Flags.None,
            DocCommandKind.RenameFolder => Flags.None,
            DocCommandKind.MoveFolder => Flags.None,
            DocCommandKind.SetTableFolder => Flags.None,
            DocCommandKind.SetDocumentFolder => Flags.None,
            DocCommandKind.AddDocument => Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RemoveDocument => Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RenameDocument => Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.AddBlock => Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.RemoveBlock => Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetBlockText => Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.SetBlockTableReference => Flags.AffectsDocumentState,
            DocCommandKind.ChangeBlockType => Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.ToggleBlockCheck => Flags.AffectsDocumentState,
            DocCommandKind.ToggleSpan => Flags.AffectsDocumentState,
            DocCommandKind.SetBlockIndent => Flags.AffectsDocumentState,
            DocCommandKind.SetBlockEmbeddedSize => Flags.AffectsDocumentState,
            DocCommandKind.SetBlockTableVariant => Flags.AffectsDocumentState,
            DocCommandKind.SetBlockTableVariableOverride => Flags.AffectsDocumentState,
            DocCommandKind.MoveBlock => Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            DocCommandKind.ReplaceProjectSnapshot => Flags.AffectsTableState | Flags.AffectsDocumentState | Flags.RequiresFormulaRecalculation,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown command kind."),
        };
    }

    public static bool RequiresFormulaRecalculation(DocCommandKind kind)
    {
        return (GetFlags(kind) & Flags.RequiresFormulaRecalculation) != 0;
    }
}
