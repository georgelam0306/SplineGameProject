namespace DerpLib.AssetPipeline;

public sealed class Package
{
    public string Name { get; set; } = string.Empty;
    public List<AssetItem> Assets { get; } = new();
    public List<string> ExplicitFolders { get; } = new();
}
