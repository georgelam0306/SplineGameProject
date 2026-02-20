namespace DerpLib.AssetPipeline;

public sealed class BuildProfile
{
    public string Name { get; init; } = "default";
    public string? Platform { get; init; }
    public string? GraphicsApi { get; init; }
}
