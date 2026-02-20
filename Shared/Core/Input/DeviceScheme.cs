namespace Core.Input;

/// <summary>
/// Defines a device scheme (e.g., "KeyboardMouse", "Gamepad").
/// Used to filter which bindings are active.
/// </summary>
public readonly struct DeviceScheme
{
    public readonly StringHandle Name;
    public readonly DeviceType[] AllowedDevices;

    public DeviceScheme(StringHandle name, params DeviceType[] allowedDevices)
    {
        Name = name;
        AllowedDevices = allowedDevices;
    }

    public bool AllowsDevice(DeviceType device)
    {
        for (int i = 0; i < AllowedDevices.Length; i++)
        {
            if (AllowedDevices[i] == device)
            {
                return true;
            }
        }
        return false;
    }

    // Predefined schemes
    public static readonly DeviceScheme KeyboardMouse = new("KeyboardMouse", DeviceType.Keyboard, DeviceType.Mouse);
    public static readonly DeviceScheme Gamepad = new("Gamepad", DeviceType.Gamepad);
    public static readonly DeviceScheme All = new("All", DeviceType.Keyboard, DeviceType.Mouse, DeviceType.Gamepad);
}
