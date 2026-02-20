namespace Derp.Doc.Model;

public sealed class DocFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Folder";
    public DocFolderScope Scope { get; set; } = DocFolderScope.Tables;
    public string? ParentFolderId { get; set; }

    public DocFolder Clone()
    {
        return new DocFolder
        {
            Id = Id,
            Name = Name,
            Scope = Scope,
            ParentFolderId = ParentFolderId,
        };
    }
}
