using BugReportContracts.Grains;
using BugReportContracts.Models;

namespace DerpTech2D.BugReportServer.Grains;

/// <summary>
/// Singleton grain for indexing and listing bug reports.
/// Uses key 0.
/// </summary>
public sealed class BugReportIndexGrain : Grain, IBugReportIndexGrain
{
    private readonly List<BugReportListItem> _reports = new();
    private const int MaxReportsInMemory = 1000;

    public Task RegisterReport(string reportId, BugReportMetadata metadata)
    {
        var listItem = new BugReportListItem
        {
            ReportId = reportId,
            SubmittedAt = metadata.SubmittedAt,
            IsCrashReport = metadata.IsCrashReport,
            ExceptionType = metadata.ExceptionType,
            Status = metadata.Status,
            Description = metadata.Description?.Length > 100
                ? metadata.Description[..100] + "..."
                : metadata.Description,
            Platform = metadata.Platform,
            GameVersion = metadata.GameVersion,
            GitCommitHash = metadata.GitCommitHash
        };

        _reports.Insert(0, listItem);

        // Trim if over limit
        if (_reports.Count > MaxReportsInMemory)
        {
            _reports.RemoveRange(MaxReportsInMemory, _reports.Count - MaxReportsInMemory);
        }

        return Task.CompletedTask;
    }

    public Task UpdateStatus(string reportId, BugReportStatus status)
    {
        var report = _reports.FirstOrDefault(r => r.ReportId == reportId);
        if (report != null)
        {
            report.Status = status;
        }
        return Task.CompletedTask;
    }

    public Task<List<BugReportListItem>> GetRecentReports(int count = 50)
    {
        var result = _reports.Take(count).ToList();
        return Task.FromResult(result);
    }

    public Task<List<BugReportListItem>> GetCrashReports(int count = 50)
    {
        var result = _reports.Where(r => r.IsCrashReport).Take(count).ToList();
        return Task.FromResult(result);
    }
}
