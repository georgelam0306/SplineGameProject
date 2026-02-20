using Profiling;

namespace DerpLib.Diagnostics;

/// <summary>
/// Compile-time declarations for CPU profile scope IDs.
/// The profiling source generator discovers these attributes and generates Profiling.ProfileScopes.
/// </summary>
internal static class EngineProfileScopeDeclarations
{
    [ProfileScope("BeginFrame")]
    public static void BeginFrame() { }

    [ProfileScope("BeginFrameWaitFence")]
    public static void BeginFrameWaitFence() { }

    [ProfileScope("BeginFrameGpuReadback")]
    public static void BeginFrameGpuReadback() { }

    [ProfileScope("BeginFrameAcquireImage")]
    public static void BeginFrameAcquireImage() { }

    [ProfileScope("BeginFrameCommandRecording")]
    public static void BeginFrameCommandRecording() { }

    [ProfileScope("BeginFrameGpuTimingBegin")]
    public static void BeginFrameGpuTimingBegin() { }

    [ProfileScope("BeginFrameResetTransient")]
    public static void BeginFrameResetTransient() { }

    [ProfileScope("EndFrame")]
    public static void EndFrame() { }

    [ProfileScope("EndFrameGpuTimingEnd")]
    public static void EndFrameGpuTimingEnd() { }

    [ProfileScope("EndFrameCommandEnd")]
    public static void EndFrameCommandEnd() { }

    [ProfileScope("EndFrameQueueSubmit")]
    public static void EndFrameQueueSubmit() { }

    [ProfileScope("EndFramePresent")]
    public static void EndFramePresent() { }

    [ProfileScope("ComputeTransforms")]
    public static void ComputeTransforms() { }

    [ProfileScope("DrawBatch")]
    public static void DrawBatch() { }

    [ProfileScope("RenderPass3D")]
    public static void RenderPass3D() { }

    [ProfileScope("RenderPass2D")]
    public static void RenderPass2D() { }

    [ProfileScope("SdfRender")]
    public static void SdfRender() { }

    [ProfileScope("SdfBuild")]
    public static void SdfBuild() { }

    [ProfileScope("SdfFlush")]
    public static void SdfFlush() { }

    [ProfileScope("SdfDispatch")]
    public static void SdfDispatch() { }

    [ProfileScope("SdfTransition")]
    public static void SdfTransition() { }

    [ProfileScope("ImGuiBegin")]
    public static void ImGuiBegin() { }

    [ProfileScope("ImGuiEnd")]
    public static void ImGuiEnd() { }

    [ProfileScope("SwapchainPresent")]
    public static void SwapchainPresent() { }
}
