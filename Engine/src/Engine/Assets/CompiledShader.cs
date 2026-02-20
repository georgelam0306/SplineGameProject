using DerpLib.AssetPipeline;

namespace DerpLib.Assets;

/// <summary>
/// A compiled shader asset containing SPIR-V bytecode for vertex and fragment stages.
/// Produced by the build pipeline, loaded at runtime.
/// </summary>
public sealed class CompiledShader : IAsset
{
    /// <summary>
    /// Vertex shader SPIR-V bytecode.
    /// </summary>
    public byte[] VertexSpirv { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Fragment shader SPIR-V bytecode.
    /// </summary>
    public byte[] FragmentSpirv { get; set; } = Array.Empty<byte>();
}
