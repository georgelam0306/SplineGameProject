namespace Derp.Doc.Model;

public sealed class DocSecondaryKey
{
    public string ColumnId { get; set; } = "";
    public bool Unique { get; set; }

    public DocSecondaryKey Clone()
    {
        return new DocSecondaryKey
        {
            ColumnId = ColumnId,
            Unique = Unique,
        };
    }
}
