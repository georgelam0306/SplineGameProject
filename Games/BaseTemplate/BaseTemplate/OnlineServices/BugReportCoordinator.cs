using System;
using System.IO;
using System.Linq;
using R3;
using BaseTemplate.GameApp.AppState;
using Serilog;

namespace BaseTemplate.Infrastructure.Networking;

/// <summary>
/// Coordinates bug report requests from UI with the BugReportService.
/// Subscribes to BugReportRequested events and triggers report creation.
/// </summary>
public sealed class BugReportCoordinator : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<BugReportCoordinator>();

    private readonly BugReportService _bugReportService;
    private readonly IDisposable _subscription;

    public BugReportCoordinator(AppEventBus eventBus, BugReportService bugReportService)
    {
        _bugReportService = bugReportService;

        _subscription = eventBus.BugReportRequested.Subscribe(OnBugReportRequested);
    }

    private void OnBugReportRequested(string? description)
    {
        Log.Information("User requested bug report");

        // Get current replay path
        string? replayPath = GetCurrentReplayPath();
        _bugReportService.CreateUserReport(description, replayPath);
    }

    private static string? GetCurrentReplayPath()
    {
        var logsDir = "Logs";
        if (!Directory.Exists(logsDir)) return null;

        return Directory.GetFiles(logsDir, "replay_*.bin")
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .FirstOrDefault();
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
