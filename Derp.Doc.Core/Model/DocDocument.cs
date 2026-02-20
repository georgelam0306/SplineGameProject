namespace Derp.Doc.Model;

public sealed class DocDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Untitled";
    public string? FolderId { get; set; }
    public string FileName { get; set; } = "untitled";
    public List<DocBlock> Blocks { get; set; } = new();
}
