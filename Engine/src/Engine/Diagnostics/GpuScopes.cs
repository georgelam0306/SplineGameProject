namespace DerpLib.Diagnostics;

/// <summary>
/// GPU timing scope IDs (manual constants, separate from CPU profiling).
/// </summary>
public static class GpuScopes
{
    public const int RenderPass3D = 0;
    public const int RenderPass2D = 1;
    public const int ComputeTransforms = 2;
    public const int SdfDispatch = 3;
    public const int SdfTransition = 4;
    public const int Count = 5;

    public static readonly string[] Names =
    {
        "3D Pass",
        "2D Pass",
        "GPU Transforms",
        "SDF",
        "SDF Transition",
    };
}
