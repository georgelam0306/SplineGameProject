namespace DerpTech.DesyncDetection;

/// <summary>
/// Interface for submitting desync bug reports.
/// Optional - allows games to hook into their bug reporting infrastructure.
/// </summary>
public interface IBugReportSubmitter
{
    /// <summary>
    /// Creates a bug report for a desync event.
    /// </summary>
    /// <param name="desyncExportPath">Path to the JSON export file.</param>
    /// <param name="replayPath">Optional path to the replay file.</param>
    void CreateDesyncReport(string desyncExportPath, string? replayPath);
}
