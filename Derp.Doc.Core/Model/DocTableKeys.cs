namespace Derp.Doc.Model;

public sealed class DocTableKeys
{
    public string PrimaryKeyColumnId { get; set; } = "";
    public List<DocSecondaryKey> SecondaryKeys { get; set; } = new();

    public DocTableKeys Clone()
    {
        var clone = new DocTableKeys
        {
            PrimaryKeyColumnId = PrimaryKeyColumnId,
        };

        for (int i = 0; i < SecondaryKeys.Count; i++)
        {
            clone.SecondaryKeys.Add(SecondaryKeys[i].Clone());
        }

        return clone;
    }
}
