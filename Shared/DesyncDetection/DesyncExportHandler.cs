using DerpTech.Rollback;
using Serilog;

namespace DerpTech.DesyncDetection;

/// <summary>
/// Handles desync debug export on the main thread.
/// The background validator detects desyncs, but export must happen on main thread
/// to safely call RestoreSnapshot without race conditions.
/// </summary>
public sealed class DesyncExportHandler<TInput>
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    private readonly ILogger _logger;
    private readonly IDesyncExportable _simWorld;
    private readonly RollbackManager<TInput> _rollbackManager;
    private readonly IGameSimulation _gameSimulation;
    private readonly IDerivedSystemRunner _derivedRunner;
    private readonly IInputExporter<TInput>? _inputExporter;
    private readonly IBugReportSubmitter? _bugReportSubmitter;

    private DesyncInfo? _pendingExport;
    private byte _localPlayerId;
    private bool _exported;

    public DesyncExportHandler(
        IDesyncExportable simWorld,
        RollbackManager<TInput> rollbackManager,
        IGameSimulation gameSimulation,
        IDerivedSystemRunner derivedRunner,
        IInputExporter<TInput>? inputExporter,
        IBugReportSubmitter? bugReportSubmitter,
        ILogger logger)
    {
        _simWorld = simWorld;
        _rollbackManager = rollbackManager;
        _gameSimulation = gameSimulation;
        _derivedRunner = derivedRunner;
        _inputExporter = inputExporter;
        _bugReportSubmitter = bugReportSubmitter;
        _logger = logger;
    }

    public void SetLocalPlayerId(byte playerId)
    {
        _localPlayerId = playerId;
    }

    /// <summary>
    /// Queue a desync for export. Called when desync is detected.
    /// </summary>
    public void QueueExport(DesyncInfo desyncInfo)
    {
        if (_exported) return; // Only export first desync
        _pendingExport = desyncInfo;
    }

    /// <summary>
    /// Process pending export on the main thread.
    /// Safe to call RestoreSnapshot here.
    /// </summary>
    public void ProcessPendingExport()
    {
        if (_pendingExport == null || _exported) return;

        var desync = _pendingExport.Value;
        _exported = true;
        _pendingExport = null;

        _logger.Information("Processing desync export on main thread for frame {Frame}", desync.Frame);

        try
        {
            // Re-simulate with per-system hashing to identify which system caused desync
            RunPerSystemHashResimulation(desync.Frame, _gameSimulation.CurrentFrame);

            string? exportPath = DesyncDebugExporter<TInput>.ExportDesyncState(
                _simWorld,
                _rollbackManager,
                _gameSimulation,
                _derivedRunner,
                _inputExporter,
                desync.Frame,
                _gameSimulation.CurrentFrame,
                desync.LocalHash,
                desync.RemoteHash,
                _localPlayerId
            );

            // Auto-submit bug report if export succeeded and submitter is available
            if (!string.IsNullOrEmpty(exportPath) && _bugReportSubmitter != null)
            {
                SubmitDesyncBugReport(exportPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export desync debug on main thread");
        }
    }

    /// <summary>
    /// Submit a bug report for the desync with the export file and replay.
    /// </summary>
    private void SubmitDesyncBugReport(string exportPath)
    {
        try
        {
            // Find the most recent replay file
            string? replayPath = GetCurrentReplayPath();

            _bugReportSubmitter!.CreateDesyncReport(exportPath, replayPath);
            _logger.Information("Desync bug report submitted automatically");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to submit desync bug report");
        }
    }

    private static string? GetCurrentReplayPath()
    {
        const string logsDir = "Logs";
        if (!Directory.Exists(logsDir)) return null;

        return Directory.GetFiles(logsDir, "replay_*.bin")
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .FirstOrDefault();
    }

    /// <summary>
    /// Re-simulate the desync frame with per-system hash tracking.
    /// Restores snapshot to frame before desync, then runs one tick with hashing.
    /// </summary>
    private void RunPerSystemHashResimulation(int desyncFrame, int currentFrame)
    {
        // We need the snapshot from the frame BEFORE desync to resimulate the desync frame
        int snapshotFrame = desyncFrame - 1;
        if (snapshotFrame < 0) snapshotFrame = 0;

        try
        {
            // Restore to frame before desync
            _rollbackManager.RestoreSnapshot(snapshotFrame, currentFrame);
            _derivedRunner.InvalidateAll();
            _logger.Information("Restored snapshot at frame {Frame} for per-system hash resimulation", snapshotFrame);

            // Run the desync frame with per-system hashing
            _gameSimulation.SimulateTickWithHashing(desyncFrame);

            _logger.Information("Per-system hash resimulation complete for frame {Frame}", desyncFrame);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to run per-system hash resimulation");
        }
    }

    /// <summary>
    /// Reset state for new match.
    /// </summary>
    public void Clear()
    {
        _pendingExport = null;
        _exported = false;
    }

    public bool HasPendingExport => _pendingExport != null;
    public bool HasExported => _exported;
}
