using Derp.Doc.Plugins;

namespace Derp.Doc.Model;

public sealed class DocColumn
{
    private DocColumnKind _kind = DocColumnKind.Text;
    private string _columnTypeId = DocColumnTypeIds.Text;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Column";

    /// <summary>
    /// Legacy built-in column kind.
    /// For built-in types this remains authoritative and stays synced with ColumnTypeId.
    /// </summary>
    public DocColumnKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            if (DocColumnTypeIdMapper.ShouldSyncWithKind(_columnTypeId))
            {
                _columnTypeId = DocColumnTypeIdMapper.FromKind(value);
            }
        }
    }

    /// <summary>
    /// Stable column type id used by plugin-dispatched behavior.
    /// Built-in kinds map to core.* type ids.
    /// </summary>
    public string ColumnTypeId
    {
        get => _columnTypeId;
        set => _columnTypeId = DocColumnTypeIdMapper.Resolve(value, _kind);
    }

    /// <summary>
    /// Optional plugin-owned per-column settings payload.
    /// Serialized as raw JSON text owned by the column type plugin.
    /// </summary>
    public string? PluginSettingsJson { get; set; }

    public float Width { get; set; } = 150f;

    /// <summary>
    /// Options for Select columns. Null/empty for other kinds.
    /// </summary>
    public List<string>? Options { get; set; }

    /// <summary>
    /// Formula expression for Formula columns. Empty for non-formula columns.
    /// </summary>
    public string FormulaExpression { get; set; } = "";

    /// <summary>
    /// Target table ID for Relation columns. Null for non-relation columns.
    /// </summary>
    public string? RelationTableId { get; set; }

    /// <summary>
    /// Optional base table constraint for TableRef columns.
    /// When set, selectable tables should be the base table itself or tables derived from it.
    /// </summary>
    public string? TableRefBaseTableId { get; set; }

    /// <summary>
    /// Optional TableRef column id used to resolve this column as a row-id reference.
    /// When set on a Text/Id-like column, values are interpreted as row ids in the table
    /// selected by the referenced TableRef column.
    /// </summary>
    public string? RowRefTableRefColumnId { get; set; }

    /// <summary>
    /// Relation target resolution mode.
    /// ExternalTable uses RelationTableId directly.
    /// SelfTable resolves to the source table.
    /// ParentTable resolves to the source table's parent table.
    /// </summary>
    public DocRelationTargetMode RelationTargetMode { get; set; } = DocRelationTargetMode.ExternalTable;

    /// <summary>
    /// Target variant id for Relation columns.
    /// 0 selects the base variant.
    /// </summary>
    public int RelationTableVariantId { get; set; }

    /// <summary>
    /// Optional display column ID on the relation target table.
    /// Used to render relation labels in table cells and dropdowns.
    /// </summary>
    public string? RelationDisplayColumnId { get; set; }

    /// <summary>
    /// Whether this column is hidden from the spreadsheet view.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// True if this column is projected from a source table in a derived table.
    /// Projected columns are read-only in the spreadsheet view.
    /// </summary>
    public bool IsProjected { get; set; }

    /// <summary>
    /// True when this column is inherited from a base table.
    /// Inherited columns are read-only in the child table.
    /// </summary>
    public bool IsInherited { get; set; }

    /// <summary>
    /// Optional export type override (Phase 5). For example: "int", "float", "Fixed32", "Fixed64".
    /// Interpretation depends on column kind.
    /// </summary>
    public string? ExportType { get; set; }

    /// <summary>
    /// Optional minimum value clamp for Number columns.
    /// Null means unbounded.
    /// </summary>
    public double? NumberMin { get; set; }

    /// <summary>
    /// Optional maximum value clamp for Number columns.
    /// Null means unbounded.
    /// </summary>
    public double? NumberMax { get; set; }

    /// <summary>
    /// Optional export enum name override (Phase 5). Applies to Select columns.
    /// </summary>
    public string? ExportEnumName { get; set; }

    /// <summary>
    /// When true, this column is excluded from runtime export output.
    /// </summary>
    public bool ExportIgnore { get; set; }

    /// <summary>
    /// For Subtable columns: the child table ID that holds nested rows.
    /// </summary>
    public string? SubtableId { get; set; }

    /// <summary>
    /// For Subtable columns: selected preview renderer for parent-table cells.
    /// Empty/null means count-only display.
    /// Built-ins use "builtin.grid", "builtin.board", "builtin.calendar", "builtin.chart".
    /// Custom renderers use "custom:{rendererId}".
    /// </summary>
    public string? SubtableDisplayRendererId { get; set; }

    /// <summary>
    /// For Subtable columns: optional preview content width in parent-table cells.
    /// Null means auto width (use available cell width).
    /// </summary>
    public float? SubtableDisplayCellWidth { get; set; }

    /// <summary>
    /// For Subtable columns: optional preview content height in parent-table cells.
    /// Null means auto height (use available cell height).
    /// </summary>
    public float? SubtableDisplayCellHeight { get; set; }

    /// <summary>
    /// For Subtable columns: optional preview quality override.
    /// Null means use global preferences.
    /// </summary>
    public DocSubtablePreviewQuality? SubtableDisplayPreviewQuality { get; set; }

    /// <summary>
    /// Optional evaluation scopes for formula columns.
    /// Allows targeted recomputation policies (for example interactive previews)
    /// without hard-coding specific UI consumers into the core model.
    /// </summary>
    public DocFormulaEvalScope FormulaEvalScopes { get; set; } = DocFormulaEvalScope.None;

    /// <summary>
    /// Optional preview settings for MeshAsset columns.
    /// Null means default editor preview settings.
    /// </summary>
    public DocModelPreviewSettings? ModelPreviewSettings { get; set; }
}
