using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using DerpLib.Core;

namespace DerpLib.Diagnostics;

/// <summary>
/// GPU timing service using Vulkan timestamp queries.
/// Double-buffered for frames-in-flight, provides rolling averages.
/// </summary>
public sealed class GpuTimingService : IDisposable
{
    /// <summary>
    /// Global instance for use by GpuScope.
    /// Set during engine initialization.
    /// </summary>
    public static GpuTimingService? Instance { get; set; }

    private readonly VkDevice _vkDevice;
    private readonly int _framesInFlight;
    private readonly int _maxScopes;
    private readonly int _queryCount;

    // Query pools - one per frame in flight
    private readonly QueryPool[] _queryPools;

    // Timing data
    private readonly long[] _timestamps;        // Raw timestamp pairs per scope
    private readonly double[] _scopeMs;          // Current frame milliseconds per scope
    private readonly double[] _avgMs;            // Rolling average milliseconds
    private readonly string[] _scopeNames;

    // Rolling average state
    private const int AvgWindowSize = 60;
    private readonly double[,] _history;        // [scopeIndex, historySlot]
    private readonly double[] _sums;            // Running sums
    private int _historyIndex;
    private int _sampleCount;

    // Frame total rolling average (includes unscoped GPU work)
    private readonly double[] _frameHistory;
    private double _frameSum;

    // Conversion factor: nanoseconds per timestamp tick
    private readonly double _timestampPeriod;

    /// <summary>Whether GPU timing is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Number of registered scopes.</summary>
    public int ScopeCount => _maxScopes;

    /// <summary>Get rolling average milliseconds per scope.</summary>
    public ReadOnlySpan<double> AverageMilliseconds => _avgMs;

    /// <summary>Get scope names.</summary>
    public ReadOnlySpan<string> ScopeNames => _scopeNames;

    /// <summary>Total GPU time for the frame in milliseconds.</summary>
    public double TotalFrameMs { get; private set; }

    /// <summary>Total GPU time summed across instrumented scopes for the last completed frame.</summary>
    public double ScopedFrameMs { get; private set; }

    /// <summary>Average total GPU time in milliseconds.</summary>
    public double AverageTotalMs { get; private set; }

    /// <summary>Get last completed frame milliseconds per scope.</summary>
    public ReadOnlySpan<double> LastFrameMilliseconds => _scopeMs;

    public GpuTimingService(VkDevice vkDevice, int framesInFlight, string[] scopeNames)
    {
        _vkDevice = vkDevice;
        _framesInFlight = framesInFlight;
        _maxScopes = scopeNames.Length;
        _queryCount = (_maxScopes + 1) * 2; // +1 for frame begin/end timestamps
        _scopeNames = scopeNames;

        // Get timestamp period from physical device properties
        _timestampPeriod = GetTimestampPeriod();

        // Allocate arrays
        _queryPools = new QueryPool[framesInFlight];
        _timestamps = new long[_queryCount];  // Frame start/end + start/end per scope
        _scopeMs = new double[_maxScopes];
        _avgMs = new double[_maxScopes];
        _history = new double[_maxScopes, AvgWindowSize];
        _sums = new double[_maxScopes];
        _frameHistory = new double[AvgWindowSize];

        // Create query pools
        CreateQueryPools();

        Instance = this;
    }

    private unsafe double GetTimestampPeriod()
    {
        PhysicalDeviceProperties props;
        _vkDevice.Vk.GetPhysicalDeviceProperties(_vkDevice.PhysicalDevice, &props);
        return props.Limits.TimestampPeriod;  // nanoseconds per tick
    }

    private unsafe void CreateQueryPools()
    {
        var vk = _vkDevice.Vk;
        var device = _vkDevice.Device;

        for (int i = 0; i < _framesInFlight; i++)
        {
            var createInfo = new QueryPoolCreateInfo
            {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.Timestamp,
                QueryCount = (uint)_queryCount
            };

            var result = vk.CreateQueryPool(device, &createInfo, null, out _queryPools[i]);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create query pool: {result}");
            }
        }
    }

    /// <summary>
    /// Called at the start of a frame to reset the query pool.
    /// </summary>
    public unsafe void BeginFrame(CommandBuffer cmd, int frameIndex)
    {
        if (!Enabled) return;

        var vk = _vkDevice.Vk;
        var queryPool = _queryPools[frameIndex];

        // Reset all queries in the pool
        vk.CmdResetQueryPool(cmd, queryPool, 0, (uint)_queryCount);

        // Frame start timestamp uses query 0
        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.TopOfPipeBit, queryPool, 0);
    }

    /// <summary>
    /// Called at the end of a frame to record the frame end timestamp.
    /// Must be recorded into the same command buffer after all GPU work for the frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void EndFrame(CommandBuffer cmd, int frameIndex)
    {
        if (!Enabled) return;

        var vk = _vkDevice.Vk;
        var queryPool = _queryPools[frameIndex];

        // Frame end timestamp uses query 1
        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, queryPool, 1);
    }

    /// <summary>
    /// Write a timestamp at the start of a scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void BeginScope(CommandBuffer cmd, int frameIndex, int scopeId)
    {
        if (!Enabled || scopeId < 0 || scopeId >= _maxScopes) return;

        var vk = _vkDevice.Vk;
        var queryPool = _queryPools[frameIndex];
        uint queryIndex = (uint)((scopeId + 1) * 2);  // Start timestamp (offset by frame queries)

        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.TopOfPipeBit, queryPool, queryIndex);
    }

    /// <summary>
    /// Write a timestamp at the end of a scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void EndScope(CommandBuffer cmd, int frameIndex, int scopeId)
    {
        if (!Enabled || scopeId < 0 || scopeId >= _maxScopes) return;

        var vk = _vkDevice.Vk;
        var queryPool = _queryPools[frameIndex];
        uint queryIndex = (uint)((scopeId + 1) * 2 + 1);  // End timestamp (offset by frame queries)

        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, queryPool, queryIndex);
    }

    /// <summary>
    /// Read results from a previous frame's query pool.
    /// Call after WaitForFence on the frame whose results you want.
    /// </summary>
    public unsafe void ReadResults(int frameIndex)
    {
        if (!Enabled) return;

        var vk = _vkDevice.Vk;
        var device = _vkDevice.Device;
        var queryPool = _queryPools[frameIndex];

        // Read all timestamps
        fixed (long* timestampsPtr = _timestamps)
        {
            var result = vk.GetQueryPoolResults(
                device,
                queryPool,
                0,
                (uint)_queryCount,
                (nuint)(_queryCount * sizeof(long)),
                timestampsPtr,
                sizeof(long),
                QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit);

            if (result != Result.Success && result != Result.NotReady)
            {
                // Query results not available yet, skip this frame
                return;
            }
        }

        // Frame total is from dedicated begin/end timestamps (queries 0 and 1)
        long frameStartTicks = _timestamps[0];
        long frameEndTicks = _timestamps[1];
        long frameDeltaTicks = frameEndTicks - frameStartTicks;
        double frameMs = frameDeltaTicks > 0 ? (frameDeltaTicks * _timestampPeriod) / 1_000_000.0 : 0;
        TotalFrameMs = frameMs;

        // Calculate milliseconds per scope
        double scopedMs = 0;
        for (int i = 0; i < _maxScopes; i++)
        {
            int baseIndex = (i + 1) * 2;
            long startTicks = _timestamps[baseIndex];
            long endTicks = _timestamps[baseIndex + 1];
            long deltaTicks = endTicks - startTicks;

            // Convert to milliseconds: (ticks * nanosPerTick) / 1_000_000
            double ms = deltaTicks > 0 ? (deltaTicks * _timestampPeriod) / 1_000_000.0 : 0;
            _scopeMs[i] = ms;
            scopedMs += ms;

            // Update rolling average
            int oldSlot = _historyIndex;
            _sums[i] -= _history[i, oldSlot];
            _sums[i] += ms;
            _history[i, oldSlot] = ms;

            int samples = Math.Min(_sampleCount + 1, AvgWindowSize);
            _avgMs[i] = _sums[i] / samples;
        }

        ScopedFrameMs = scopedMs;

        // Update sample count and history index
        if (_sampleCount < AvgWindowSize)
        {
            _sampleCount++;
        }

        int frameOldSlot = _historyIndex;
        _frameSum -= _frameHistory[frameOldSlot];
        _frameSum += frameMs;
        _frameHistory[frameOldSlot] = frameMs;

        _historyIndex = (_historyIndex + 1) % AvgWindowSize;

        // Calculate average total
        AverageTotalMs = _frameSum / Math.Min(_sampleCount, AvgWindowSize);
    }

    /// <summary>
    /// Reset all timing data.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_scopeMs, 0, _maxScopes);
        Array.Clear(_avgMs, 0, _maxScopes);
        Array.Clear(_sums, 0, _maxScopes);
        Array.Clear(_frameHistory, 0, AvgWindowSize);
        _frameSum = 0;

        for (int i = 0; i < _maxScopes; i++)
        {
            for (int j = 0; j < AvgWindowSize; j++)
            {
                _history[i, j] = 0;
            }
        }

        _historyIndex = 0;
        _sampleCount = 0;
        TotalFrameMs = 0;
        ScopedFrameMs = 0;
        AverageTotalMs = 0;
    }

    public unsafe void Dispose()
    {
        var vk = _vkDevice.Vk;
        var device = _vkDevice.Device;

        vk.DeviceWaitIdle(device);

        foreach (var pool in _queryPools)
        {
            vk.DestroyQueryPool(device, pool, null);
        }

        if (Instance == this)
            Instance = null;
    }
}
