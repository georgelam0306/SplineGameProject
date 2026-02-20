using BugReportContracts.Models;

namespace BugReportContracts.Grains;

/// <summary>
/// Grain for managing individual bug report metadata.
/// </summary>
public interface IBugReportGrain : IGrainWithStringKey
{
    Task<BugReportMetadata?> GetMetadata();
    Task SetMetadata(BugReportMetadata metadata);
    Task SetStatus(BugReportStatus status);
}

/// <summary>
/// Singleton grain for indexing and listing bug reports.
/// </summary>
public interface IBugReportIndexGrain : IGrainWithIntegerKey
{
    Task RegisterReport(string reportId, BugReportMetadata metadata);
    Task UpdateStatus(string reportId, BugReportStatus status);
    Task<List<BugReportListItem>> GetRecentReports(int count = 50);
    Task<List<BugReportListItem>> GetCrashReports(int count = 50);
}
