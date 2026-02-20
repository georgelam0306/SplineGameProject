using System;
using System.Collections.Generic;

namespace DerpTech.Rollback;

/// <summary>
/// Manages input recording and replay for debugging, testing, and spectating.
/// Also handles per-system hash debugging for desync analysis.
/// </summary>
public sealed class ReplayManager<TInput> : IDisposable
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    private InputRecorder<TInput>? _inputRecorder;
    private InputReplayer<TInput>? _inputReplayer;
    private TInput[] _allInputsBuffer = Array.Empty<TInput>();
    private bool _isReplaying;
    private int _playerCount;
    private string? _pendingRecordPath;
    private int _localPlayerSlot = -1;
    private int _lastRecordedFrame = -1;

    // Per-system hash debugging
    private bool _perSystemHashEnabled;
    private ulong[]? _perSystemHashes;
    private string[]? _systemNames;
    private const int MaxStoredPerSystemHashes = 120; // Store ~2 seconds of per-system hashes
    private readonly Dictionary<int, ulong[]> _perSystemHashHistory = new();

    public bool IsReplaying => _isReplaying;
    public bool IsRecording => _inputRecorder != null;
    public bool HasPendingRecord => _pendingRecordPath != null;
    public bool ReplayEnded => _inputReplayer?.EndOfFile ?? false;
    public int PlayerCount => _isReplaying ? _inputReplayer?.PlayerCount ?? 0 : _playerCount;
    public long Seed => _inputReplayer?.Seed ?? 0;

    // Per-system hash properties
    public bool PerSystemHashEnabled => _perSystemHashEnabled;
    public ReadOnlySpan<ulong> PerSystemHashes => _perSystemHashes ?? Array.Empty<ulong>();
    public ReadOnlySpan<string> SystemNames => _systemNames ?? Array.Empty<string>();

    /// <summary>
    /// Set a pending record path. Recording will start when StartPendingRecording is called.
    /// This allows deferring recording start until after the session seed is known.
    /// </summary>
    public void SetPendingRecordPath(string filePath, int localPlayerSlot = -1)
    {
        _pendingRecordPath = filePath;
        if (localPlayerSlot >= 0)
        {
            _localPlayerSlot = localPlayerSlot;
        }
    }

    /// <summary>
    /// Restart recording with a new file. Use this after game restart to continue recording.
    /// Generates a new filename with current timestamp.
    /// </summary>
    public void RestartRecording(int playerCount, long seed)
    {
        StopRecording();

        if (_localPlayerSlot < 0) return; // Recording was never configured

        System.IO.Directory.CreateDirectory("Logs");
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string recordPath = $"Logs/replay_{timestamp}_p{_localPlayerSlot}.bin";

        StartRecording(recordPath, playerCount, seed);
    }

    /// <summary>
    /// Start recording from the pending path with the given seed.
    /// Call this after the session seed is established (e.g., after GameSimulation.Reset()).
    /// </summary>
    public void StartPendingRecording(int playerCount, long seed)
    {
        if (_pendingRecordPath == null) return;
        StartRecording(_pendingRecordPath, playerCount, seed);
        _pendingRecordPath = null;
    }

    /// <summary>
    /// Start recording inputs to a file. Call before game starts.
    /// </summary>
    public void StartRecording(string filePath, int playerCount, long seed = 0)
    {
        _playerCount = playerCount;
        _inputRecorder = new InputRecorder<TInput>(filePath, playerCount, seed);
        _allInputsBuffer = new TInput[playerCount];
        _lastRecordedFrame = -1;
    }

    /// <summary>
    /// Stop recording and close the file.
    /// </summary>
    public void StopRecording()
    {
        _inputRecorder?.Dispose();
        _inputRecorder = null;
    }

    /// <summary>
    /// Start replaying inputs from a file. Call before game starts.
    /// Returns the player count from the replay file.
    /// </summary>
    public int StartReplay(string filePath)
    {
        _inputReplayer = new InputReplayer<TInput>(filePath);
        _isReplaying = true;
        _playerCount = _inputReplayer.PlayerCount;
        _allInputsBuffer = new TInput[_playerCount];

        Console.WriteLine($"[ReplayManager] Replay mode: {_playerCount} players, seed={_inputReplayer.Seed}");
        return _playerCount;
    }

    /// <summary>
    /// Record inputs for confirmed frames. Call after tick completes.
    /// Only records frames once all inputs are confirmed to ensure replay consistency.
    /// </summary>
    public void RecordFrame(int currentFrame, RollbackManager<TInput> rollbackManager)
    {
        if (_inputRecorder == null) return;

        // Record all confirmed frames up to the oldest unconfirmed frame
        // This ensures we only record inputs that all clients have agreed upon
        int oldestUnconfirmed = rollbackManager.GetOldestUnconfirmedFrame();

        // Record frames from _lastRecordedFrame + 1 up to (but not including) oldestUnconfirmed
        while (_lastRecordedFrame + 1 < oldestUnconfirmed)
        {
            int frameToRecord = _lastRecordedFrame + 1;

            // Double-check this frame has all confirmed inputs
            if (!rollbackManager.HasAllInputsForFrame(frameToRecord))
            {
                break;
            }

            rollbackManager.GetAllConfirmedInputs(frameToRecord, _allInputsBuffer);
            _inputRecorder.RecordFrame(frameToRecord, _allInputsBuffer);
            _lastRecordedFrame = frameToRecord;
        }
    }

    /// <summary>
    /// Get inputs for a replay frame. Returns false if replay ended.
    /// </summary>
    public bool TryGetReplayInputs(int frame, Span<TInput> outputBuffer)
    {
        if (_inputReplayer == null || _inputReplayer.EndOfFile)
            return false;

        return _inputReplayer.TryGetInputsForFrame(frame, outputBuffer);
    }

    // --- Per-system hash debugging ---

    /// <summary>
    /// Enable per-system hash computation for debugging desyncs.
    /// This adds overhead but helps identify which system causes divergence.
    /// </summary>
    public void EnablePerSystemHashing(int systemCount, Func<int, string> getSystemName)
    {
        _perSystemHashEnabled = true;
        if (_perSystemHashes == null)
        {
            _perSystemHashes = new ulong[systemCount + 1]; // +1 for BeginFrame
            _systemNames = new string[systemCount + 1];
            _systemNames[0] = "BeginFrame";
            for (int i = 0; i < systemCount; i++)
            {
                _systemNames[i + 1] = getSystemName(i);
            }
        }
    }

    /// <summary>
    /// Disable per-system hash computation.
    /// </summary>
    public void DisablePerSystemHashing()
    {
        _perSystemHashEnabled = false;
    }

    /// <summary>
    /// Capture hash before systems run (BeginFrame state).
    /// </summary>
    public void CaptureBeginFrameHash(ISnapshotProvider snapshotProvider)
    {
        if (!_perSystemHashEnabled || _perSystemHashes == null) return;
        _perSystemHashes[0] = snapshotProvider.ComputeStateHash();
    }

    /// <summary>
    /// Capture hash after a system runs.
    /// </summary>
    public void CaptureSystemHash(int systemIndex, ISnapshotProvider snapshotProvider)
    {
        if (!_perSystemHashEnabled || _perSystemHashes == null) return;
        _perSystemHashes[systemIndex + 1] = snapshotProvider.ComputeStateHash();
    }

    /// <summary>
    /// Store captured hashes for the frame and clean up old entries.
    /// </summary>
    public void StoreFrameHashes(int frame)
    {
        if (!_perSystemHashEnabled || _perSystemHashes == null) return;

        // Copy hashes to history (must copy since array is reused)
        var hashCopy = new ulong[_perSystemHashes.Length];
        _perSystemHashes.CopyTo(hashCopy, 0);
        _perSystemHashHistory[frame] = hashCopy;

        // Clean up old entries
        int oldestAllowed = frame - MaxStoredPerSystemHashes;
        if (oldestAllowed > 0 && _perSystemHashHistory.ContainsKey(oldestAllowed))
        {
            _perSystemHashHistory.Remove(oldestAllowed);
        }
    }

    /// <summary>
    /// Get per-system hashes for a specific frame (if stored).
    /// </summary>
    public bool TryGetPerSystemHashesForFrame(int frame, out ulong[]? hashes)
    {
        return _perSystemHashHistory.TryGetValue(frame, out hashes);
    }

    public void Dispose()
    {
        _inputRecorder?.Dispose();
        _inputReplayer?.Dispose();
    }
}
