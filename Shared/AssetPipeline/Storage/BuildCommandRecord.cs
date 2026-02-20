namespace DerpLib.AssetPipeline;

public sealed class BuildCommandRecord
{
    public string Hash { get; set; } = string.Empty;
    public int Version { get; set; }
    public string[] Inputs { get; set; } = Array.Empty<string>();
    public string[] Outputs { get; set; } = Array.Empty<string>();
}
