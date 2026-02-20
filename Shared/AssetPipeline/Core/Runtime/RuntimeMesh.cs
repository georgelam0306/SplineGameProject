namespace DerpLib.AssetPipeline;

public sealed class RuntimeMesh
{
    public string Name { get; set; } = string.Empty;
    public float[] Positions { get; set; } = Array.Empty<float>();
    public float[]? Normals { get; set; }
    public float[]? TexCoords { get; set; }
    public int[] Indices { get; set; } = Array.Empty<int>();
}
