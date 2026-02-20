using Silk.NET.Vulkan;

namespace DerpLib.Shaders;

/// <summary>
/// Tracks current render state. Modified by SetBlendMode, SetDepthTest, etc.
/// Combined with current shader at draw time to get/create the right pipeline.
/// </summary>
public sealed class RenderState
{
    /// <summary>Current shader (set by BeginShaderMode).</summary>
    public Shader? CurrentShader { get; set; }

    /// <summary>Current blend mode. Default: Alpha.</summary>
    public BlendMode BlendMode { get; set; } = BlendMode.Alpha;

    /// <summary>Enable depth testing. Default: false (2D).</summary>
    public bool DepthTestEnabled { get; set; } = false;

    /// <summary>Enable depth writing. Default: false (2D).</summary>
    public bool DepthWriteEnabled { get; set; } = false;

    /// <summary>Face culling mode. Default: None (2D friendly).</summary>
    public CullModeFlags CullMode { get; set; } = CullModeFlags.None;

    /// <summary>Primitive topology. Default: TriangleList.</summary>
    public PrimitiveTopology Topology { get; set; } = PrimitiveTopology.TriangleList;

    /// <summary>
    /// Resets to default 2D state.
    /// </summary>
    public void Reset2D()
    {
        BlendMode = BlendMode.Alpha;
        DepthTestEnabled = false;
        DepthWriteEnabled = false;
        CullMode = CullModeFlags.None;
        Topology = PrimitiveTopology.TriangleList;
    }

    /// <summary>
    /// Resets to default 3D state.
    /// </summary>
    public void Reset3D()
    {
        BlendMode = BlendMode.None;
        DepthTestEnabled = true;
        DepthWriteEnabled = true;
        CullMode = CullModeFlags.BackBit;
        Topology = PrimitiveTopology.TriangleList;
    }

    /// <summary>
    /// Creates a pipeline key from current state + shader.
    /// </summary>
    internal PipelineKey ToKey(Shader shader)
    {
        return new PipelineKey
        {
            ShaderId = shader.Id,
            BlendMode = BlendMode,
            DepthTestEnabled = DepthTestEnabled,
            DepthWriteEnabled = DepthWriteEnabled,
            CullMode = CullMode,
            Topology = Topology
        };
    }
}
