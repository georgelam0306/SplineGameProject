using DerpLib.AssetPipeline;

namespace DerpLib.Assets;

/// <summary>
/// A submesh within a compiled mesh, representing a portion with a specific material/texture.
/// </summary>
public sealed class Submesh : IAsset
{
    /// <summary>Index into the mesh's index buffer where this submesh starts.</summary>
    public int IndexOffset { get; set; }

    /// <summary>Number of indices in this submesh.</summary>
    public int IndexCount { get; set; }

    /// <summary>
    /// Index into the EmbeddedTextures array for this submesh's diffuse texture.
    /// -1 means no texture (use white).
    /// </summary>
    public int TextureIndex { get; set; } = -1;
}

/// <summary>
/// A compiled mesh asset containing interleaved vertex data and indices.
/// Produced by the build pipeline, loaded at runtime.
/// </summary>
public sealed class CompiledMesh : IAsset
{
    /// <summary>
    /// Number of vertices in the mesh.
    /// </summary>
    public int VertexCount { get; set; }

    /// <summary>
    /// Number of indices in the mesh.
    /// </summary>
    public int IndexCount { get; set; }

    /// <summary>
    /// Interleaved vertex data: Position(3) + Normal(3) + TexCoord(2) = 8 floats per vertex.
    /// Total size = VertexCount * 8 * sizeof(float).
    /// </summary>
    public float[] Vertices { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Triangle indices (3 per triangle).
    /// </summary>
    public uint[] Indices { get; set; } = Array.Empty<uint>();

    /// <summary>
    /// Submeshes defining material/texture regions within the mesh.
    /// If empty, the entire mesh uses a single default material.
    /// </summary>
    public Submesh[] Submeshes { get; set; } = Array.Empty<Submesh>();

    /// <summary>
    /// Embedded textures extracted from the model file (e.g., GLB).
    /// Each is a CompiledTexture stored inline.
    /// </summary>
    public CompiledTexture[] EmbeddedTextures { get; set; } = Array.Empty<CompiledTexture>();
}
