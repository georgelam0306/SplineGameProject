using Serilog;

namespace DerpLib.Diagnostics;

/// <summary>
/// Tracks heap allocations per frame to identify allocation hot spots.
/// Uses GC.GetAllocatedBytesForCurrentThread() for accurate per-thread tracking.
/// </summary>
public static class AllocationTracker
{
    private static long _frameStartBytes;
    private static long _totalAllocatedThisFrame;
    private static int _frameCount;
    private static int _framesWithAllocations;
    private static bool _enabled;
    private static bool _logEveryFrame;
    private static ILogger? _log;

    // GC tracking
    private static int _gcGen0Start;
    private static int _gcGen1Start;
    private static int _gcGen2Start;
    private static int _gcGen0ThisFrame;
    private static int _gcGen1ThisFrame;
    private static int _gcGen2ThisFrame;

    /// <summary>Bytes allocated this frame.</summary>
    public static long AllocatedThisFrame => _totalAllocatedThisFrame;

    /// <summary>GC Gen0 collections this frame.</summary>
    public static int GcGen0ThisFrame => _gcGen0ThisFrame;

    /// <summary>GC Gen1 collections this frame.</summary>
    public static int GcGen1ThisFrame => _gcGen1ThisFrame;

    /// <summary>GC Gen2 collections this frame.</summary>
    public static int GcGen2ThisFrame => _gcGen2ThisFrame;

    /// <summary>
    /// Enable allocation tracking.
    /// </summary>
    /// <param name="log">Logger for output.</param>
    /// <param name="logEveryFrame">If true, logs every frame with allocations. If false, only logs summary.</param>
    public static void Enable(ILogger log, bool logEveryFrame = true)
    {
        _log = log;
        _enabled = true;
        _logEveryFrame = logEveryFrame;
        _frameCount = 0;
        _framesWithAllocations = 0;
        _totalAllocatedThisFrame = 0;
        _log.Information("Allocation tracking enabled");
    }

    /// <summary>
    /// Disable allocation tracking.
    /// </summary>
    public static void Disable()
    {
        if (_enabled && _log != null)
        {
            _log.Information("Allocation tracking disabled. {FramesWithAllocs}/{TotalFrames} frames had allocations",
                _framesWithAllocations, _frameCount);
        }
        _enabled = false;
    }

    /// <summary>
    /// Call at the start of each frame.
    /// </summary>
    public static void BeginFrame()
    {
        if (!_enabled) return;
        _frameStartBytes = GC.GetAllocatedBytesForCurrentThread();
        _gcGen0Start = GC.CollectionCount(0);
        _gcGen1Start = GC.CollectionCount(1);
        _gcGen2Start = GC.CollectionCount(2);
    }

    /// <summary>
    /// Call at the end of each frame.
    /// </summary>
    public static void EndFrame()
    {
        if (!_enabled) return;

        long endBytes = GC.GetAllocatedBytesForCurrentThread();
        long allocated = endBytes - _frameStartBytes;
        _frameCount++;
        _totalAllocatedThisFrame = allocated;

        // Track GC collections
        _gcGen0ThisFrame = GC.CollectionCount(0) - _gcGen0Start;
        _gcGen1ThisFrame = GC.CollectionCount(1) - _gcGen1Start;
        _gcGen2ThisFrame = GC.CollectionCount(2) - _gcGen2Start;

        if (allocated > 0)
        {
            _framesWithAllocations++;

            if (_logEveryFrame)
            {
                _log?.Warning("Frame {Frame}: Allocated {Bytes:N0} bytes on heap",
                    _frameCount, allocated);
            }
        }

        // No logging during frame loop - use LogSummary() at end or check console
    }

    /// <summary>
    /// Check a specific section of code for allocations.
    /// Returns the number of bytes allocated.
    /// </summary>
    public static long MeasureSection(string name, Action action)
    {
        if (!_enabled)
        {
            action();
            return 0;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        long after = GC.GetAllocatedBytesForCurrentThread();
        long allocated = after - before;

        if (allocated > 0 && _logEveryFrame)
        {
            _log?.Warning("  [{Section}]: Allocated {Bytes:N0} bytes", name, allocated);
        }

        return allocated;
    }

    /// <summary>
    /// Begin measuring a named section. Call EndSection with the returned value.
    /// </summary>
    public static long BeginSection() => _enabled ? GC.GetAllocatedBytesForCurrentThread() : 0;

    /// <summary>
    /// End measuring a section and log if allocations occurred.
    /// </summary>
    public static void EndSection(string name, long startBytes)
    {
        if (!_enabled) return;

        long endBytes = GC.GetAllocatedBytesForCurrentThread();
        long allocated = endBytes - startBytes;

        if (allocated > 0 && _logEveryFrame)
        {
            _log?.Warning("  [{Section}]: Allocated {Bytes:N0} bytes", name, allocated);
        }
    }

    /// <summary>
    /// Log a summary of allocation statistics.
    /// </summary>
    public static void LogSummary()
    {
        if (_log == null) return;

        float percentage = _frameCount > 0 ? (float)_framesWithAllocations / _frameCount * 100 : 0;
        _log.Information("Allocation summary: {FramesWithAllocs}/{TotalFrames} frames ({Percent:F1}%) had allocations",
            _framesWithAllocations, _frameCount, percentage);
    }
}
