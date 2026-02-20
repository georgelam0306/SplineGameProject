namespace Derp.Doc.Model;

public sealed class DocProject
{
    public string Name { get; set; } = "Untitled";
    public DocProjectUiState UiState { get; set; } = new();
    public Dictionary<string, string> PluginSettingsByKey { get; set; } = new(StringComparer.Ordinal);
    public List<DocFolder> Folders { get; set; } = new();
    public List<DocTable> Tables { get; set; } = new();
    public List<DocDocument> Documents { get; set; } = new();
}
