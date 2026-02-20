namespace Derp.Doc.Model;

public sealed class DocTableVariantDelta
{
    public int VariantId { get; set; }
    public List<string> DeletedBaseRowIds { get; set; } = new();
    public List<DocRow> AddedRows { get; set; } = new();
    public List<DocTableCellOverride> CellOverrides { get; set; } = new();

    public DocTableVariantDelta Clone()
    {
        var clone = new DocTableVariantDelta
        {
            VariantId = VariantId,
            DeletedBaseRowIds = new List<string>(DeletedBaseRowIds),
            AddedRows = new List<DocRow>(AddedRows.Count),
            CellOverrides = new List<DocTableCellOverride>(CellOverrides.Count),
        };

        for (int rowIndex = 0; rowIndex < AddedRows.Count; rowIndex++)
        {
            var sourceRow = AddedRows[rowIndex];
            var clonedRow = new DocRow
            {
                Id = sourceRow.Id,
                Cells = new Dictionary<string, DocCellValue>(sourceRow.Cells.Count, StringComparer.Ordinal),
            };

            foreach (var cellEntry in sourceRow.Cells)
            {
                clonedRow.Cells[cellEntry.Key] = cellEntry.Value.Clone();
            }

            clone.AddedRows.Add(clonedRow);
        }

        for (int overrideIndex = 0; overrideIndex < CellOverrides.Count; overrideIndex++)
        {
            clone.CellOverrides.Add(CellOverrides[overrideIndex].Clone());
        }

        return clone;
    }
}
