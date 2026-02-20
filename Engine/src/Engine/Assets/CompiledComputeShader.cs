using DerpLib.AssetPipeline;

namespace DerpLib.Assets;

/// <summary>
/// A compiled compute shader asset containing SPIR-V bytecode.
/// Produced by the build pipeline, loaded at runtime.
/// </summary>
public sealed class CompiledComputeShader : IAsset
{
    /// <summary>
    /// Compute shader SPIR-V bytecode.
    /// </summary>
    public byte[] Spirv { get; set; } = Array.Empty<byte>();
}
