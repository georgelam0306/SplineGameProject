using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Profiling
{
    /// <summary>
    /// Zero-allocation profiling service for collecting timing data.
    /// Stores rolling averages and supports CSV export.
    /// </summary>
    /// <remarks>
    /// Thread-safety: This class is designed for single-threaded game loop usage.
    /// The static Instance property should be set once during initialization.
    /// </remarks>
    public sealed class ProfilingService : IDisposable
    {
        /// <summary>
        /// Global instance for use by generated ProfileScope code.
        /// Set this during application startup before any profiling occurs.
        /// </summary>
        public static ProfilingService? Instance { get; set; }

        // Pre-allocated arrays (zero allocation during measurement)
        private readonly long[] _ticksPerScope;         // Current frame ticks per scope
        private readonly double[] _lastMsPerScope;      // Last completed frame ms per scope
        private readonly double[] _avgMsPerScope;       // Rolling average in milliseconds
        private readonly string[] _scopeNames;          // Scope names (from ProfileScopes.Names)

        // Rolling average state
        private const int AvgWindowSize = 60;           // 1 second at 60fps
        private readonly long[,] _tickHistory;          // [scopeIndex, historySlot]
        private readonly long[] _tickSums;              // Running sum for fast average
        private int _historyIndex;
        private int _sampleCount;

        // Wall-clock frame timing (covers the whole frame, not just scoped work)
        private long _currentFrameWallTicks;
        private readonly long[] _wallTickHistory;
        private long _wallTickSum;

        // Conversion factor
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        // Configuration
        /// <summary>Enable or disable timing collection.</summary>
        public bool Enabled { get; set; }

        /// <summary>Whether to show the profiler overlay.</summary>
        public bool ShowOverlay { get; set; }

        // CSV export
        private StreamWriter? _csvWriter;
        private readonly StringBuilder _csvLineBuffer;
        private bool _isRecording;
        private int _recordedFrameCount;

        /// <summary>Whether CSV recording is active.</summary>
        public bool IsRecording => _isRecording;

        /// <summary>Number of frames recorded to CSV.</summary>
        public int RecordedFrameCount => _recordedFrameCount;

        /// <summary>Total simulation time for current frame in milliseconds.</summary>
        public double TotalFrameMs { get; private set; }

        /// <summary>Average total simulation time in milliseconds.</summary>
        public double AverageTotalMs { get; private set; }

        /// <summary>
        /// Total wall-clock time for the last completed frame in milliseconds.
        /// This includes all work done on the main thread between the engine's frame begin/end.
        /// </summary>
        public double LastFrameWallMs { get; private set; }

        /// <summary>Rolling average wall-clock frame time in milliseconds.</summary>
        public double AverageFrameWallMs { get; private set; }

        /// <summary>Number of registered scopes.</summary>
        public int ScopeCount { get; }

        /// <summary>Get the cached scope names.</summary>
        public ReadOnlySpan<string> ScopeNames => _scopeNames;

        /// <summary>Get rolling average milliseconds per scope.</summary>
        public ReadOnlySpan<double> AverageMilliseconds => _avgMsPerScope;

        /// <summary>Get last completed frame milliseconds per scope.</summary>
        public ReadOnlySpan<double> LastFrameMilliseconds => _lastMsPerScope;

        /// <summary>
        /// Creates a new ProfilingService with the given scope names.
        /// </summary>
        /// <param name="scopeNames">Array of scope names (typically ProfileScopes.Names).</param>
        public ProfilingService(string[] scopeNames)
        {
            ScopeCount = scopeNames.Length;
            _scopeNames = new string[ScopeCount];
            Array.Copy(scopeNames, _scopeNames, ScopeCount);

            _ticksPerScope = new long[ScopeCount];
            _lastMsPerScope = new double[ScopeCount];
            _avgMsPerScope = new double[ScopeCount];
            _tickHistory = new long[ScopeCount, AvgWindowSize];
            _tickSums = new long[ScopeCount];
            _csvLineBuffer = new StringBuilder(4096);

            _wallTickHistory = new long[AvgWindowSize];

            // Set as global instance
            Instance = this;
        }

        /// <summary>
        /// Record elapsed ticks for a scope. Called by generated ProfileScope.Dispose().
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordTiming(int scopeId, long elapsedTicks)
        {
            if (scopeId >= 0 && scopeId < ScopeCount)
            {
                _ticksPerScope[scopeId] += elapsedTicks;
            }
        }

        /// <summary>
        /// Provide the wall-clock ticks for the current frame.
        /// This must be called once per frame before EndFrame().
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCurrentFrameWallTicks(long frameWallTicks)
        {
            _currentFrameWallTicks = frameWallTicks;
        }

        /// <summary>
        /// Called at the end of each frame to update rolling averages.
        /// </summary>
        /// <param name="frame">Current frame number (for CSV recording).</param>
        public void EndFrame(int frame)
        {
            // Calculate total and update rolling averages
            long totalTicks = 0;

            for (int i = 0; i < ScopeCount; i++)
            {
                long ticks = _ticksPerScope[i];
                totalTicks += ticks;
                _lastMsPerScope[i] = ticks * TicksToMs;

                // Update rolling sum: subtract old value, add new
                int oldSlot = _historyIndex;
                _tickSums[i] -= _tickHistory[i, oldSlot];
                _tickSums[i] += ticks;
                _tickHistory[i, oldSlot] = ticks;

                // Calculate average
                int samples = Math.Min(_sampleCount + 1, AvgWindowSize);
                _avgMsPerScope[i] = (_tickSums[i] / (double)samples) * TicksToMs;
            }

            TotalFrameMs = totalTicks * TicksToMs;

            // Update average total
            if (_sampleCount < AvgWindowSize)
            {
                _sampleCount++;
            }

            long totalSum = 0;
            for (int i = 0; i < ScopeCount; i++)
            {
                totalSum += _tickSums[i];
            }
            AverageTotalMs = (totalSum / (double)Math.Min(_sampleCount, AvgWindowSize)) * TicksToMs;

            // Update wall-clock frame time and rolling average (separate from scoped total)
            long wallTicks = _currentFrameWallTicks;
            LastFrameWallMs = wallTicks * TicksToMs;

            int wallOldSlot = _historyIndex;
            _wallTickSum -= _wallTickHistory[wallOldSlot];
            _wallTickSum += wallTicks;
            _wallTickHistory[wallOldSlot] = wallTicks;
            AverageFrameWallMs = (_wallTickSum / (double)Math.Min(_sampleCount, AvgWindowSize)) * TicksToMs;

            // Advance history index
            _historyIndex = (_historyIndex + 1) % AvgWindowSize;

            // Write to CSV if recording
            if (_isRecording && _csvWriter != null)
            {
                WriteFrameToCsv(frame);
            }

            // Reset current frame ticks
            Array.Clear(_ticksPerScope, 0, ScopeCount);
            _currentFrameWallTicks = 0;
        }

        /// <summary>
        /// Start recording timing data to a CSV file.
        /// </summary>
        /// <param name="filePath">Path to the CSV file.</param>
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

            for (int i = 0; i < ScopeCount; i++)
            {
                _csvLineBuffer.Append(',');
                _csvLineBuffer.Append(_scopeNames[i]);
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

            for (int i = 0; i < ScopeCount; i++)
            {
                _csvLineBuffer.Append(',');
                _csvLineBuffer.AppendFormat("{0:F4}", _ticksPerScope[i] * TicksToMs);
            }

            _csvWriter.WriteLine(_csvLineBuffer.ToString());
            _recordedFrameCount++;
        }

        /// <summary>
        /// Reset all timing data.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_ticksPerScope, 0, ScopeCount);
            Array.Clear(_lastMsPerScope, 0, ScopeCount);
            Array.Clear(_avgMsPerScope, 0, ScopeCount);
            Array.Clear(_tickSums, 0, ScopeCount);
            Array.Clear(_wallTickHistory, 0, AvgWindowSize);
            _wallTickSum = 0;

            for (int i = 0; i < ScopeCount; i++)
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
            LastFrameWallMs = 0;
            AverageFrameWallMs = 0;
            _currentFrameWallTicks = 0;
        }

        /// <summary>
        /// Dispose the service and stop recording.
        /// </summary>
        public void Dispose()
        {
            StopRecording();

            // Clear static instance if this is it
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
