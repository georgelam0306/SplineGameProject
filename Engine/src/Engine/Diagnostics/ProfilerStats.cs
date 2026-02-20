using Profiling;

namespace DerpLib.Diagnostics;

/// <summary>
/// Aggregated profiler statistics from CPU, GPU, memory, and draw call sources.
/// Use Gather() to collect current frame stats.
/// </summary>
public ref struct ProfilerStats
{
    public bool CpuAvailable;
    public bool GpuAvailable;

    // CPU timing (from ProfilingService)
    public ReadOnlySpan<double> CpuScopeMs; // Last completed frame per-scope ms
    public ReadOnlySpan<string> CpuScopeNames;
    public double CpuFrameMs;              // Wall-clock for the frame (main thread)
    public double CpuFrameMsAvg;
    public double CpuScopedMs;             // Sum of instrumented scopes
    public double CpuScopedMsAvg;
    public double CpuOtherMs;              // max(0, Frame - Scoped)

    // GPU timing (from GpuTimingService)
    public ReadOnlySpan<double> GpuScopeMs; // Last completed frame per-scope ms
    public ReadOnlySpan<string> GpuScopeNames;
    public double GpuFrameMs;              // Frame total from timestamps
    public double GpuFrameMsAvg;
    public double GpuScopedMs;             // Sum of instrumented scopes
    public double GpuScopedMsAvg;
    public double GpuOtherMs;              // max(0, Frame - Scoped)

    // Memory (from AllocationTracker)
    public long AllocatedThisFrame;
    public int GcGen0;
    public int GcGen1;
    public int GcGen2;

    // Draw calls (from Engine)
    public int DrawCalls3D;
    public int MeshInstances;
    public int SdfCommands;
    public int TextureCount;

    /// <summary>
    /// Gather current profiler stats from all sources.
    /// </summary>
    public static ProfilerStats Gather(
        int meshInstances = 0,
        int sdfCommands = 0,
        int textureCount = 0)
    {
        var stats = new ProfilerStats();

        // CPU timing
        var cpuService = ProfilingService.Instance;
        if (cpuService != null)
        {
            stats.CpuAvailable = true;
            stats.CpuScopeMs = cpuService.LastFrameMilliseconds;
            stats.CpuScopeNames = cpuService.ScopeNames;
            stats.CpuFrameMs = cpuService.LastFrameWallMs;
            stats.CpuFrameMsAvg = cpuService.AverageFrameWallMs;

            stats.CpuScopedMs = cpuService.TotalFrameMs;
            stats.CpuScopedMsAvg = cpuService.AverageTotalMs;
            stats.CpuOtherMs = Math.Max(0.0, stats.CpuFrameMs - stats.CpuScopedMs);
        }

        // GPU timing
        var gpuService = GpuTimingService.Instance;
        if (gpuService != null)
        {
            stats.GpuAvailable = true;
            stats.GpuScopeMs = gpuService.LastFrameMilliseconds;
            stats.GpuScopeNames = gpuService.ScopeNames;
            stats.GpuFrameMs = gpuService.TotalFrameMs;
            stats.GpuFrameMsAvg = gpuService.AverageTotalMs;

            stats.GpuScopedMs = gpuService.ScopedFrameMs;
            double avgScopedMs = 0;
            var avgPerScope = gpuService.AverageMilliseconds;
            for (int i = 0; i < avgPerScope.Length; i++)
            {
                avgScopedMs += avgPerScope[i];
            }
            stats.GpuScopedMsAvg = avgScopedMs;
            stats.GpuOtherMs = Math.Max(0.0, stats.GpuFrameMs - stats.GpuScopedMs);
        }

        // Memory
        stats.AllocatedThisFrame = AllocationTracker.AllocatedThisFrame;
        stats.GcGen0 = AllocationTracker.GcGen0ThisFrame;
        stats.GcGen1 = AllocationTracker.GcGen1ThisFrame;
        stats.GcGen2 = AllocationTracker.GcGen2ThisFrame;

        // Draw calls (passed in from Engine)
        stats.MeshInstances = meshInstances;
        stats.SdfCommands = sdfCommands;
        stats.TextureCount = textureCount;

        return stats;
    }

    /// <summary>
    /// Check if we have any profiling data.
    /// </summary>
    public readonly bool HasCpuData => CpuAvailable;

    /// <summary>
    /// Check if we have GPU profiling data.
    /// </summary>
    public readonly bool HasGpuData => GpuAvailable;
}
