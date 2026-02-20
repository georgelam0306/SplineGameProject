namespace Derp.Doc.Model;

public enum DocBlockType : byte
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    BulletList,
    NumberedList,
    CheckboxList,
    CodeBlock,
    Quote,
    Formula,
    Variable,
    Divider,
    Table,
}

public sealed class DocBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Order { get; set; } = "a0";
    public DocBlockType Type { get; set; } = DocBlockType.Paragraph;
    public RichText Text { get; set; } = new();
    public int IndentLevel { get; set; }
    public bool Checked { get; set; }
    public string Language { get; set; } = "";
    public string TableId { get; set; } = "";
    public int TableVariantId { get; set; }
    public string ViewId { get; set; } = "";
    public float EmbeddedWidth { get; set; }
    public float EmbeddedHeight { get; set; }
    public List<DocBlockTableVariableOverride> TableVariableOverrides { get; set; } = new();

    public DocBlock Clone()
    {
        var clone = new DocBlock
        {
            Id = Id,
            Order = Order,
            Type = Type,
            Text = Text.Clone(),
            IndentLevel = IndentLevel,
            Checked = Checked,
            Language = Language,
            TableId = TableId,
            TableVariantId = TableVariantId,
            ViewId = ViewId,
            EmbeddedWidth = EmbeddedWidth,
            EmbeddedHeight = EmbeddedHeight,
            TableVariableOverrides = new List<DocBlockTableVariableOverride>(TableVariableOverrides.Count),
        };

        for (int overrideIndex = 0; overrideIndex < TableVariableOverrides.Count; overrideIndex++)
        {
            clone.TableVariableOverrides.Add(TableVariableOverrides[overrideIndex].Clone());
        }

        return clone;
    }
}
