namespace Derp.Doc.Model;

public sealed class DocTableCellOverride
{
    public string RowId { get; set; } = "";
    public string ColumnId { get; set; } = "";
    public DocCellValue Value { get; set; }

    public DocTableCellOverride Clone()
    {
        return new DocTableCellOverride
        {
            RowId = RowId,
            ColumnId = ColumnId,
            Value = Value.Clone(),
        };
    }
}
