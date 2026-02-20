using System;
using System.Collections.Generic;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Catrillion.Core;

/// <summary>
/// Centralized Serilog configuration with file sink and ring buffer for bug reports.
/// </summary>
public static class Logging
{
    private static bool _isInitialized;
    private static RingBufferSink? _ringBufferSink;

    private const int RingBufferSize = 500; // Keep last 500 log entries
    private const string LogFilePath = "Logs/game.log";
    private const long MaxLogFileSize = 10 * 1024 * 1024; // 10 MB
    private const int RetainedFileCount = 3;

    /// <summary>
    /// Initialize Serilog with Console sink, file sink, and ring buffer.
    /// Call once at application startup before any logging occurs.
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;

        // Ensure Logs directory exists
        Directory.CreateDirectory("Logs");

        // Create ring buffer sink for bug reports
        _ringBufferSink = new RingBufferSink(RingBufferSize);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
#if !DEBUG
            .MinimumLevel.Override("Catrillion", LogEventLevel.Information)
#endif
            .WriteTo.Console(
                outputTemplate: "[{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                path: LogFilePath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: MaxLogFileSize,
                retainedFileCountLimit: RetainedFileCount,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                shared: true,  // Allow reading while writing
                flushToDiskInterval: TimeSpan.FromSeconds(1)
            )
            .WriteTo.Sink(_ringBufferSink);

        Log.Logger = configuration.CreateLogger();
        _isInitialized = true;
    }

    /// <summary>
    /// Write the ring buffer contents to a file for bug reports.
    /// Safe to call during crash handling.
    /// </summary>
    public static void WriteRingBufferToFile(string filePath)
    {
        if (_ringBufferSink == null) return;

        try
        {
            var entries = _ringBufferSink.GetEntries();
            using var writer = new StreamWriter(filePath);
            foreach (var entry in entries)
            {
                writer.WriteLine(entry);
            }
        }
        catch
        {
            // Ignore errors during crash handling
        }
    }

    /// <summary>
    /// Flush and close the logger. Call at application shutdown.
    /// </summary>
    public static void Shutdown() => Log.CloseAndFlush();
}

/// <summary>
/// Ring buffer sink that keeps the last N log entries in memory.
/// Thread-safe for concurrent logging.
/// </summary>
internal sealed class RingBufferSink : ILogEventSink
{
    private readonly string[] _buffer;
    private readonly object _lock = new();
    private int _head;
    private int _count;

    public RingBufferSink(int capacity)
    {
        _buffer = new string[capacity];
    }

    public void Emit(LogEvent logEvent)
    {
        var message = FormatLogEvent(logEvent);

        lock (_lock)
        {
            _buffer[_head] = message;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }
    }

    public IReadOnlyList<string> GetEntries()
    {
        lock (_lock)
        {
            var result = new List<string>(_count);
            int start = _count < _buffer.Length ? 0 : _head;
            for (int i = 0; i < _count; i++)
            {
                int index = (start + i) % _buffer.Length;
                if (_buffer[index] != null)
                    result.Add(_buffer[index]);
            }
            return result;
        }
    }

    private static string FormatLogEvent(LogEvent logEvent)
    {
        var context = "";
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            context = $"[{sourceContext}] ";
        }

        var message = logEvent.RenderMessage();
        var exception = logEvent.Exception != null ? $"\n{logEvent.Exception}" : "";

        return $"{logEvent.Timestamp:HH:mm:ss.fff} [{logEvent.Level:u3}] {context}{message}{exception}";
    }
}
