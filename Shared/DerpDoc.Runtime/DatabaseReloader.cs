using System.Globalization;

namespace DerpDoc.Runtime;

/// <summary>
/// Simplified hot-reload that works directly with <see cref="BinaryLoader"/>.
/// Polls a live binary sidecar for changes and falls back to a baked binary.
/// </summary>
public sealed class DatabaseReloader : IDisposable
{
    private const long LiveReconnectRetryTicks = TimeSpan.TicksPerMillisecond * 500;

    private readonly string? _liveBinaryPath;
    private readonly string? _bakedBinaryPath;

    private LiveBinaryReader? _liveBinaryReader;
    private BinaryLoader? _currentLoader;
    private long _nextLiveConnectAttemptTicks;
    private uint _lastLiveGeneration;
    private int _reloadCount;
    private bool _disposed;

    public event Action<BinaryLoader>? SnapshotChanged;

    public bool HasData => _currentLoader != null;

    public bool IsLiveConnected => _liveBinaryReader != null;

    public string StatusText { get; private set; }

    public string SourceText { get; private set; }

    public string LastErrorText { get; private set; }

    public DatabaseReloader(string? liveBinaryPath, string? bakedBinaryPath)
    {
        _liveBinaryPath = liveBinaryPath;
        _bakedBinaryPath = bakedBinaryPath;
        StatusText = "Waiting for first database load.";
        SourceText = "Source: awaiting first data load.";
        LastErrorText = string.Empty;
    }

    /// <summary>
    /// Tries to load data immediately: live first, then baked fallback.
    /// </summary>
    public void LoadInitial()
    {
        if (_liveBinaryPath != null && TryEnsureLiveReaderOpen() && TryRefreshFromLive(forceReload: true))
        {
            return;
        }

        TryLoadBaked();
    }

    /// <summary>
    /// Polls the live binary for generation changes. One uint read when no change.
    /// </summary>
    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        if (_liveBinaryPath == null)
        {
            return;
        }

        if (!TryEnsureLiveReaderOpen())
        {
            return;
        }

        TryRefreshFromLive(forceReload: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _liveBinaryReader?.Dispose();
        _liveBinaryReader = null;
        _currentLoader?.Dispose();
        _currentLoader = null;
    }

    private bool TryEnsureLiveReaderOpen()
    {
        if (_liveBinaryReader != null)
        {
            return true;
        }

        if (_liveBinaryPath == null)
        {
            return false;
        }

        long nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks < _nextLiveConnectAttemptTicks)
        {
            return false;
        }

        if (!File.Exists(_liveBinaryPath))
        {
            _nextLiveConnectAttemptTicks = nowTicks + LiveReconnectRetryTicks;
            return false;
        }

        try
        {
            _liveBinaryReader = LiveBinaryReader.Open(_liveBinaryPath);
            return true;
        }
        catch (Exception exception)
        {
            HandleLiveError(exception);
            return false;
        }
    }

    private bool TryRefreshFromLive(bool forceReload)
    {
        if (_liveBinaryReader == null)
        {
            return false;
        }

        try
        {
            LiveBinaryHeader liveHeader = _liveBinaryReader.ReadHeader();
            if (liveHeader.Magic != LiveBinaryHeader.MagicValue)
            {
                throw new InvalidDataException(
                    $"Invalid live binary header. Expected magic {LiveBinaryHeader.MagicValue:X8}, got {liveHeader.Magic:X8}.");
            }

            if (!forceReload && liveHeader.Generation == _lastLiveGeneration)
            {
                return false;
            }

            if (liveHeader.SlotSize <= 0)
            {
                throw new InvalidDataException($"Invalid live slot size: {liveHeader.SlotSize}.");
            }

            long activeSlotOffset = liveHeader.ActiveSlot == 0
                ? liveHeader.Slot0Offset
                : liveHeader.Slot1Offset;

            BinaryLoader newLoader = BinaryLoader.Load(_liveBinaryPath!, activeSlotOffset, liveHeader.SlotSize);
            PublishReload(newLoader, isLive: true, liveHeader.Generation);
            SourceText = "Source: live memory-mapped database (generation " +
                _lastLiveGeneration.ToString(CultureInfo.InvariantCulture) + ").";
            LastErrorText = string.Empty;
            StatusText = $"Loaded database from live binary (generation {_lastLiveGeneration}, reload {_reloadCount}).";
            return true;
        }
        catch (Exception exception)
        {
            HandleLiveError(exception);
            return false;
        }
    }

    private bool TryLoadBaked()
    {
        if (_bakedBinaryPath == null)
        {
            return false;
        }

        try
        {
            if (!File.Exists(_bakedBinaryPath))
            {
                StatusText = "No database binary found.";
                return false;
            }

            BinaryLoader newLoader = BinaryLoader.Load(_bakedBinaryPath);
            PublishReload(newLoader, isLive: false, liveGeneration: 0u);
            SourceText = "Source: baked .derpdoc binary (waiting for live sidecar).";
            LastErrorText = string.Empty;
            StatusText = $"Loaded database from baked binary (reload {_reloadCount}).";
            return true;
        }
        catch (Exception exception)
        {
            LastErrorText = exception.Message;
            StatusText = "Database reload failed.";
            return false;
        }
    }

    private void PublishReload(BinaryLoader newLoader, bool isLive, uint liveGeneration)
    {
        BinaryLoader? previousLoader = _currentLoader;

        try
        {
            SnapshotChanged?.Invoke(newLoader);
        }
        catch
        {
            newLoader.Dispose();
            throw;
        }

        _currentLoader = newLoader;
        _lastLiveGeneration = isLive ? liveGeneration : 0u;
        _reloadCount++;
        previousLoader?.Dispose();
    }

    private void HandleLiveError(Exception exception)
    {
        LastErrorText = exception.Message;
        StatusText = "Live reload failed; keeping current snapshot.";
        SourceText = "Source: live sidecar unavailable (using latest loaded snapshot).";
        _liveBinaryReader?.Dispose();
        _liveBinaryReader = null;
        _nextLiveConnectAttemptTicks = DateTime.UtcNow.Ticks + LiveReconnectRetryTicks;
    }
}
