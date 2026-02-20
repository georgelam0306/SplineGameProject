using BugReportContracts.Grains;
using BugReportContracts.Models;

namespace DerpTech2D.BugReportServer.Grains;

/// <summary>
/// Grain for managing individual bug report metadata.
/// Keyed by report ID.
/// </summary>
public sealed class BugReportGrain : Grain, IBugReportGrain
{
    private BugReportMetadata? _metadata;

    public Task<BugReportMetadata?> GetMetadata()
    {
        return Task.FromResult(_metadata);
    }

    public Task SetMetadata(BugReportMetadata metadata)
    {
        _metadata = metadata;
        return Task.CompletedTask;
    }

    public Task SetStatus(BugReportStatus status)
    {
        if (_metadata != null)
        {
            _metadata.Status = status;
        }
        return Task.CompletedTask;
    }
}
