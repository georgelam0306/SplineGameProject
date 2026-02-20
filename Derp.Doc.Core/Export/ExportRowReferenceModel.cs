using Derp.Doc.Model;

namespace Derp.Doc.Export;

internal sealed class ExportRowReferenceModel
{
    public ExportRowReferenceModel(
        string name,
        string propertyName,
        DocColumn tableRefColumn,
        DocColumn rowIdColumn,
        ExportColumnModel tableRefExportColumn,
        ExportColumnModel rowIdExportColumn,
        IReadOnlyList<ExportRowReferenceTargetModel> targets)
    {
        Name = name;
        PropertyName = propertyName;
        TableRefColumn = tableRefColumn;
        RowIdColumn = rowIdColumn;
        TableRefExportColumn = tableRefExportColumn;
        RowIdExportColumn = rowIdExportColumn;
        Targets = targets;

        string stem = "__rowref_" + name.ToLowerInvariant();
        RowTargetsSuffix = stem + "_row_targets";
        ParentKindRangesSuffix = stem + "_parent_kind_ranges";
        ParentKindRowsSuffix = stem + "_parent_kind_rows";
        ParentKindTargetMetaSuffix = stem + "_parent_kind_target_meta";
        ParentKindTargetRangesSuffix = stem + "_parent_kind_target_ranges";
        ParentKindTargetRowsSuffix = stem + "_parent_kind_target_rows";
        ParentTargetMetaSuffix = stem + "_parent_target_meta";
        ParentTargetRangesSuffix = stem + "_parent_target_ranges";
        ParentTargetRowsSuffix = stem + "_parent_target_rows";
    }

    public string Name { get; }
    public string PropertyName { get; }
    public DocColumn TableRefColumn { get; }
    public DocColumn RowIdColumn { get; }
    public ExportColumnModel TableRefExportColumn { get; }
    public ExportColumnModel RowIdExportColumn { get; }
    public IReadOnlyList<ExportRowReferenceTargetModel> Targets { get; }

    public string RowTargetsSuffix { get; }
    public string ParentKindRangesSuffix { get; }
    public string ParentKindRowsSuffix { get; }
    public string ParentKindTargetMetaSuffix { get; }
    public string ParentKindTargetRangesSuffix { get; }
    public string ParentKindTargetRowsSuffix { get; }
    public string ParentTargetMetaSuffix { get; }
    public string ParentTargetRangesSuffix { get; }
    public string ParentTargetRowsSuffix { get; }
}
