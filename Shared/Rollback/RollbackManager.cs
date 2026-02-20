using System;
using System.Collections.Generic;
using Serilog;

namespace DerpTech.Rollback;

public sealed class RollbackManager<TInput>
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    private readonly WorldSnapshot _worldSnapshot;
    private readonly SnapshotBuffer _snapshotBuffer;
    private readonly MultiPlayerInputBuffer<TInput> _inputBuffer;
    private readonly PendingInputBuffer<TInput> _pendingInputs;
    private readonly Dictionary<int, ulong> _recentHashes;

    // Pending sync checks (processed after simulation update)
    private readonly List<PendingSyncCheck> _pendingSyncChecks;
    private const int MaxPendingSyncChecks = 16;

    private readonly struct PendingSyncCheck
    {
        public readonly byte PlayerId;
        public readonly int Frame;
        public readonly ulong Hash;

        public PendingSyncCheck(byte playerId, int frame, ulong hash)
        {
            PlayerId = playerId;
            Frame = frame;
            Hash = hash;
        }
    }

    // Desync detection state
    private bool _desyncDetected;
    private int _desyncFrame;
    private ulong _localDesyncHash;
    private ulong _remoteDesyncHash;
    private byte _desyncRemotePlayerId;

    public const int MaxRollbackFrames = 7;
    private const int MaxStoredHashes = 60;
    private const int SnapshotHeaderSize = 64; // Margin for header/alignment

    private int _playerCount;
    private int _localPlayerId;

    private readonly ILogger _logger;

    public MultiPlayerInputBuffer<TInput> InputBuffer => _inputBuffer;
    public int PlayerCount => _playerCount;
    public int LocalPlayerId => _localPlayerId;

    // Desync detection accessors
    public bool DesyncDetected => _desyncDetected;
    public int DesyncFrame => _desyncFrame;
    public ulong LocalDesyncHash => _localDesyncHash;
    public ulong RemoteDesyncHash => _remoteDesyncHash;
    public byte DesyncRemotePlayerId => _desyncRemotePlayerId;

    public RollbackManager(ISnapshotProvider snapshotProvider, MultiPlayerInputBuffer<TInput> inputBuffer, ILogger logger)
    {
        _worldSnapshot = new WorldSnapshot(snapshotProvider);
        int snapshotSize = snapshotProvider.TotalSnapshotSize + SnapshotHeaderSize;
        _snapshotBuffer = new SnapshotBuffer(MaxRollbackFrames + 1, snapshotSize);
        _inputBuffer = inputBuffer;
        _pendingInputs = new PendingInputBuffer<TInput>();
        _recentHashes = new Dictionary<int, ulong>(MaxStoredHashes);
        _pendingSyncChecks = new List<PendingSyncCheck>(MaxPendingSyncChecks);
        _logger = logger;

        _playerCount = 1;
        _localPlayerId = 0;
    }

    public void SetPlayerCount(int playerCount)
    {
        _playerCount = Math.Clamp(playerCount, 1, _inputBuffer.MaxPlayers);
    }

    public void SetLocalPlayerId(int playerId)
    {
        _localPlayerId = Math.Clamp(playerId, 0, _inputBuffer.MaxPlayers - 1);
    }

    public void SaveSnapshot(int frame)
    {
        Span<byte> buffer = _snapshotBuffer.GetWriteBuffer(frame);
        int bytesWritten = _worldSnapshot.SerializeWorld(frame, buffer);
        _snapshotBuffer.SetUsedBytes(frame, bytesWritten);
    }

    public void RestoreSnapshot(int targetFrame, int currentFrame)
    {
        if (targetFrame < currentFrame - MaxRollbackFrames)
        {
            throw new InvalidOperationException($"Cannot rollback {currentFrame - targetFrame} frames, max is {MaxRollbackFrames}");
        }

        if (!_snapshotBuffer.HasFrame(targetFrame, currentFrame))
        {
            throw new InvalidOperationException($"No snapshot available for frame {targetFrame}");
        }

        ReadOnlySpan<byte> snapshot = _snapshotBuffer.GetReadBuffer(targetFrame);
        _worldSnapshot.DeserializeWorld(snapshot);
    }

    public void StoreLocalInput(int frame, in TInput input)
    {
        _inputBuffer.StoreInput(frame, _localPlayerId, input);
    }

    public void StoreRemoteInput(int frame, int playerId, in TInput input)
    {
        _inputBuffer.StoreInput(frame, playerId, input);
    }

    public void StorePredictedInput(int frame, int playerId, in TInput input)
    {
        _inputBuffer.StorePredictedInput(frame, playerId, input);
    }

    public void EnqueueInput(int frame, int playerId, in TInput input)
    {
        _pendingInputs.Enqueue(frame, playerId, in input);
    }

    /// <summary>
    /// Process all pending inputs queued from the network.
    /// Returns the earliest frame that needs rollback, or -1 if no rollback needed.
    /// </summary>
    public int ProcessPendingInputs(int currentFrame)
    {
        int earliestConflict = -1;

        for (int i = 0; i < _pendingInputs.Count; i++)
        {
            ref readonly var pending = ref _pendingInputs.Get(i);

            // Skip too-old inputs
            if (pending.Frame < currentFrame - MaxRollbackFrames)
            {
                continue;
            }

            // Future inputs: store without conflict check
            if (pending.Frame > currentFrame)
            {
                _inputBuffer.StoreInput(pending.Frame, pending.PlayerId, pending.Input);
                continue;
            }

            // Already have confirmed input for this frame - skip
            if (_inputBuffer.HasInput(pending.Frame, pending.PlayerId))
            {
                continue;
            }

            // Check if this input differs from our prediction
            ref readonly var predicted = ref _inputBuffer.GetInput(pending.Frame, pending.PlayerId);
            bool differs = !pending.Input.Equals(predicted);

            // Store the confirmed input
            _inputBuffer.StoreInput(pending.Frame, pending.PlayerId, pending.Input);

            // Track earliest conflict
            if (differs && (earliestConflict < 0 || pending.Frame < earliestConflict))
            {
                earliestConflict = pending.Frame;
            }
        }

        _pendingInputs.Clear();
        return earliestConflict;
    }

    public void PredictMissingInputs(int frame)
    {
        for (int playerId = 0; playerId < _playerCount; playerId++)
        {
            // With input delay, local player stores inputs for future frames (currentFrame + delay),
            // so they may not have input for the current frame during initial frames.
            // Predict for ANY player missing input, including local player.
            if (!_inputBuffer.HasInput(frame, playerId))
            {
                TInput predicted = _inputBuffer.PredictInput(playerId);
                _inputBuffer.StorePredictedInput(frame, playerId, in predicted);
            }
        }
    }

    public ref readonly TInput GetInput(int frame, int playerId)
    {
        return ref _inputBuffer.GetInput(frame, playerId);
    }

    public void GetAllInputs(int frame, Span<TInput> outputInputs)
    {
        _inputBuffer.GetAllInputs(frame, outputInputs);
    }

    /// <summary>
    /// Gets only confirmed inputs for a frame. Unconfirmed slots return Empty.
    /// </summary>
    public void GetAllConfirmedInputs(int frame, Span<TInput> outputInputs)
    {
        _inputBuffer.GetAllConfirmedInputs(frame, outputInputs);
    }

    public bool HasAllInputsForFrame(int frame)
    {
        return _inputBuffer.HasAllInputs(frame, _playerCount);
    }

    public int GetOldestUnconfirmedFrame()
    {
        return _inputBuffer.GetOldestUnconfirmedFrame(_playerCount);
    }

    public void StoreHashForFrame(int frame)
    {
        // Compute FNV-1a hash on snapshot bytes for network sync
        // This ensures all peers use the same hash algorithm on the same data
        ReadOnlySpan<byte> snapshot = _snapshotBuffer.GetReadBuffer(frame);
        ulong hash = ComputeFnv1aHash(snapshot);
        _recentHashes[frame] = hash;

        int oldestAllowed = frame - MaxStoredHashes;
        if (oldestAllowed > 0 && _recentHashes.ContainsKey(oldestAllowed))
        {
            _recentHashes.Remove(oldestAllowed);
        }
    }

    private static ulong ComputeFnv1aHash(ReadOnlySpan<byte> data)
    {
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    public bool TryGetHashForFrame(int frame, out ulong hash)
    {
        return _recentHashes.TryGetValue(frame, out hash);
    }

    /// <summary>
    /// Get hash history for recent frames (for desync debugging).
    /// </summary>
    public void GetHashHistory(int endFrame, int count, Span<(int frame, ulong hash)> output, out int written)
    {
        written = 0;
        int startFrame = endFrame - count + 1;
        if (startFrame < 0) startFrame = 0;

        for (int frame = startFrame; frame <= endFrame && written < output.Length; frame++)
        {
            if (_recentHashes.TryGetValue(frame, out ulong hash))
            {
                output[written++] = (frame, hash);
            }
        }
    }

    /// <summary>
    /// Get snapshot bytes for a confirmed frame.
    /// Used by background sync validator for hash computation.
    /// </summary>
    public bool TryGetSnapshotBytes(int frame, int currentFrame, out ReadOnlySpan<byte> snapshot)
    {
        if (_snapshotBuffer.HasFrame(frame, currentFrame))
        {
            snapshot = _snapshotBuffer.GetReadBuffer(frame);
            return true;
        }
        snapshot = default;
        return false;
    }

    public ulong GetStateHash()
    {
        return _worldSnapshot.GetStateHash();
    }

    /// <summary>
    /// Queue a sync check to be processed after simulation update.
    /// This ensures rollbacks have been applied before comparing hashes.
    /// </summary>
    public void EnqueueSyncCheck(byte playerId, int frame, ulong remoteHash)
    {
        if (_desyncDetected) return;

        // Limit queue size to prevent memory issues
        if (_pendingSyncChecks.Count >= MaxPendingSyncChecks)
        {
            _pendingSyncChecks.RemoveAt(0);
        }

        _pendingSyncChecks.Add(new PendingSyncCheck(playerId, frame, remoteHash));
    }

    /// <summary>
    /// Process all pending sync checks. Call this AFTER simulation update
    /// to ensure any rollbacks have been applied and hashes updated.
    /// Returns true if a desync was detected.
    /// </summary>
    public bool ProcessPendingSyncChecks()
    {
        if (_desyncDetected)
        {
            _pendingSyncChecks.Clear();
            return false;
        }

        // Process in reverse order so we can safely remove items
        for (int i = _pendingSyncChecks.Count - 1; i >= 0; i--)
        {
            var check = _pendingSyncChecks[i];

            // Only compare if we have all confirmed inputs for this frame
            if (!_inputBuffer.HasAllInputs(check.Frame, _playerCount))
            {
                // Keep this check for later - don't remove it
                continue;
            }

            // Remove the check now that we're processing it
            _pendingSyncChecks.RemoveAt(i);

            if (_recentHashes.TryGetValue(check.Frame, out ulong localHash))
            {
                if (localHash != check.Hash)
                {
                    _desyncDetected = true;
                    _desyncFrame = check.Frame;
                    _localDesyncHash = localHash;
                    _remoteDesyncHash = check.Hash;
                    _desyncRemotePlayerId = check.PlayerId;
                    _logger.Warning("DESYNC at frame {Frame}: local={LocalHash:X16} remote={RemoteHash:X16} from player {PlayerId}",
                        check.Frame, localHash, check.Hash, check.PlayerId);
                    _pendingSyncChecks.Clear();
                    return true;
                }
            }
            // else: we don't have the hash anymore (frame too old), check was removed above
        }

        // Clean up any checks for frames too old to have hashes
        // This prevents unbounded queue growth if inputs are never confirmed
        for (int i = _pendingSyncChecks.Count - 1; i >= 0; i--)
        {
            if (!_recentHashes.ContainsKey(_pendingSyncChecks[i].Frame))
            {
                _pendingSyncChecks.RemoveAt(i);
            }
        }

        return false;
    }

    public void ClearDesyncState()
    {
        _desyncDetected = false;
        _desyncFrame = 0;
        _localDesyncHash = 0;
        _remoteDesyncHash = 0;
        _desyncRemotePlayerId = 0;
    }

    /// <summary>
    /// Set desync state when notified by another peer.
    /// </summary>
    public void SetDesyncFromPeer(byte playerId, int frame, ulong peerLocalHash, ulong peerRemoteHash)
    {
        if (_desyncDetected) return;

        _desyncDetected = true;
        _desyncFrame = frame;
        // From our perspective, our hash should match what the peer saw as "remote"
        // and the peer's local hash is what they computed
        _localDesyncHash = peerRemoteHash;  // What peer thought we should have
        _remoteDesyncHash = peerLocalHash;  // What peer had
        _desyncRemotePlayerId = playerId;
        _logger.Warning("DESYNC (notified by player {PlayerId}) at frame {Frame}: peer_local={PeerLocalHash:X16} peer_remote={PeerRemoteHash:X16}",
            playerId, frame, peerLocalHash, peerRemoteHash);
    }

    /// <summary>
    /// Set desync state when detected locally (by background validator).
    /// </summary>
    public void SetDesyncFromLocal(int frame, ulong localHash, ulong remoteHash, byte remotePlayerId)
    {
        if (_desyncDetected) return;

        _desyncDetected = true;
        _desyncFrame = frame;
        _localDesyncHash = localHash;
        _remoteDesyncHash = remoteHash;
        _desyncRemotePlayerId = remotePlayerId;
        _logger.Warning("DESYNC (detected locally) at frame {Frame}: local={LocalHash:X16} remote={RemoteHash:X16} from player {PlayerId}",
            frame, localHash, remoteHash, remotePlayerId);
    }

    public void Clear()
    {
        _inputBuffer.Clear();
        _snapshotBuffer.Clear();
        _recentHashes.Clear();
        _pendingInputs.Clear();
        ClearDesyncState();
    }
}
