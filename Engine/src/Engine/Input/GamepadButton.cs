namespace DerpLib.Input;

/// <summary>
/// Gamepad button identifiers, compatible with Raylib's GamepadButton enum.
/// </summary>
public enum GamepadButton
{
    Unknown = 0,
    LeftFaceUp = 1,       // D-pad up
    LeftFaceRight = 2,    // D-pad right
    LeftFaceDown = 3,     // D-pad down
    LeftFaceLeft = 4,     // D-pad left
    RightFaceUp = 5,      // Y (Xbox) / Triangle (PS)
    RightFaceRight = 6,   // B (Xbox) / Circle (PS)
    RightFaceDown = 7,    // A (Xbox) / Cross (PS)
    RightFaceLeft = 8,    // X (Xbox) / Square (PS)
    LeftTrigger1 = 9,     // Left bumper (LB)
    LeftTrigger2 = 10,    // Left trigger (LT)
    RightTrigger1 = 11,   // Right bumper (RB)
    RightTrigger2 = 12,   // Right trigger (RT)
    MiddleLeft = 13,      // Select/Back
    Middle = 14,          // Guide/Home
    MiddleRight = 15,     // Start
    LeftThumb = 16,       // Left stick click (L3)
    RightThumb = 17,      // Right stick click (R3)
}
