using Silk.NET.Vulkan;

namespace DerpLib.Diagnostics;

/// <summary>
/// RAII-style GPU scope for automatic timestamp recording.
/// </summary>
public ref struct GpuScope
{
    private readonly CommandBuffer _cmd;
    private readonly int _frameIndex;
    private readonly int _scopeId;

    private GpuScope(CommandBuffer cmd, int frameIndex, int scopeId)
    {
        _cmd = cmd;
        _frameIndex = frameIndex;
        _scopeId = scopeId;
    }

    /// <summary>
    /// Begin a GPU timing scope. Use with 'using' statement.
    /// </summary>
    public static GpuScope Begin(CommandBuffer cmd, int frameIndex, int scopeId)
    {
        GpuTimingService.Instance?.BeginScope(cmd, frameIndex, scopeId);
        return new GpuScope(cmd, frameIndex, scopeId);
    }

    /// <summary>
    /// End the GPU timing scope.
    /// </summary>
    public void Dispose()
    {
        GpuTimingService.Instance?.EndScope(_cmd, _frameIndex, _scopeId);
    }
}

