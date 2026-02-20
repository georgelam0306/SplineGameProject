namespace DerpLib.AssetPipeline;

public sealed class RuntimeModel
{
    public string Name { get; init; } = string.Empty;
    public List<string> MeshNames { get; init; } = new();
    public List<RuntimeMesh> Meshes { get; init; } = new();
}
