using System.Numerics;

namespace Core.Input;

/// <summary>
/// Context passed to action callbacks. Contains all information about the action event.
/// </summary>
public readonly struct ActionContext
{
    public readonly StringHandle ActionName;
    public readonly StringHandle MapName;
    public readonly ActionPhase Phase;
    public readonly ActionType Type;
    public readonly InputValue Value;
    public readonly InputValue PreviousValue;
    public readonly double Time;
    public readonly double Duration; // Time since Started phase

    public ActionContext(
        StringHandle actionName,
        StringHandle mapName,
        ActionPhase phase,
        ActionType type,
        InputValue value,
        InputValue previousValue,
        double time,
        double duration)
    {
        ActionName = actionName;
        MapName = mapName;
        Phase = phase;
        Type = type;
        Value = value;
        PreviousValue = previousValue;
        Time = time;
        Duration = duration;
    }

    // Convenience accessors
    public bool WasPerformedThisFrame => Phase == ActionPhase.Started;
    public bool WasCanceledThisFrame => Phase == ActionPhase.Canceled;
    public bool IsPerformed => Phase == ActionPhase.Performed;
    public Vector2 ReadVector2() => Value.Vector2;
    public float ReadValue() => Value.Value;
    public bool ReadButton() => Value.IsPressed;
}

/// <summary>
/// Delegate for action callbacks.
/// </summary>
public delegate void ActionCallbackHandler(in ActionContext context);
