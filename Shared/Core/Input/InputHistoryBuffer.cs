using System;
using Core;

namespace Core.Input;

/// <summary>
/// Ring buffer storing N frames of action snapshots for input history.
/// Zero-allocation hot path - pre-allocated arrays, no LINQ.
/// Used for querying action state across frame boundaries.
/// </summary>
public sealed class InputHistoryBuffer
{
    /// <summary>Default number of frames to store.</summary>
    public const int DefaultCapacity = 16;

    /// <summary>Maximum actions that can be captured per frame.</summary>
    public const int MaxActionsPerFrame = 64;

    // Per-frame snapshot storage: flat array [frameIndex * MaxActionsPerFrame + actionIndex]
    private readonly ActionSnapshot[] _buffer;
    private readonly int[] _countsPerFrame;  // How many actions captured per frame slot
    private readonly int _frameCapacity;

    private int _head;           // Next write position (frame index)
    private int _count;          // Number of valid frames in buffer
    private ulong _oldestFrame;  // Oldest frame number in buffer
    private ulong _newestFrame;  // Most recent frame number

    public InputHistoryBuffer(int frameCapacity = DefaultCapacity)
    {
        _frameCapacity = frameCapacity;
        _buffer = new ActionSnapshot[frameCapacity * MaxActionsPerFrame];
        _countsPerFrame = new int[frameCapacity];
        _head = 0;
        _count = 0;
        _oldestFrame = 0;
        _newestFrame = 0;
    }

    /// <summary>Number of frames this buffer can store.</summary>
    public int FrameCapacity => _frameCapacity;

    /// <summary>Number of valid frames currently in buffer.</summary>
    public int FrameCount => _count;

    /// <summary>Oldest frame number in buffer (0 if empty).</summary>
    public ulong OldestFrame => _count > 0 ? _oldestFrame : 0;

    /// <summary>Most recent frame number in buffer.</summary>
    public ulong NewestFrame => _newestFrame;

    /// <summary>
    /// Begin recording a new frame. Call before adding snapshots.
    /// </summary>
    public void BeginFrame(ulong frameNumber)
    {
        _newestFrame = frameNumber;
        _countsPerFrame[_head] = 0;  // Reset count for this slot
    }

    /// <summary>
    /// Add an action snapshot to the current frame being recorded.
    /// </summary>
    public void AddSnapshot(in ActionSnapshot snapshot)
    {
        int baseIndex = _head * MaxActionsPerFrame;
        int count = _countsPerFrame[_head];

        if (count >= MaxActionsPerFrame)
            return;  // Silently drop if too many actions

        _buffer[baseIndex + count] = snapshot;
        _countsPerFrame[_head] = count + 1;
    }

    /// <summary>
    /// End the current frame recording and advance the ring buffer.
    /// </summary>
    public void EndFrame()
    {
        _head = (_head + 1) % _frameCapacity;

        if (_count < _frameCapacity)
        {
            _count++;
            _oldestFrame = _newestFrame - (ulong)(_count - 1);
        }
        else
        {
            // Overwriting oldest frame
            _oldestFrame = _newestFrame - (ulong)(_frameCapacity - 1);
        }
    }

    /// <summary>
    /// Check if an action was in Started phase within the last N frames.
    /// </summary>
    public bool WasStartedWithinFrames(StringHandle actionName, int withinFrames)
    {
        uint actionId = actionName.Id;
        int framesToCheck = Math.Min(withinFrames, _count);

        for (int f = 0; f < framesToCheck; f++)
        {
            int frameIdx = (_head - 1 - f + _frameCapacity) % _frameCapacity;
            int baseIndex = frameIdx * MaxActionsPerFrame;
            int count = _countsPerFrame[frameIdx];

            for (int a = 0; a < count; a++)
            {
                ref var snapshot = ref _buffer[baseIndex + a];
                if (snapshot.ActionId == actionId && snapshot.Phase == ActionPhase.Started)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Check if an action was in Canceled phase within the last N frames.
    /// </summary>
    public bool WasCanceledWithinFrames(StringHandle actionName, int withinFrames)
    {
        uint actionId = actionName.Id;
        int framesToCheck = Math.Min(withinFrames, _count);

        for (int f = 0; f < framesToCheck; f++)
        {
            int frameIdx = (_head - 1 - f + _frameCapacity) % _frameCapacity;
            int baseIndex = frameIdx * MaxActionsPerFrame;
            int count = _countsPerFrame[frameIdx];

            for (int a = 0; a < count; a++)
            {
                ref var snapshot = ref _buffer[baseIndex + a];
                if (snapshot.ActionId == actionId && snapshot.Phase == ActionPhase.Canceled)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get the most recent snapshot for an action, if present.
    /// </summary>
    public bool TryGetLatest(StringHandle actionName, out ActionSnapshot snapshot)
    {
        uint actionId = actionName.Id;

        for (int f = 0; f < _count; f++)
        {
            int frameIdx = (_head - 1 - f + _frameCapacity) % _frameCapacity;
            int baseIndex = frameIdx * MaxActionsPerFrame;
            int count = _countsPerFrame[frameIdx];

            for (int a = 0; a < count; a++)
            {
                ref var s = ref _buffer[baseIndex + a];
                if (s.ActionId == actionId)
                {
                    snapshot = s;
                    return true;
                }
            }
        }

        snapshot = default;
        return false;
    }

    /// <summary>
    /// Get action snapshot at a specific frame number.
    /// </summary>
    public bool TryGetAtFrame(StringHandle actionName, ulong frameNumber, out ActionSnapshot snapshot)
    {
        if (_count == 0 || frameNumber < _oldestFrame || frameNumber > _newestFrame)
        {
            snapshot = default;
            return false;
        }

        uint actionId = actionName.Id;
        int frameOffset = (int)(_newestFrame - frameNumber);
        int frameIdx = (_head - 1 - frameOffset + _frameCapacity) % _frameCapacity;
        int baseIndex = frameIdx * MaxActionsPerFrame;
        int count = _countsPerFrame[frameIdx];

        for (int a = 0; a < count; a++)
        {
            ref var s = ref _buffer[baseIndex + a];
            if (s.ActionId == actionId)
            {
                snapshot = s;
                return true;
            }
        }

        snapshot = default;
        return false;
    }

    /// <summary>
    /// Clear all buffered history.
    /// </summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
        _oldestFrame = 0;
        _newestFrame = 0;
        Array.Clear(_countsPerFrame);
    }
}
