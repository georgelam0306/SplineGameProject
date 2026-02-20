namespace Derp.Doc.Model;

public sealed class DocTableVariant
{
    public const int BaseVariantId = 0;
    public const string BaseVariantName = "Base";

    public int Id { get; set; }
    public string Name { get; set; } = "";

    public DocTableVariant Clone()
    {
        return new DocTableVariant
        {
            Id = Id,
            Name = Name,
        };
    }
}

