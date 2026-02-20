using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FlameProfiler;

/// <summary>
/// Hierarchical flame graph profiler service.
/// Tracks parent-child relationships between scopes for flame graph visualization.
/// </summary>
/// <remarks>
/// Thread-safety: This class is designed for single-threaded game loop usage.
/// The static Instance property should be set once during initialization.
/// </remarks>
public sealed class FlameProfilerService : IDisposable
{
    /// <summary>
    /// Global instance for use by FlameScope.
    /// Set this during application startup before any profiling occurs.
    /// </summary>
    public static FlameProfilerService? Instance { get; set; }

    /// <summary>Number of frames to keep in history (ring buffer size).</summary>
    public const int FrameHistorySize = 120; // 2 seconds at 60 FPS

    /// <summary>Conversion factor from ticks to milliseconds.</summary>
    public static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    // Frame ring buffer
    private readonly FlameFrame[] _frames;
    private int _currentFrameIndex;
    private int _validFrameCount;

    // Active frame being recorded
    private FlameFrame? _activeFrame;
    private bool _frameInProgress;

    // Scope names
    private readonly string[] _scopeNames;

    /// <summary>Enable or disable timing collection.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether to show the profiler overlay.</summary>
    public bool ShowOverlay { get; set; }

    /// <summary>Number of registered scopes.</summary>
    public int ScopeCount { get; }

    /// <summary>Get the scope names.</summary>
    public ReadOnlySpan<string> ScopeNames => _scopeNames;

    /// <summary>Number of valid frames in history.</summary>
    public int ValidFrameCount => _validFrameCount;

    /// <summary>Whether a frame is currently being recorded.</summary>
    public bool IsFrameInProgress => _frameInProgress;

    /// <summary>
    /// Creates a new FlameProfilerService.
    /// </summary>
    /// <param name="scopeNames">Array of scope names.</param>
    public FlameProfilerService(string[] scopeNames)
    {
        ScopeCount = scopeNames.Length;
        _scopeNames = new string[ScopeCount];
        Array.Copy(scopeNames, _scopeNames, ScopeCount);

        // Pre-allocate frame ring buffer
        _frames = new FlameFrame[FrameHistorySize];
        for (int i = 0; i < FrameHistorySize; i++)
        {
            _frames[i] = new FlameFrame();
        }

        // Set as global instance
        Instance = this;
    }

    /// <summary>
    /// Begins a new frame. Must be called at the start of each frame.
    /// </summary>
    /// <param name="frameNumber">The frame number.</param>
    public void BeginFrame(int frameNumber)
    {
        if (!Enabled || _frameInProgress)
        {
            return;
        }

        _activeFrame = _frames[_currentFrameIndex];
        _activeFrame.Reset();
        _activeFrame.FrameNumber = frameNumber;
        _activeFrame.FrameStartTicks = Stopwatch.GetTimestamp();
        _frameInProgress = true;
    }

    /// <summary>
    /// Cancels the current in-progress frame without committing it to history.
    /// Useful when the engine skips a frame after BeginFrame() (e.g., swapchain out-of-date or minimized).
    /// </summary>
    public void CancelFrame()
    {
        if (!_frameInProgress || _activeFrame == null)
        {
            return;
        }

        _activeFrame.Reset();
        _activeFrame = null;
        _frameInProgress = false;
    }

    /// <summary>
    /// Ends the current frame. Must be called at the end of each frame.
    /// </summary>
    public void EndFrame()
    {
        if (!Enabled || !_frameInProgress || _activeFrame == null)
        {
            return;
        }

        _activeFrame.FrameEndTicks = Stopwatch.GetTimestamp();
        _activeFrame.IsValid = true;
        _frameInProgress = false;

        // Advance ring buffer
        _currentFrameIndex = (_currentFrameIndex + 1) % FrameHistorySize;
        if (_validFrameCount < FrameHistorySize)
        {
            _validFrameCount++;
        }

        _activeFrame = null;
    }

    /// <summary>
    /// Begins a scope. Returns the node index for use with EndScope.
    /// </summary>
    /// <param name="scopeId">The scope identifier.</param>
    /// <returns>Node index, or -1 if profiling is disabled or capacity exceeded.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int BeginScope(int scopeId)
    {
        if (!Enabled || !_frameInProgress || _activeFrame == null)
        {
            return -1;
        }

        return _activeFrame.BeginScope(scopeId, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Ends a scope started by BeginScope.
    /// </summary>
    /// <param name="nodeIndex">The node index returned by BeginScope.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EndScope(int nodeIndex)
    {
        if (!Enabled || !_frameInProgress || _activeFrame == null || nodeIndex < 0)
        {
            return;
        }

        _activeFrame.EndScope(nodeIndex, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Gets a frame from history by age (0 = most recent completed frame).
    /// </summary>
    /// <param name="age">How many frames back (0 = most recent).</param>
    /// <returns>The frame, or null if not available.</returns>
    public FlameFrame? GetFrame(int age)
    {
        if (age < 0 || age >= _validFrameCount)
        {
            return null;
        }

        // Calculate index in ring buffer
        // _currentFrameIndex points to the next frame to write
        // So most recent completed frame is at _currentFrameIndex - 1
        int index = (_currentFrameIndex - 1 - age + FrameHistorySize) % FrameHistorySize;
        FlameFrame frame = _frames[index];

        return frame.IsValid ? frame : null;
    }

    /// <summary>
    /// Gets the most recent completed frame.
    /// </summary>
    public FlameFrame? GetLatestFrame() => GetFrame(0);

    /// <summary>
    /// Gets the name for a scope ID.
    /// </summary>
    public string GetScopeName(int scopeId)
    {
        if (scopeId >= 0 && scopeId < ScopeCount)
        {
            return _scopeNames[scopeId];
        }
        return "Unknown";
    }

    /// <summary>
    /// Converts ticks to milliseconds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double TicksToMilliseconds(long ticks) => ticks * TicksToMs;

    /// <summary>
    /// Resets all frame data.
    /// </summary>
    public void Reset()
    {
        _frameInProgress = false;
        _activeFrame = null;
        _validFrameCount = 0;
        _currentFrameIndex = 0;

        for (int i = 0; i < FrameHistorySize; i++)
        {
            _frames[i].Reset();
        }
    }

    /// <summary>
    /// Disposes the service.
    /// </summary>
    public void Dispose()
    {
        Reset();

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
