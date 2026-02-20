using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DieDrifterDie.Simulation;

/// <summary>
/// Zero-allocation profiler for measuring per-system tick times.
/// Stores rolling averages and supports CSV export.
/// </summary>
public sealed class SimulationProfiler : IDisposable
{
    // Pre-allocated arrays (zero allocation during measurement)
    private readonly long[] _ticksPerSystem;         // Current frame ticks per system
    private readonly double[] _avgMsPerSystem;       // Rolling average in milliseconds
    private readonly string[] _systemNames;          // Cached system names

    // Rolling average state
    private const int AvgWindowSize = 60;            // 1 second at 60fps
    private readonly long[,] _tickHistory;           // [systemIndex, historySlot]
    private readonly long[] _tickSums;               // Running sum for fast average
    private int _historyIndex;
    private int _sampleCount;

    // Conversion factor
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    // Configuration
    public bool Enabled { get; set; }
    public bool ShowOverlay { get; set; }

    // CSV export
    private StreamWriter? _csvWriter;
    private readonly StringBuilder _csvLineBuffer;
    private bool _isRecording;
    private int _recordedFrameCount;

    public bool IsRecording => _isRecording;
    public int RecordedFrameCount => _recordedFrameCount;

    /// <summary>
    /// Get the cached system names.
    /// </summary>
    public ReadOnlySpan<string> SystemNames => _systemNames;

    /// <summary>
    /// Get the current frame's raw tick counts per system.
    /// </summary>
    public ReadOnlySpan<long> CurrentTicks => _ticksPerSystem;

    /// <summary>
    /// Get rolling average milliseconds per system.
    /// </summary>
    public ReadOnlySpan<double> AverageMilliseconds => _avgMsPerSystem;

    /// <summary>
    /// Get total simulation time for current frame in milliseconds.
    /// </summary>
    public double TotalFrameMs { get; private set; }

    /// <summary>
    /// Get average total simulation time in milliseconds.
    /// </summary>
    public double AverageTotalMs { get; private set; }

    public SimulationProfiler(int systemCount, ReadOnlySpan<string> systemNames)
    {
        _ticksPerSystem = new long[systemCount];
        _avgMsPerSystem = new double[systemCount];
        _systemNames = new string[systemCount];
        _tickHistory = new long[systemCount, AvgWindowSize];
        _tickSums = new long[systemCount];
        _csvLineBuffer = new StringBuilder(2048);

        // Copy system names
        for (int i = 0; i < systemCount && i < systemNames.Length; i++)
        {
            _systemNames[i] = systemNames[i];
        }
    }

    /// <summary>
    /// Record elapsed ticks for a system. Called after each system tick.
    /// </summary>
    public void RecordSystemTick(int systemIndex, long elapsedTicks)
    {
        if (systemIndex < 0 || systemIndex >= _ticksPerSystem.Length) return;

        _ticksPerSystem[systemIndex] = elapsedTicks;
    }

    /// <summary>
    /// Called at the end of each simulation frame to update averages.
    /// </summary>
    public void EndFrame(int frame)
    {
        // Calculate total and update rolling averages
        long totalTicks = 0;

        for (int i = 0; i < _ticksPerSystem.Length; i++)
        {
            long ticks = _ticksPerSystem[i];
            totalTicks += ticks;

            // Update rolling sum: subtract old value, add new
            int oldSlot = _historyIndex;
            _tickSums[i] -= _tickHistory[i, oldSlot];
            _tickSums[i] += ticks;
            _tickHistory[i, oldSlot] = ticks;

            // Calculate average
            int samples = Math.Min(_sampleCount + 1, AvgWindowSize);
            _avgMsPerSystem[i] = (_tickSums[i] / (double)samples) * TicksToMs;
        }

        TotalFrameMs = totalTicks * TicksToMs;

        // Update average total
        if (_sampleCount < AvgWindowSize)
        {
            _sampleCount++;
        }

        long totalSum = 0;
        for (int i = 0; i < _ticksPerSystem.Length; i++)
        {
            totalSum += _tickSums[i];
        }
        AverageTotalMs = (totalSum / (double)Math.Min(_sampleCount, AvgWindowSize)) * TicksToMs;

        // Advance history index
        _historyIndex = (_historyIndex + 1) % AvgWindowSize;

        // Write to CSV if recording
        if (_isRecording && _csvWriter != null)
        {
            WriteFrameToCsv(frame);
        }

        // Reset current frame ticks
        Array.Clear(_ticksPerSystem, 0, _ticksPerSystem.Length);
    }

    /// <summary>
    /// Start recording timing data to a CSV file.
    /// </summary>
    public void StartRecording(string filePath)
    {
        if (_isRecording) return;

        try
        {
            _csvWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            WriteHeader();
            _isRecording = true;
            _recordedFrameCount = 0;
        }
        catch (Exception)
        {
            _csvWriter?.Dispose();
            _csvWriter = null;
        }
    }

    /// <summary>
    /// Stop recording and close the CSV file.
    /// </summary>
    public void StopRecording()
    {
        if (!_isRecording) return;

        _csvWriter?.Flush();
        _csvWriter?.Dispose();
        _csvWriter = null;
        _isRecording = false;
    }

    private void WriteHeader()
    {
        if (_csvWriter == null) return;

        _csvLineBuffer.Clear();
        _csvLineBuffer.Append("Frame,TotalMs");

        for (int i = 0; i < _systemNames.Length; i++)
        {
            _csvLineBuffer.Append(',');
            _csvLineBuffer.Append(_systemNames[i]);
        }

        _csvWriter.WriteLine(_csvLineBuffer.ToString());
    }

    private void WriteFrameToCsv(int frame)
    {
        if (_csvWriter == null) return;

        _csvLineBuffer.Clear();
        _csvLineBuffer.Append(frame);
        _csvLineBuffer.Append(',');
        _csvLineBuffer.AppendFormat("{0:F4}", TotalFrameMs);

        for (int i = 0; i < _ticksPerSystem.Length; i++)
        {
            _csvLineBuffer.Append(',');
            _csvLineBuffer.AppendFormat("{0:F4}", _ticksPerSystem[i] * TicksToMs);
        }

        _csvWriter.WriteLine(_csvLineBuffer.ToString());
        _recordedFrameCount++;
    }

    /// <summary>
    /// Reset all timing data.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_ticksPerSystem, 0, _ticksPerSystem.Length);
        Array.Clear(_avgMsPerSystem, 0, _avgMsPerSystem.Length);
        Array.Clear(_tickSums, 0, _tickSums.Length);

        for (int i = 0; i < _systemNames.Length; i++)
        {
            for (int j = 0; j < AvgWindowSize; j++)
            {
                _tickHistory[i, j] = 0;
            }
        }

        _historyIndex = 0;
        _sampleCount = 0;
        TotalFrameMs = 0;
        AverageTotalMs = 0;
    }

    public void Dispose()
    {
        StopRecording();
    }
}
