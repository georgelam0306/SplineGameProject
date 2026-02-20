namespace DerpLib.AssetPipeline;

public sealed class AssetItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Location { get; init; } = string.Empty;
    public IAsset Asset { get; init; } = default!;
}
