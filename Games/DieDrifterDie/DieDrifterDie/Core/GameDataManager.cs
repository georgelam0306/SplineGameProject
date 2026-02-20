using System;
using System.IO;
using System.Threading;
using Serilog;

namespace DieDrifterDie.GameApp.Core;

/// <summary>
/// Manages data tables with support for loading, hot-reload, and network sync.
/// Thread-safe via immutable table swaps.
///
/// Architecture:
/// - Always loads from binary (.bin) file
/// - Hot-reload: watches JSON files, rebuilds .bin, reloads
/// - Network sync: uses checksum verification
/// </summary>
public sealed class GameDataManager<TDb> : IDisposable where TDb : class
{
    private readonly string _jsonDataPath;
    private readonly string _binPath;
    private readonly Func<string, TDb> _loader;
    private readonly Action<string, string>? _builder;
    private readonly FileSystemWatcher? _watcher;
    private readonly ILogger _log;
    private volatile TDb _db;
    private DateTime _lastReloadTime = DateTime.MinValue;
    private readonly object _reloadLock = new();

    // Scheduled reload for network-synchronized hot-reload
    private volatile int _scheduledReloadFrame = -1;
    private volatile TDb? _pendingDb;
    private volatile bool _dataJustReloaded;
    private int _generation;

    /// <summary>Current database. Thread-safe to read.</summary>
    public TDb Db => _db;

    /// <summary>True if data was reloaded this frame. Check once at frame start.</summary>
    public bool DataJustReloaded => _dataJustReloaded;

    /// <summary>
    /// Monotonically increasing counter that increments on each reload.
    /// Systems compare this to their cached generation to detect staleness.
    /// </summary>
    public int Generation => _generation;

    /// <summary>Clear the reload flag after processing. Called by refresh systems.</summary>
    public void ClearReloadFlag() => _dataJustReloaded = false;

    /// <summary>Fires when a file change is detected (before reload). Used by coordinator to broadcast target frame.</summary>
    public event Action? OnDataChangeDetected;

    /// <summary>Fires when data is reloaded and applied.</summary>
    public event Action? OnDataReloaded;

    /// <summary>Returns true if a reload is scheduled for a future frame.</summary>
    public bool HasPendingReload => _scheduledReloadFrame >= 0;

    /// <summary>
    /// Set to true if this instance is the network coordinator.
    /// Only the coordinator builds binaries; others just load.
    /// </summary>
    public bool IsCoordinator { get; set; } = true;

    /// <summary>
    /// Creates a new GameDataManager.
    /// </summary>
    /// <param name="jsonDataPath">Path to JSON data files (used for hot-reload watching).</param>
    /// <param name="binPath">Path to binary file (used for loading).</param>
    /// <param name="loader">Function to load the database from a binary file path.</param>
    /// <param name="builder">Optional action to build binary from JSON. If null, binary must already exist.</param>
    /// <param name="enableHotReload">Whether to watch JSON files and rebuild .bin on changes.</param>
    /// <param name="logger">Logger instance.</param>
    public GameDataManager(
        string jsonDataPath,
        string binPath,
        Func<string, TDb> loader,
        Action<string, string>? builder = null,
        bool enableHotReload = false,
        ILogger? logger = null)
    {
        _jsonDataPath = jsonDataPath;
        _binPath = binPath;
        _loader = loader;
        _builder = builder;
        _log = logger?.ForContext<GameDataManager<TDb>>() ?? Log.ForContext<GameDataManager<TDb>>();

        _log.Information("JSON source: {JsonDataPath}", jsonDataPath);
        _log.Information("Binary: {BinPath}", binPath);

        // Build binary if needed, with file locking to prevent race conditions
        EnsureBinaryExists(jsonDataPath, binPath);

        // Load from binary (copies to managed arrays, no file lock held)
        _db = _loader(binPath);

        if (enableHotReload)
        {
            _log.Information("Hot-reload enabled, watching: {JsonDataPath}", jsonDataPath);
            _watcher = new FileSystemWatcher(jsonDataPath, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: ignore events within 500ms of last reload
        lock (_reloadLock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastReloadTime).TotalMilliseconds < 500)
                return;
            _lastReloadTime = now;
        }

        // Wait briefly for file to be fully written
        Thread.Sleep(100);

        try
        {
            _log.Information("File changed: {FileName}", e.Name);

            // Only coordinator builds the binary; others just wait and notify
            if (IsCoordinator && _builder != null)
            {
                _log.Debug("Rebuilding binary...");
                _builder(_jsonDataPath, _binPath);
            }

            // Notify listeners (NetworkCoordinator will broadcast target frame)
            OnDataChangeDetected?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Hot-reload failed");
        }
    }

    /// <summary>Reloads all data from disk. Thread-safe. For single-player or immediate reload.</summary>
    public void Reload()
    {
        // Rebuild binary then load
        _builder?.Invoke(_jsonDataPath, _binPath);
        var newDb = _loader(_binPath);

        // Swap
        _db = newDb;
        _generation++;

        _dataJustReloaded = true;
        OnDataReloaded?.Invoke();
    }

    /// <summary>
    /// Schedule a reload to happen at a specific simulation frame.
    /// Called by NetworkCoordinator when receiving GameDataReload message from host.
    /// </summary>
    public void ScheduleReload(int targetFrame)
    {
        lock (_reloadLock)
        {
            // If we don't have pending data yet, load it now
            if (_pendingDb == null)
            {
                // Only coordinator builds; non-coordinators just load what coordinator built
                if (IsCoordinator && _builder != null)
                {
                    _builder(_jsonDataPath, _binPath);
                }
                _pendingDb = _loader(_binPath);
            }

            _scheduledReloadFrame = targetFrame;
            _log.Information("Reload scheduled for frame {TargetFrame}", targetFrame);
        }
    }

    /// <summary>
    /// Check if scheduled reload should be applied at this frame.
    /// Call this from the game loop at the start of each simulation tick.
    /// Returns true if reload was applied.
    /// </summary>
    public bool TryApplyScheduledReload(int currentFrame)
    {
        if (_scheduledReloadFrame < 0 || currentFrame < _scheduledReloadFrame)
            return false;

        lock (_reloadLock)
        {
            if (_scheduledReloadFrame < 0 || currentFrame < _scheduledReloadFrame)
                return false;

            if (_pendingDb != null)
            {
                _db = _pendingDb;
                _pendingDb = null;
                _scheduledReloadFrame = -1;
                _generation++;

                _log.Information("Hot-reload applied at frame {CurrentFrame}", currentFrame);
                _dataJustReloaded = true;
                OnDataReloaded?.Invoke();
                return true;
            }

            _scheduledReloadFrame = -1;
            return false;
        }
    }

    /// <summary>
    /// Get the scheduled reload frame, or -1 if no reload is scheduled.
    /// </summary>
    public int ScheduledReloadFrame => _scheduledReloadFrame;

    public void Dispose()
    {
        _watcher?.Dispose();
    }

    /// <summary>
    /// Ensures binary file exists, building it if needed with proper file locking.
    /// Handles multi-client scenarios where multiple processes may try to build simultaneously.
    /// </summary>
    private void EnsureBinaryExists(string jsonDataPath, string binPath)
    {
        if (File.Exists(binPath))
            return;

        if (_builder == null)
            throw new InvalidOperationException($"Binary file not found and no builder provided: {binPath}");

        // Use a lock file to coordinate between processes
        var lockPath = binPath + ".lock";

        try
        {
            // Try to acquire exclusive lock
            using var lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            // Double-check after acquiring lock (another process may have built it)
            if (File.Exists(binPath))
                return;

            _log.Information("Binary not found, building...");
            _builder(jsonDataPath, binPath);
            _log.Information("Binary built successfully");
        }
        catch (IOException)
        {
            // Another process has the lock - wait for them to finish building
            _log.Debug("Waiting for binary to be built by another process...");
            WaitForFile(binPath, TimeSpan.FromSeconds(30));
        }
        finally
        {
            // Clean up lock file if we created it and binary exists
            try { if (File.Exists(binPath)) File.Delete(lockPath); } catch { }
        }
    }

    /// <summary>
    /// Waits for a file to exist and be readable.
    /// </summary>
    private static void WaitForFile(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                // Try to open to ensure it's not being written
                try
                {
                    using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return;
                }
                catch (IOException)
                {
                    // Still being written, wait more
                }
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Timed out waiting for file: {path}");
    }
}
