namespace DerpLib.AssetPipeline;

public sealed class ModelAsset : IAsset
{
    public string Source { get; set; } = string.Empty;
    public string? TextureUrl { get; set; }
}
