using Derp.Doc.Model;

namespace Derp.Doc.Export;

internal sealed class ExportSubtableLinkModel
{
    public ExportSubtableLinkModel(
        ExportTableModel parentTable,
        DocColumn parentSubtableColumn,
        ExportTableModel childTable,
        DocColumn childParentRowColumn,
        string propertyName)
    {
        ParentTable = parentTable;
        ParentSubtableColumn = parentSubtableColumn;
        ChildTable = childTable;
        ChildParentRowColumn = childParentRowColumn;
        PropertyName = propertyName;
    }

    public ExportTableModel ParentTable { get; }
    public DocColumn ParentSubtableColumn { get; }
    public ExportTableModel ChildTable { get; }
    public DocColumn ChildParentRowColumn { get; }
    public string PropertyName { get; }
}

