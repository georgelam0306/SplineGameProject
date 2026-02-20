using System;

namespace Core.Input;

/// <summary>
/// Physical device type for input bindings.
/// </summary>
public enum DeviceType : byte
{
    None = 0,
    Keyboard = 1,
    Mouse = 2,
    Gamepad = 3
}

/// <summary>
/// Type of value an action produces.
/// </summary>
public enum ActionType : byte
{
    /// <summary>Digital on/off (space, mouse click).</summary>
    Button = 0,
    /// <summary>1D analog (trigger, scroll wheel).</summary>
    Value = 1,
    /// <summary>2D analog (stick, WASD composite, mouse position).</summary>
    Vector2 = 2,
    /// <summary>3D analog (rarely used).</summary>
    Vector3 = 3
}

/// <summary>
/// Current phase of an action's lifecycle.
/// </summary>
public enum ActionPhase : byte
{
    /// <summary>Action is disabled (context inactive).</summary>
    Disabled = 0,
    /// <summary>Waiting for input.</summary>
    Waiting = 1,
    /// <summary>Just pressed this frame.</summary>
    Started = 2,
    /// <summary>Held/active.</summary>
    Performed = 3,
    /// <summary>Just released this frame.</summary>
    Canceled = 4
}

/// <summary>
/// Modifier keys that can be combined with bindings.
/// </summary>
[Flags]
public enum ModifierKeys : byte
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4
}
