using System;
using System.Collections.Generic;

namespace Core.Input;

/// <summary>
/// Parsed binding path like "&lt;Keyboard&gt;/w" or "&lt;Mouse&gt;/leftButton".
/// Pre-parses the path at creation time for zero-allocation runtime queries.
/// </summary>
public readonly struct BindingPath
{
    public readonly DeviceType Device;
    public readonly int ControlCode;        // KeyboardKey, MouseButton, or GamepadButton enum value
    public readonly int AxisCode;           // For gamepad axes or mouse special controls
    public readonly bool IsAxis;
    public readonly bool IsPositive;        // For axis->button (e.g., leftStick/up)

    // Cache parsed paths to avoid string parsing at runtime
    private static readonly Dictionary<string, BindingPath> _pathCache = new();

    private BindingPath(DeviceType device, int controlCode, int axisCode = 0, bool isAxis = false, bool isPositive = true)
    {
        Device = device;
        ControlCode = controlCode;
        AxisCode = axisCode;
        IsAxis = isAxis;
        IsPositive = isPositive;
    }

    /// <summary>
    /// Parse a binding path string. Caches result for reuse.
    /// Call during initialization, not per-frame.
    /// </summary>
    public static BindingPath Parse(string path)
    {
        if (string.IsNullOrEmpty(path))
            return default;

        lock (_pathCache)
        {
            if (_pathCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var parsed = ParseInternal(path);
            _pathCache[path] = parsed;
            return parsed;
        }
    }

    private static BindingPath ParseInternal(string path)
    {
        // Format: <Device>/control or <Device>/control/direction
        // Examples: <Keyboard>/w, <Mouse>/leftButton, <Gamepad>/leftStick/up

        int deviceStart = path.IndexOf('<');
        int deviceEnd = path.IndexOf('>');
        if (deviceStart < 0 || deviceEnd < 0 || deviceEnd <= deviceStart + 1)
        {
            return default;
        }

        int slashPos = path.IndexOf('/', deviceEnd);
        if (slashPos < 0)
        {
            return default;
        }

        string deviceStr = path.Substring(deviceStart + 1, deviceEnd - deviceStart - 1);
        string controlPath = path.Substring(slashPos + 1);

        DeviceType device = deviceStr.ToLowerInvariant() switch
        {
            "keyboard" => DeviceType.Keyboard,
            "mouse" => DeviceType.Mouse,
            "gamepad" => DeviceType.Gamepad,
            _ => DeviceType.None
        };

        return device switch
        {
            DeviceType.Keyboard => ParseKeyboard(controlPath),
            DeviceType.Mouse => ParseMouse(controlPath),
            DeviceType.Gamepad => ParseGamepad(controlPath),
            _ => default
        };
    }

    private static BindingPath ParseKeyboard(string control)
    {
        KeyboardKey key = control.ToLowerInvariant() switch
        {
            "a" => KeyboardKey.A,
            "b" => KeyboardKey.B,
            "c" => KeyboardKey.C,
            "d" => KeyboardKey.D,
            "e" => KeyboardKey.E,
            "f" => KeyboardKey.F,
            "g" => KeyboardKey.G,
            "h" => KeyboardKey.H,
            "i" => KeyboardKey.I,
            "j" => KeyboardKey.J,
            "k" => KeyboardKey.K,
            "l" => KeyboardKey.L,
            "m" => KeyboardKey.M,
            "n" => KeyboardKey.N,
            "o" => KeyboardKey.O,
            "p" => KeyboardKey.P,
            "q" => KeyboardKey.Q,
            "r" => KeyboardKey.R,
            "s" => KeyboardKey.S,
            "t" => KeyboardKey.T,
            "u" => KeyboardKey.U,
            "v" => KeyboardKey.V,
            "w" => KeyboardKey.W,
            "x" => KeyboardKey.X,
            "y" => KeyboardKey.Y,
            "z" => KeyboardKey.Z,
            "space" => KeyboardKey.Space,
            "enter" => KeyboardKey.Enter,
            "escape" or "esc" => KeyboardKey.Escape,
            "tab" => KeyboardKey.Tab,
            "backspace" => KeyboardKey.Backspace,
            "delete" => KeyboardKey.Delete,
            "insert" => KeyboardKey.Insert,
            "home" => KeyboardKey.Home,
            "end" => KeyboardKey.End,
            "pageup" => KeyboardKey.PageUp,
            "pagedown" => KeyboardKey.PageDown,
            "up" => KeyboardKey.Up,
            "down" => KeyboardKey.Down,
            "left" => KeyboardKey.Left,
            "right" => KeyboardKey.Right,
            "leftshift" or "lshift" => KeyboardKey.LeftShift,
            "rightshift" or "rshift" => KeyboardKey.RightShift,
            "leftcontrol" or "lctrl" or "leftctrl" => KeyboardKey.LeftControl,
            "rightcontrol" or "rctrl" or "rightctrl" => KeyboardKey.RightControl,
            "leftalt" or "lalt" => KeyboardKey.LeftAlt,
            "rightalt" or "ralt" => KeyboardKey.RightAlt,
            "0" => KeyboardKey.Zero,
            "1" => KeyboardKey.One,
            "2" => KeyboardKey.Two,
            "3" => KeyboardKey.Three,
            "4" => KeyboardKey.Four,
            "5" => KeyboardKey.Five,
            "6" => KeyboardKey.Six,
            "7" => KeyboardKey.Seven,
            "8" => KeyboardKey.Eight,
            "9" => KeyboardKey.Nine,
            "f1" => KeyboardKey.F1,
            "f2" => KeyboardKey.F2,
            "f3" => KeyboardKey.F3,
            "f4" => KeyboardKey.F4,
            "f5" => KeyboardKey.F5,
            "f6" => KeyboardKey.F6,
            "f7" => KeyboardKey.F7,
            "f8" => KeyboardKey.F8,
            "f9" => KeyboardKey.F9,
            "f10" => KeyboardKey.F10,
            "f11" => KeyboardKey.F11,
            "f12" => KeyboardKey.F12,
            "minus" => KeyboardKey.Minus,
            "equal" or "equals" => KeyboardKey.Equal,
            "leftbracket" => KeyboardKey.LeftBracket,
            "rightbracket" => KeyboardKey.RightBracket,
            "backslash" => KeyboardKey.Backslash,
            "semicolon" => KeyboardKey.Semicolon,
            "apostrophe" or "quote" => KeyboardKey.Apostrophe,
            "comma" => KeyboardKey.Comma,
            "period" or "dot" => KeyboardKey.Period,
            "slash" => KeyboardKey.Slash,
            "grave" or "tilde" => KeyboardKey.Grave,
            _ => KeyboardKey.Unknown
        };

        return new BindingPath(DeviceType.Keyboard, (int)key);
    }

    private static BindingPath ParseMouse(string control)
    {
        string mainControl = control.ToLowerInvariant();

        // Check for position/delta first
        if (mainControl == "position")
        {
            return new BindingPath(DeviceType.Mouse, 0, 0, isAxis: true);
        }
        if (mainControl == "delta")
        {
            return new BindingPath(DeviceType.Mouse, 0, 1, isAxis: true);
        }
        if (mainControl == "scroll" || mainControl == "scrollwheel")
        {
            return new BindingPath(DeviceType.Mouse, 0, 2, isAxis: true);
        }

        MouseButton button = mainControl switch
        {
            "leftbutton" or "left" => MouseButton.Left,
            "rightbutton" or "right" => MouseButton.Right,
            "middlebutton" or "middle" => MouseButton.Middle,
            "button4" or "back" => MouseButton.Back,
            "button5" or "forward" => MouseButton.Forward,
            _ => MouseButton.Left
        };

        return new BindingPath(DeviceType.Mouse, (int)button);
    }

    private static BindingPath ParseGamepad(string control)
    {
        // Split by / for directional bindings
        int slashPos = control.IndexOf('/');
        string mainControl = slashPos >= 0 ? control.Substring(0, slashPos) : control;
        string? direction = slashPos >= 0 ? control.Substring(slashPos + 1) : null;
        mainControl = mainControl.ToLowerInvariant();
        direction = direction?.ToLowerInvariant();

        // Axes
        if (mainControl is "leftstick" or "rightstick" or "lefttrigger" or "righttrigger")
        {
            int axisCode = mainControl switch
            {
                "leftstick" => 0,   // X=0, Y=1
                "rightstick" => 2,  // X=2, Y=3
                "lefttrigger" => 4,
                "righttrigger" => 5,
                _ => 0
            };

            bool isPositive = true;
            if (direction != null)
            {
                // Handle directional bindings like leftStick/up.
                //
                // SDL convention: stick X is (-left .. +right), stick Y is (-up .. +down).
                // So for Y we invert "up/down" compared to X.
                if (direction is "left" or "right")
                {
                    isPositive = direction == "right";
                }
                else if (direction is "up" or "down")
                {
                    axisCode += 1; // Y axis
                    isPositive = direction == "down";
                }
            }

            return new BindingPath(DeviceType.Gamepad, 0, axisCode, isAxis: true, isPositive);
        }

        // Buttons
        GamepadButton button = mainControl switch
        {
            "a" or "south" => GamepadButton.RightFaceDown,
            "b" or "east" => GamepadButton.RightFaceRight,
            "x" or "west" => GamepadButton.RightFaceLeft,
            "y" or "north" => GamepadButton.RightFaceUp,
            "start" => GamepadButton.MiddleRight,
            "select" or "back" => GamepadButton.MiddleLeft,
            "guide" or "home" => GamepadButton.Middle,
            "leftbumper" or "lb" or "leftshoulder" => GamepadButton.LeftTrigger1,
            "rightbumper" or "rb" or "rightshoulder" => GamepadButton.RightTrigger1,
            "leftstickpress" or "l3" or "leftthumb" => GamepadButton.LeftThumb,
            "rightstickpress" or "r3" or "rightthumb" => GamepadButton.RightThumb,
            "dpadup" => GamepadButton.LeftFaceUp,
            "dpaddown" => GamepadButton.LeftFaceDown,
            "dpadleft" => GamepadButton.LeftFaceLeft,
            "dpadright" => GamepadButton.LeftFaceRight,
            _ => GamepadButton.Unknown
        };

        return new BindingPath(DeviceType.Gamepad, (int)button);
    }
}
