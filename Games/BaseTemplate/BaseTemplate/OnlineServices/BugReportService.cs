using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace BaseTemplate.Infrastructure.Networking;

/// <summary>
/// Handles bug report collection, local storage, and background upload.
/// Thread-safe for crash handling scenarios.
/// </summary>
public sealed class BugReportService : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<BugReportService>();

    private const string LocalReportsDir = "Logs/reports";
    private const string ServerUrl = "http://45.76.79.231:5052/api/reports";
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 5000;

    private readonly HttpClient _httpClient;
    private readonly ConcurrentQueue<string> _pendingUploads = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _uploadTask;
    private bool _disposed;

    /// <summary>
    /// Game version string (set at startup).
    /// </summary>
    public static string GameVersion { get; set; } = "dev";

    public BugReportService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        Directory.CreateDirectory(LocalReportsDir);

        // Queue any pending reports from previous sessions
        if (Directory.Exists(LocalReportsDir))
        {
            foreach (var dir in Directory.GetDirectories(LocalReportsDir))
            {
                if (File.Exists(Path.Combine(dir, "pending")))
                {
                    _pendingUploads.Enqueue(dir);
                }
            }
        }

        // Start background upload task
        _uploadTask = Task.Run(ProcessPendingUploadsAsync);
    }

    /// <summary>
    /// Create a bug report from a crash exception. Called from exception handlers.
    /// Designed to be as safe as possible - minimal allocations, no exceptions thrown.
    /// </summary>
    public void CreateCrashReport(Exception exception, string? currentReplayPath = null)
    {
        try
        {
            CreateReportInternal(
                isCrash: true,
                description: null,
                exception: exception,
                currentReplayPath: currentReplayPath,
                desyncExportPath: null);
        }
        catch
        {
            // Swallow all exceptions - we're already in crash handling
        }
    }

    /// <summary>
    /// Create a user-initiated bug report.
    /// </summary>
    public void CreateUserReport(string? description, string? currentReplayPath = null)
    {
        CreateReportInternal(
            isCrash: false,
            description: description,
            exception: null,
            currentReplayPath: currentReplayPath,
            desyncExportPath: null);
    }

    /// <summary>
    /// Create an automatic desync report with the desync debug export.
    /// </summary>
    public void CreateDesyncReport(string desyncExportPath, string? currentReplayPath = null)
    {
        CreateReportInternal(
            isCrash: false,
            description: "Automatic desync detection",
            exception: null,
            currentReplayPath: currentReplayPath,
            desyncExportPath: desyncExportPath);
    }

    private void CreateReportInternal(bool isCrash, string? description, Exception? exception, string? currentReplayPath, string? desyncExportPath)
    {
        var reportId = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        var reportDir = Path.Combine(LocalReportsDir, reportId);
        Directory.CreateDirectory(reportDir);

        // Collect system info
        var systemInfo = new SystemInfo
        {
            Platform = GetPlatformString(),
            OsVersion = Environment.OSVersion.ToString(),
            MemoryUsageMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024),
            GameVersion = GameVersion,
            DotNetVersion = Environment.Version.ToString()
        };

        // Write system info
        File.WriteAllText(Path.Combine(reportDir, "system_info.txt"), systemInfo.ToString());

        // Write exception if crash
        if (exception != null)
        {
            File.WriteAllText(Path.Combine(reportDir, "exception.txt"),
                $"Type: {exception.GetType().FullName}\n" +
                $"Message: {exception.Message}\n\n" +
                $"Stack Trace:\n{exception.StackTrace}");

            // Write inner exceptions
            var inner = exception.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                File.WriteAllText(Path.Combine(reportDir, $"inner_exception_{depth}.txt"),
                    $"Type: {inner.GetType().FullName}\n" +
                    $"Message: {inner.Message}\n\n" +
                    $"Stack Trace:\n{inner.StackTrace}");
                inner = inner.InnerException;
                depth++;
            }
        }

        // Write description if user report
        if (!string.IsNullOrEmpty(description))
        {
            File.WriteAllText(Path.Combine(reportDir, "description.txt"), description);
        }

        // Copy replay file
        if (!string.IsNullOrEmpty(currentReplayPath) && File.Exists(currentReplayPath))
        {
            try
            {
                File.Copy(currentReplayPath, Path.Combine(reportDir, Path.GetFileName(currentReplayPath)));
            }
            catch { /* Ignore copy failures */ }
        }

        // Copy desync export file
        if (!string.IsNullOrEmpty(desyncExportPath) && File.Exists(desyncExportPath))
        {
            try
            {
                File.Copy(desyncExportPath, Path.Combine(reportDir, Path.GetFileName(desyncExportPath)));
            }
            catch { /* Ignore copy failures */ }
        }

        // Copy log file
        CopyLogFile(reportDir);

        // Copy log ring buffer
        BaseTemplate.GameApp.Core.Logging.WriteRingBufferToFile(Path.Combine(reportDir, "recent_logs.txt"));

        // Write metadata
        var metadata = new ReportMetadata
        {
            ReportId = reportId,
            IsCrashReport = isCrash,
            IsDesyncReport = !string.IsNullOrEmpty(desyncExportPath),
            Description = description,
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception?.Message,
            SystemInfo = systemInfo,
            CreatedAt = DateTime.UtcNow
        };
        File.WriteAllText(Path.Combine(reportDir, "metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

        // Create zip archive
        var zipPath = reportDir + ".zip";
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        ZipFile.CreateFromDirectory(reportDir, zipPath);

        // Mark as pending upload
        File.WriteAllText(Path.Combine(reportDir, "pending"), "");

        // Queue for upload
        _pendingUploads.Enqueue(reportDir);

        Log.Information("Bug report created: {ReportId} (crash={IsCrash})", reportId, isCrash);
    }

    private void CopyLogFile(string reportDir)
    {
        try
        {
            var logPath = "Logs/game.log";
            if (File.Exists(logPath))
            {
                // Copy with sharing to allow concurrent writes
                using var source = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dest = File.Create(Path.Combine(reportDir, "game.log"));
                source.CopyTo(dest);
            }
        }
        catch { /* Ignore */ }
    }

    private async Task ProcessPendingUploadsAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_pendingUploads.TryDequeue(out var reportDir))
                {
                    await UploadReportAsync(reportDir);
                }
                else
                {
                    await Task.Delay(5000, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning("Bug report upload error: {Error}", ex.Message);
                await Task.Delay(10000, _cts.Token);
            }
        }
    }

    private async Task UploadReportAsync(string reportDir)
    {
        var zipPath = reportDir + ".zip";
        if (!File.Exists(zipPath))
        {
            // Remove pending marker if zip doesn't exist
            var pendingPath = Path.Combine(reportDir, "pending");
            if (File.Exists(pendingPath)) File.Delete(pendingPath);
            return;
        }

        var metadataPath = Path.Combine(reportDir, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            return;
        }

        var metadataJson = await File.ReadAllTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<ReportMetadata>(metadataJson);
        if (metadata == null) return;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var content = new MultipartFormDataContent();

                // Add metadata fields
                content.Add(new StringContent(metadata.SystemInfo?.GameVersion ?? ""), "gameVersion");
                content.Add(new StringContent(metadata.SystemInfo?.Platform ?? ""), "platform");
                content.Add(new StringContent(metadata.Description ?? ""), "description");
                content.Add(new StringContent(metadata.IsCrashReport.ToString()), "isCrashReport");
                content.Add(new StringContent(metadata.ExceptionType ?? ""), "exceptionType");
                content.Add(new StringContent(metadata.ExceptionMessage ?? ""), "exceptionMessage");
                content.Add(new StringContent(metadata.SystemInfo?.MemoryUsageMb.ToString() ?? "0"), "memoryUsageMb");
                content.Add(new StringContent(metadata.SystemInfo?.OsVersion ?? ""), "osVersion");

                // Add zip file
                var zipBytes = await File.ReadAllBytesAsync(zipPath);
                content.Add(new ByteArrayContent(zipBytes), "report", Path.GetFileName(zipPath));

                var response = await _httpClient.PostAsync(ServerUrl, content, _cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    // Remove pending marker
                    var pendingPath = Path.Combine(reportDir, "pending");
                    if (File.Exists(pendingPath)) File.Delete(pendingPath);

                    Log.Information("Bug report uploaded: {ReportId}", metadata.ReportId);
                    return;
                }

                Log.Warning("Bug report upload failed (attempt {Attempt}): {Status}",
                    attempt + 1, response.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Warning("Bug report upload error (attempt {Attempt}): {Error}",
                    attempt + 1, ex.Message);
            }

            if (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelayMs * (attempt + 1), _cts.Token);
            }
        }

        // Re-queue for later retry
        _pendingUploads.Enqueue(reportDir);
    }

    private static string GetPlatformString()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "win-x64" : "win-x86";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "linux-x64" : "linux-arm64";
        return "unknown";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Wait for pending uploads to complete (with timeout), then cancel
        if (_uploadTask != null && !_uploadTask.IsCompleted)
        {
            Log.Information("Waiting for bug report uploads to complete...");
            if (!_uploadTask.Wait(TimeSpan.FromSeconds(5)))
            {
                Log.Warning("Bug report upload timed out during shutdown");
                _cts.Cancel();
            }
        }

        _httpClient.Dispose();
        _cts.Dispose();
    }

    private sealed class SystemInfo
    {
        public string Platform { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public long MemoryUsageMb { get; set; }
        public string GameVersion { get; set; } = "";
        public string DotNetVersion { get; set; } = "";

        public override string ToString() =>
            $"Platform: {Platform}\n" +
            $"OS Version: {OsVersion}\n" +
            $"Memory Usage: {MemoryUsageMb} MB\n" +
            $"Game Version: {GameVersion}\n" +
            $".NET Version: {DotNetVersion}";
    }

    private sealed class ReportMetadata
    {
        public string ReportId { get; set; } = "";
        public bool IsCrashReport { get; set; }
        public bool IsDesyncReport { get; set; }
        public string? Description { get; set; }
        public string? ExceptionType { get; set; }
        public string? ExceptionMessage { get; set; }
        public SystemInfo? SystemInfo { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
