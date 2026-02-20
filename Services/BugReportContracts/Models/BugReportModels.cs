namespace BugReportContracts.Models;

[GenerateSerializer]
public sealed class BugReportMetadata
{
    [Id(0)] public string ReportId { get; set; } = string.Empty;
    [Id(1)] public DateTime SubmittedAt { get; set; }
    [Id(2)] public string GameVersion { get; set; } = string.Empty;
    [Id(3)] public string Platform { get; set; } = string.Empty;  // win-x64, osx-arm64, linux-x64
    [Id(4)] public string? Description { get; set; }
    [Id(5)] public bool IsCrashReport { get; set; }
    [Id(6)] public string? ExceptionType { get; set; }
    [Id(7)] public string? ExceptionMessage { get; set; }
    [Id(8)] public long MemoryUsageMb { get; set; }
    [Id(9)] public string OsVersion { get; set; } = string.Empty;
    [Id(10)] public List<string> AttachedFiles { get; set; } = new();
    [Id(11)] public BugReportStatus Status { get; set; }
    [Id(12)] public string? GitCommitHash { get; set; }
}

[GenerateSerializer]
public enum BugReportStatus
{
    New,
    Reviewed,
    Resolved,
    Invalid
}

[GenerateSerializer]
public sealed class BugReportListItem
{
    [Id(0)] public string ReportId { get; set; } = string.Empty;
    [Id(1)] public DateTime SubmittedAt { get; set; }
    [Id(2)] public bool IsCrashReport { get; set; }
    [Id(3)] public string? ExceptionType { get; set; }
    [Id(4)] public BugReportStatus Status { get; set; }
    [Id(5)] public string? Description { get; set; }
    [Id(6)] public string? Platform { get; set; }
    [Id(7)] public string? GameVersion { get; set; }
    [Id(8)] public string? GitCommitHash { get; set; }
}
