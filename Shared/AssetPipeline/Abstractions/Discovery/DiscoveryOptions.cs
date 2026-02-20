namespace DerpLib.AssetPipeline;

public sealed class DiscoveryOptions
{
    public bool PreferPlugin { get; init; }
    public List<string> PluginDirectories { get; } = new();
}
