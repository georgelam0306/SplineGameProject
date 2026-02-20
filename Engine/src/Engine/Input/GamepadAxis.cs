namespace DerpLib.Input;

/// <summary>
/// Gamepad axis identifiers, compatible with Raylib's GamepadAxis enum.
/// </summary>
public enum GamepadAxis
{
    LeftX = 0,         // Left stick horizontal
    LeftY = 1,         // Left stick vertical
    RightX = 2,        // Right stick horizontal
    RightY = 3,        // Right stick vertical
    LeftTrigger = 4,   // Left trigger (0 to 1)
    RightTrigger = 5,  // Right trigger (0 to 1)
}
