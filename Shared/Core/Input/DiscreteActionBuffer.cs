using System;
using Core;

namespace Core.Input;

/// <summary>
/// Buffer for discrete action events with "last wins" semantics.
/// Used for actions that need to be consumed by simulation ticks.
/// When multiple events occur between ticks, only the last one is kept.
/// </summary>
public sealed class DiscreteActionBuffer
{
    /// <summary>Maximum number of discrete actions that can be tracked.</summary>
    public const int MaxActions = 32;

    private readonly ActionSnapshot[] _buffer;
    private readonly bool[] _hasValue;
    private readonly uint[] _registeredActionIds;
    private int _registeredCount;

    public DiscreteActionBuffer()
    {
        _buffer = new ActionSnapshot[MaxActions];
        _hasValue = new bool[MaxActions];
        _registeredActionIds = new uint[MaxActions];
        _registeredCount = 0;
    }

    /// <summary>Number of registered discrete actions.</summary>
    public int RegisteredCount => _registeredCount;

    /// <summary>
    /// Register an action for discrete tracking. Call during setup, not per-frame.
    /// </summary>
    /// <returns>True if registered, false if at capacity.</returns>
    public bool RegisterAction(StringHandle actionName)
    {
        if (_registeredCount >= MaxActions)
            return false;

        uint actionId = actionName.Id;

        // Check if already registered
        for (int i = 0; i < _registeredCount; i++)
        {
            if (_registeredActionIds[i] == actionId)
                return true;  // Already registered
        }

        _registeredActionIds[_registeredCount] = actionId;
        _registeredCount++;
        return true;
    }

    /// <summary>
    /// Buffer an action event. Last write wins for each action.
    /// Only buffers Started and Canceled phases (discrete events).
    /// </summary>
    public void Buffer(in ActionSnapshot snapshot)
    {
        // Only buffer discrete events
        if (snapshot.Phase != ActionPhase.Started && snapshot.Phase != ActionPhase.Canceled)
            return;

        // Find index for this action
        for (int i = 0; i < _registeredCount; i++)
        {
            if (_registeredActionIds[i] == snapshot.ActionId)
            {
                _buffer[i] = snapshot;
                _hasValue[i] = true;
                return;
            }
        }
    }

    /// <summary>
    /// Check if action has a buffered event (Started or Canceled).
    /// </summary>
    public bool HasBufferedEvent(StringHandle actionName)
    {
        uint actionId = actionName.Id;
        for (int i = 0; i < _registeredCount; i++)
        {
            if (_registeredActionIds[i] == actionId)
                return _hasValue[i];
        }
        return false;
    }

    /// <summary>
    /// Peek at a buffered action event without consuming it.
    /// </summary>
    public bool TryPeek(StringHandle actionName, out ActionSnapshot snapshot)
    {
        uint actionId = actionName.Id;
        for (int i = 0; i < _registeredCount; i++)
        {
            if (_registeredActionIds[i] == actionId && _hasValue[i])
            {
                snapshot = _buffer[i];
                return true;
            }
        }
        snapshot = default;
        return false;
    }

    /// <summary>
    /// Try to consume a buffered action event. Returns true if consumed.
    /// </summary>
    public bool TryConsume(StringHandle actionName, out ActionSnapshot snapshot)
    {
        uint actionId = actionName.Id;
        for (int i = 0; i < _registeredCount; i++)
        {
            if (_registeredActionIds[i] == actionId && _hasValue[i])
            {
                snapshot = _buffer[i];
                _hasValue[i] = false;
                return true;
            }
        }
        snapshot = default;
        return false;
    }

    /// <summary>
    /// Check if action was started and consume the event.
    /// </summary>
    public bool ConsumeIfStarted(StringHandle actionName, out InputValue value)
    {
        if (TryConsume(actionName, out var snapshot) && snapshot.Phase == ActionPhase.Started)
        {
            value = snapshot.Value;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Check if action was canceled and consume the event.
    /// </summary>
    public bool ConsumeIfCanceled(StringHandle actionName, out InputValue value)
    {
        if (TryConsume(actionName, out var snapshot) && snapshot.Phase == ActionPhase.Canceled)
        {
            value = snapshot.Value;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Clear all buffered events without unregistering actions.
    /// Call at the end of each simulation tick.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_hasValue, 0, _registeredCount);
    }

    /// <summary>
    /// Reset buffer completely, including unregistering all actions.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_hasValue);
        Array.Clear(_registeredActionIds);
        _registeredCount = 0;
    }
}
