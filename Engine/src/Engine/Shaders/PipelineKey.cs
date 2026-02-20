using Silk.NET.Vulkan;

namespace DerpLib.Shaders;

/// <summary>
/// Lightweight cache key for pipeline lookup.
/// Combines shader ID with render state settings.
/// </summary>
public struct PipelineKey : IEquatable<PipelineKey>
{
    public int ShaderId;
    public ulong RenderPassHandle;
    public BlendMode BlendMode;
    public bool DepthTestEnabled;
    public bool DepthWriteEnabled;
    public CullModeFlags CullMode;
    public PrimitiveTopology Topology;

    public bool Equals(PipelineKey other)
    {
        return ShaderId == other.ShaderId &&
               RenderPassHandle == other.RenderPassHandle &&
               BlendMode == other.BlendMode &&
               DepthTestEnabled == other.DepthTestEnabled &&
               DepthWriteEnabled == other.DepthWriteEnabled &&
               CullMode == other.CullMode &&
               Topology == other.Topology;
    }

    public override bool Equals(object? obj) => obj is PipelineKey other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(ShaderId, RenderPassHandle, BlendMode, DepthTestEnabled, DepthWriteEnabled, CullMode, Topology);
    }
}
