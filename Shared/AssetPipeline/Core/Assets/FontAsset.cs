namespace DerpLib.AssetPipeline;

public sealed class FontAsset : IAsset
{
    public string Source { get; set; } = string.Empty;
    public string BoldSource { get; set; } = string.Empty;
    public string ItalicSource { get; set; } = string.Empty;
    public string BoldItalicSource { get; set; } = string.Empty;

    public int FontSizePixels { get; set; } = 32;

    public int AtlasSizePixels { get; set; } = 1024;

    public int FirstCodepoint { get; set; } = 32;

    public int LastCodepoint { get; set; } = 126;
}
