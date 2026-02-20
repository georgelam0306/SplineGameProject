namespace Core.Input;

/// <summary>
/// Canonical gamepad button identifiers used by the action/binding system.
/// Values match common conventions (and DerpLib's button enum) for easy backend mapping.
/// </summary>
public enum GamepadButton : byte
{
    Unknown = 0,
    LeftFaceUp = 1,
    LeftFaceRight = 2,
    LeftFaceDown = 3,
    LeftFaceLeft = 4,
    RightFaceUp = 5,
    RightFaceRight = 6,
    RightFaceDown = 7,
    RightFaceLeft = 8,
    LeftTrigger1 = 9,
    LeftTrigger2 = 10,
    RightTrigger1 = 11,
    RightTrigger2 = 12,
    MiddleLeft = 13,
    Middle = 14,
    MiddleRight = 15,
    LeftThumb = 16,
    RightThumb = 17,
}

