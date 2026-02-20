using System.Buffers;
using System.Collections.Concurrent;
using Serilog;

namespace DerpTech.DesyncDetection;

/// <summary>
/// Background service for desync detection.
/// Runs hash computation and sync checks on a background thread.
/// Export is handled separately on the main thread via DesyncExportHandler.
/// </summary>
public sealed class BackgroundSyncValidator : IDisposable
{
    private readonly ILogger _logger;
    private readonly Thread _workerThread;
    private readonly BlockingCollection<WorkItem> _workQueue;
    private readonly ConcurrentDictionary<int, ulong> _computedHashes;
    private readonly ConcurrentQueue<DesyncInfo> _desyncResults;
    private readonly ConcurrentQueue<PendingSyncCheck> _pendingSyncChecks;
    private readonly ConcurrentDictionary<int, (byte PlayerId, ulong Hash)> _pendingRemoteHashes;
    private volatile bool _shutdownRequested;
    private volatile bool _desyncDetected;

    private const int MaxQueueSize = 64;
    private const int MaxStoredHashes = 120;

    /// <summary>
    /// Enable/disable the validator. When disabled, all methods are no-ops.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Local player ID for debug output.
    /// </summary>
    public byte LocalPlayerId { get; set; }

    public BackgroundSyncValidator(ILogger logger)
    {
        _logger = logger;
        _workQueue = new BlockingCollection<WorkItem>(MaxQueueSize);
        _computedHashes = new ConcurrentDictionary<int, ulong>();
        _desyncResults = new ConcurrentQueue<DesyncInfo>();
        _pendingSyncChecks = new ConcurrentQueue<PendingSyncCheck>();
        _pendingRemoteHashes = new ConcurrentDictionary<int, (byte, ulong)>();

        _workerThread = new Thread(WorkerLoop)
        {
            Name = "SyncValidator",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _workerThread.Start();
    }

    #region Work Items

    private readonly struct WorkItem
    {
        public readonly WorkType Type;
        public readonly int Frame;
        public readonly byte[] SnapshotData;
        public readonly int SnapshotLength;  // Actual length (rented arrays may be larger)
        public readonly byte PlayerId;
        public readonly ulong Hash;
        public readonly bool SendSyncCheck;

        private WorkItem(WorkType type, int frame, byte[] snapshotData, int snapshotLength, byte playerId, ulong hash, bool sendSyncCheck)
        {
            Type = type;
            Frame = frame;
            SnapshotData = snapshotData;
            SnapshotLength = snapshotLength;
            PlayerId = playerId;
            Hash = hash;
            SendSyncCheck = sendSyncCheck;
        }

        public static WorkItem HashComputation(int frame, byte[] snapshotData, int length, bool sendSyncCheck)
            => new(WorkType.ComputeHash, frame, snapshotData, length, 0, 0, sendSyncCheck);

        public static WorkItem SyncCheck(byte playerId, int frame, ulong hash)
            => new(WorkType.SyncCheck, frame, Array.Empty<byte>(), 0, playerId, hash, false);
    }

    private enum WorkType
    {
        ComputeHash,
        SyncCheck
    }

    #endregion

    #region Main Thread API (non-blocking)

    /// <summary>
    /// Called when a frame is confirmed (outside rollback window).
    /// Queues snapshot for background hash computation.
    /// </summary>
    public void OnFrameConfirmed(int frame, ReadOnlySpan<byte> snapshotBytes)
    {
        QueueHashComputation(frame, snapshotBytes, sendSyncCheck: false);
    }

    /// <summary>
    /// Queue snapshot for hash computation AND sync check sending.
    /// After hash is computed, it will be queued for main thread to send.
    /// </summary>
    public void QueueSyncCheck(int frame, ReadOnlySpan<byte> snapshotBytes)
    {
        QueueHashComputation(frame, snapshotBytes, sendSyncCheck: true);
    }

    /// <summary>
    /// Compute hash inline on main thread (avoids copy to background thread).
    /// Stores hash for desync detection and returns it for immediate sending.
    /// </summary>
    public unsafe ulong ComputeHashInline(int frame, ReadOnlySpan<byte> snapshotBytes)
    {
        if (_desyncDetected) return 0;

        // Fast FNV-1a using unsafe pointer access (eliminates bounds checking)
        // Must match byte-by-byte algorithm for compatibility with background thread
        ulong hash = 14695981039346656037UL;
        int len = snapshotBytes.Length;
        fixed (byte* ptr = snapshotBytes)
        {
            for (int i = 0; i < len; i++)
            {
                hash ^= ptr[i];
                hash *= 1099511628211UL;
            }
        }

        // Store for desync detection against incoming remote hashes
        _computedHashes[frame] = hash;

        // Check if there's a pending remote hash waiting for this frame
        if (_pendingRemoteHashes.TryRemove(frame, out var pending))
        {
            CompareHashes(pending.PlayerId, frame, hash, pending.Hash);
        }

        // Clean up old hashes
        int oldestAllowed = frame - MaxStoredHashes;
        if (oldestAllowed > 0)
        {
            _computedHashes.TryRemove(oldestAllowed, out _);
            _pendingRemoteHashes.TryRemove(oldestAllowed, out _);
        }

        return hash;
    }

    private void QueueHashComputation(int frame, ReadOnlySpan<byte> snapshotBytes, bool sendSyncCheck)
    {
        if (!Enabled || _shutdownRequested || _desyncDetected) return;

        // Rent buffer from pool (avoids allocation) and copy bytes for background processing
        int length = snapshotBytes.Length;
        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(length);
        snapshotBytes.CopyTo(rentedBuffer);

        if (!_workQueue.TryAdd(WorkItem.HashComputation(frame, rentedBuffer, length, sendSyncCheck), TimeSpan.Zero))
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);  // Return if queue full
            _logger.Debug("Sync validator queue full, skipping frame {Frame}", frame);
        }
    }

    /// <summary>
    /// Called when receiving a hash from a remote peer.
    /// Queues sync check for background comparison.
    /// </summary>
    public void OnRemoteHash(byte playerId, int frame, ulong hash)
    {
        if (!Enabled || _shutdownRequested || _desyncDetected) return;

        if (!_workQueue.TryAdd(WorkItem.SyncCheck(playerId, frame, hash), TimeSpan.Zero))
        {
            _logger.Debug("Sync validator queue full, skipping sync check for frame {Frame}", frame);
        }
    }

    /// <summary>
    /// Poll for desync detection results. Call from main thread.
    /// Returns null if no desync detected.
    /// </summary>
    public DesyncInfo? PollDesyncResult()
    {
        if (_desyncResults.TryDequeue(out var result))
        {
            return result;
        }
        return null;
    }

    /// <summary>
    /// Poll for pending sync checks to send. Call from main thread.
    /// Returns null if no sync checks pending.
    /// </summary>
    public PendingSyncCheck? PollPendingSyncCheck()
    {
        if (_pendingSyncChecks.TryDequeue(out var result))
        {
            return result;
        }
        return null;
    }

    /// <summary>
    /// Clear all state. Call when restarting a match.
    /// </summary>
    public void Clear()
    {
        while (_workQueue.TryTake(out _)) { }
        while (_desyncResults.TryDequeue(out _)) { }
        while (_pendingSyncChecks.TryDequeue(out _)) { }
        _computedHashes.Clear();
        _pendingRemoteHashes.Clear();
        _desyncDetected = false;
    }

    #endregion

    #region Background Worker

    private void WorkerLoop()
    {
        while (!_shutdownRequested)
        {
            try
            {
                if (_workQueue.TryTake(out var work, TimeSpan.FromMilliseconds(50)))
                {
                    ProcessWorkItem(work);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in sync validator worker");
            }
        }
    }

    private void ProcessWorkItem(in WorkItem work)
    {
        switch (work.Type)
        {
            case WorkType.ComputeHash:
                ComputeAndStoreHash(work.Frame, work.SnapshotData, work.SnapshotLength, work.SendSyncCheck);
                break;

            case WorkType.SyncCheck:
                CheckSync(work.PlayerId, work.Frame, work.Hash);
                break;
        }
    }

    private void ComputeAndStoreHash(int frame, byte[] snapshotData, int length, bool sendSyncCheck)
    {
        // FNV-1a hash (use actual length, not array length - rented arrays may be larger)
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < length; i++)
        {
            hash ^= snapshotData[i];
            hash *= 1099511628211UL;
        }

        // Return rented buffer to pool
        ArrayPool<byte>.Shared.Return(snapshotData);

        _computedHashes[frame] = hash;

        // Queue sync check for main thread to send
        if (sendSyncCheck)
        {
            _pendingSyncChecks.Enqueue(new PendingSyncCheck(frame, hash));
        }

        // Check if there's a pending remote hash waiting for this frame
        if (_pendingRemoteHashes.TryRemove(frame, out var pending))
        {
            CompareHashes(pending.PlayerId, frame, hash, pending.Hash);
        }

        // Clean up old hashes
        int oldestAllowed = frame - MaxStoredHashes;
        if (oldestAllowed > 0)
        {
            _computedHashes.TryRemove(oldestAllowed, out _);
            _pendingRemoteHashes.TryRemove(oldestAllowed, out _);
        }
    }

    private void CheckSync(byte playerId, int frame, ulong remoteHash)
    {
        // Stop checking after first desync detected
        if (_desyncDetected) return;

        if (!_computedHashes.TryGetValue(frame, out ulong localHash))
        {
            // Local hash not computed yet - store for later comparison
            _pendingRemoteHashes[frame] = (playerId, remoteHash);
            return;
        }

        CompareHashes(playerId, frame, localHash, remoteHash);
    }

    private void CompareHashes(byte playerId, int frame, ulong localHash, ulong remoteHash)
    {
        if (_desyncDetected) return;

        if (localHash != remoteHash)
        {
            _desyncDetected = true;
            var desyncInfo = new DesyncInfo(frame, localHash, remoteHash, playerId);
            _desyncResults.Enqueue(desyncInfo);

            _logger.Warning("DESYNC at frame {Frame}: local={LocalHash:X16} remote={RemoteHash:X16} from player {PlayerId}",
                frame, localHash, remoteHash, playerId);

            // Export is handled on main thread via DesyncExportHandler
        }
    }

    #endregion

    public void Dispose()
    {
        _shutdownRequested = true;
        _workQueue.CompleteAdding();
        if (!_workerThread.Join(TimeSpan.FromSeconds(2)))
        {
            _logger.Warning("Sync validator thread did not terminate gracefully");
        }
        _workQueue.Dispose();
    }
}
