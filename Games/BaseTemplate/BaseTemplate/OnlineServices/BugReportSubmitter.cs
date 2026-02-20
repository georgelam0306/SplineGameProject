using DerpTech.DesyncDetection;

namespace BaseTemplate.Infrastructure.Networking;

/// <summary>
/// Wrapper to integrate BugReportService with the shared desync detection system.
/// </summary>
public sealed class BugReportSubmitter : IBugReportSubmitter
{
    private readonly BugReportService? _bugReportService;

    public BugReportSubmitter(BugReportService? bugReportService)
    {
        _bugReportService = bugReportService;
    }

    public void CreateDesyncReport(string desyncExportPath, string? replayPath)
    {
        _bugReportService?.CreateDesyncReport(desyncExportPath, replayPath);
    }
}
