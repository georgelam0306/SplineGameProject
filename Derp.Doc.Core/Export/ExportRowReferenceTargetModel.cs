namespace Derp.Doc.Export;

internal sealed class ExportRowReferenceTargetModel
{
    public ExportRowReferenceTargetModel(
        int tag,
        int tagIndex,
        ExportTableModel targetTable,
        string propertyName)
    {
        Tag = tag;
        TagIndex = tagIndex;
        TargetTable = targetTable;
        PropertyName = propertyName;
    }

    public int Tag { get; }
    public int TagIndex { get; }
    public ExportTableModel TargetTable { get; }
    public string PropertyName { get; }
}
