using DerpLib.AssetPipeline;

namespace DerpLib.Build;

public enum ShaderStage
{
    Vertex,
    Fragment,
    Compute
}

/// <summary>
/// Represents a single shader stage to be compiled to SPIR-V.
/// </summary>
public sealed class ShaderStageAsset : IAsset
{
    /// <summary>
    /// Path to the GLSL source file.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// The shader stage type (Vertex or Fragment).
    /// </summary>
    public ShaderStage Stage { get; set; }
}
