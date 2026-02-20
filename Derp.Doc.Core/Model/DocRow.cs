namespace Derp.Doc.Model;

public sealed class DocRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, DocCellValue> Cells { get; set; } = new();

    /// <summary>
    /// Gets the cell value for a column, returning a default if not set.
    /// </summary>
    public DocCellValue GetCell(DocColumn column)
    {
        return Cells.TryGetValue(column.Id, out var value) ? value : DocCellValue.Default(column);
    }

    /// <summary>
    /// Gets the cell value by column ID, returning a text default if not set.
    /// </summary>
    public DocCellValue GetCell(string columnId)
    {
        return Cells.TryGetValue(columnId, out var value) ? value : DocCellValue.Text("");
    }

    /// <summary>
    /// Sets the cell value for a column.
    /// </summary>
    public void SetCell(string columnId, DocCellValue value)
    {
        Cells[columnId] = value.Clone();
    }
}
