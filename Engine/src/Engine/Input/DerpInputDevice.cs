using System;
using System.Numerics;
using Core.Input;
using DerpGamepadAxis = DerpLib.Input.GamepadAxis;
using DerpGamepadButton = DerpLib.Input.GamepadButton;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace DerpLib;

/// <summary>
/// Derp-backed input device implementation for the action system.
/// Reads keyboard/mouse via Derp.Window (Silk.NET) and gamepad via Derp SDL gamepad wrapper.
/// </summary>
internal sealed class DerpInputDevice : IInputDevice
{
    private int _gamepadIndex;

    // Modifier key cache
    private bool _shiftHeld;
    private bool _ctrlHeld;
    private bool _altHeld;

    // Mouse state cache
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private float _mouseScroll;

    public int GamepadIndex
    {
        get => _gamepadIndex;
        set => _gamepadIndex = value;
    }

    public bool IsGamepadConnected => Derp.IsGamepadAvailable(_gamepadIndex);

    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;
    public float MouseScroll => _mouseScroll;

    public void Update()
    {
        _shiftHeld = Derp.IsKeyDown(SilkKey.ShiftLeft) || Derp.IsKeyDown(SilkKey.ShiftRight);
        _ctrlHeld = Derp.IsKeyDown(SilkKey.ControlLeft) || Derp.IsKeyDown(SilkKey.ControlRight);
        _altHeld = Derp.IsKeyDown(SilkKey.AltLeft) || Derp.IsKeyDown(SilkKey.AltRight);

        _mousePosition = Derp.GetMousePosition();
        _mouseDelta = Derp.GetMouseDelta();
        _mouseScroll = Derp.GetScrollDelta();

        if (!Derp.IsGamepadAvailable(_gamepadIndex))
        {
            for (int i = 0; i < 4; i++)
            {
                if (Derp.IsGamepadAvailable(i))
                {
                    _gamepadIndex = i;
                    break;
                }
            }
        }
    }

    public bool CheckModifiers(ModifierKeys required)
    {
        if (required == ModifierKeys.None)
        {
            return true;
        }

        if ((required & ModifierKeys.Shift) != 0 && !_shiftHeld)
        {
            return false;
        }
        if ((required & ModifierKeys.Control) != 0 && !_ctrlHeld)
        {
            return false;
        }
        if ((required & ModifierKeys.Alt) != 0 && !_altHeld)
        {
            return false;
        }

        return true;
    }

    public InputValue ReadBinding(ref BindingPath path)
    {
        return path.Device switch
        {
            DeviceType.Keyboard => ReadKeyboard(path.ControlCode),
            DeviceType.Mouse => ReadMouse(ref path),
            DeviceType.Gamepad => ReadGamepad(ref path),
            _ => InputValue.Zero
        };
    }

    public bool IsKeyDown(int keyCode)
    {
        SilkKey key = ToSilkKey((KeyboardKey)keyCode);
        if (key == SilkKey.Unknown)
        {
            return false;
        }
        return Derp.IsKeyDown(key);
    }

    public bool IsKeyPressed(int keyCode)
    {
        SilkKey key = ToSilkKey((KeyboardKey)keyCode);
        if (key == SilkKey.Unknown)
        {
            return false;
        }
        return Derp.IsKeyPressed(key);
    }

    public bool IsKeyReleased(int keyCode)
    {
        SilkKey key = ToSilkKey((KeyboardKey)keyCode);
        if (key == SilkKey.Unknown)
        {
            return false;
        }
        return Derp.IsKeyReleased(key);
    }

    public bool IsMouseButtonDown(int buttonCode)
    {
        if (!TryToSilkMouseButton((MouseButton)buttonCode, out var button))
        {
            return false;
        }
        return Derp.IsMouseButtonDown(button);
    }

    public bool IsMouseButtonPressed(int buttonCode)
    {
        if (!TryToSilkMouseButton((MouseButton)buttonCode, out var button))
        {
            return false;
        }
        return Derp.IsMouseButtonPressed(button);
    }

    public bool IsMouseButtonReleased(int buttonCode)
    {
        if (!TryToSilkMouseButton((MouseButton)buttonCode, out var button))
        {
            return false;
        }
        return Derp.IsMouseButtonReleased(button);
    }

    public bool IsGamepadButtonDown(int buttonCode) =>
        Derp.IsGamepadButtonDown(_gamepadIndex, (DerpGamepadButton)buttonCode);

    public bool IsGamepadButtonPressed(int buttonCode) =>
        Derp.IsGamepadButtonPressed(_gamepadIndex, (DerpGamepadButton)buttonCode);

    public bool IsGamepadButtonReleased(int buttonCode) =>
        Derp.IsGamepadButtonReleased(_gamepadIndex, (DerpGamepadButton)buttonCode);

    public float GetGamepadAxis(int axisCode) =>
        Derp.GetGamepadAxisMovement(_gamepadIndex, (DerpGamepadAxis)axisCode);

    private static InputValue ReadKeyboard(int keyCode)
    {
        SilkKey key = ToSilkKey((KeyboardKey)keyCode);
        if (key == SilkKey.Unknown)
        {
            return InputValue.Zero;
        }

        bool isDown = Derp.IsKeyDown(key);
        return InputValue.FromButton(isDown);
    }

    private InputValue ReadMouse(ref BindingPath path)
    {
        if (path.IsAxis)
        {
            return path.AxisCode switch
            {
                0 => InputValue.FromVector2(_mousePosition), // Position
                1 => InputValue.FromVector2(_mouseDelta), // Delta
                2 => InputValue.FromAxis(_mouseScroll), // Scroll
                _ => InputValue.Zero
            };
        }

        bool isDown = IsMouseButtonDown(path.ControlCode);
        return InputValue.FromButton(isDown);
    }

    private InputValue ReadGamepad(ref BindingPath path)
    {
        if (!Derp.IsGamepadAvailable(_gamepadIndex))
        {
            return InputValue.Zero;
        }

        if (path.IsAxis)
        {
            float axisValue = Derp.GetGamepadAxisMovement(_gamepadIndex, (DerpGamepadAxis)path.AxisCode);

            if (path.IsPositive)
            {
                return InputValue.FromAxis(Math.Max(0f, axisValue));
            }

            return InputValue.FromAxis(Math.Max(0f, -axisValue));
        }

        bool isDown = Derp.IsGamepadButtonDown(_gamepadIndex, (DerpGamepadButton)path.ControlCode);
        return InputValue.FromButton(isDown);
    }

    private static bool TryToSilkMouseButton(MouseButton button, out SilkMouseButton silkButton)
    {
        switch (button)
        {
            case MouseButton.Left:
                silkButton = SilkMouseButton.Left;
                return true;
            case MouseButton.Right:
                silkButton = SilkMouseButton.Right;
                return true;
            case MouseButton.Middle:
                silkButton = SilkMouseButton.Middle;
                return true;
            default:
                silkButton = default;
                return false;
        }
    }

    private static SilkKey ToSilkKey(KeyboardKey key)
    {
        return key switch
        {
            KeyboardKey.A => SilkKey.A,
            KeyboardKey.B => SilkKey.B,
            KeyboardKey.C => SilkKey.C,
            KeyboardKey.D => SilkKey.D,
            KeyboardKey.E => SilkKey.E,
            KeyboardKey.F => SilkKey.F,
            KeyboardKey.G => SilkKey.G,
            KeyboardKey.H => SilkKey.H,
            KeyboardKey.I => SilkKey.I,
            KeyboardKey.J => SilkKey.J,
            KeyboardKey.K => SilkKey.K,
            KeyboardKey.L => SilkKey.L,
            KeyboardKey.M => SilkKey.M,
            KeyboardKey.N => SilkKey.N,
            KeyboardKey.O => SilkKey.O,
            KeyboardKey.P => SilkKey.P,
            KeyboardKey.Q => SilkKey.Q,
            KeyboardKey.R => SilkKey.R,
            KeyboardKey.S => SilkKey.S,
            KeyboardKey.T => SilkKey.T,
            KeyboardKey.U => SilkKey.U,
            KeyboardKey.V => SilkKey.V,
            KeyboardKey.W => SilkKey.W,
            KeyboardKey.X => SilkKey.X,
            KeyboardKey.Y => SilkKey.Y,
            KeyboardKey.Z => SilkKey.Z,

            KeyboardKey.Space => SilkKey.Space,
            KeyboardKey.Enter => SilkKey.Enter,
            KeyboardKey.Escape => SilkKey.Escape,
            KeyboardKey.Tab => SilkKey.Tab,
            KeyboardKey.Backspace => SilkKey.Backspace,
            KeyboardKey.Delete => SilkKey.Delete,
            KeyboardKey.Insert => SilkKey.Insert,
            KeyboardKey.Home => SilkKey.Home,
            KeyboardKey.End => SilkKey.End,
            KeyboardKey.PageUp => SilkKey.PageUp,
            KeyboardKey.PageDown => SilkKey.PageDown,

            KeyboardKey.Up => SilkKey.Up,
            KeyboardKey.Down => SilkKey.Down,
            KeyboardKey.Left => SilkKey.Left,
            KeyboardKey.Right => SilkKey.Right,

            KeyboardKey.LeftShift => SilkKey.ShiftLeft,
            KeyboardKey.RightShift => SilkKey.ShiftRight,
            KeyboardKey.LeftControl => SilkKey.ControlLeft,
            KeyboardKey.RightControl => SilkKey.ControlRight,
            KeyboardKey.LeftAlt => SilkKey.AltLeft,
            KeyboardKey.RightAlt => SilkKey.AltRight,

            KeyboardKey.Zero => SilkKey.Number0,
            KeyboardKey.One => SilkKey.Number1,
            KeyboardKey.Two => SilkKey.Number2,
            KeyboardKey.Three => SilkKey.Number3,
            KeyboardKey.Four => SilkKey.Number4,
            KeyboardKey.Five => SilkKey.Number5,
            KeyboardKey.Six => SilkKey.Number6,
            KeyboardKey.Seven => SilkKey.Number7,
            KeyboardKey.Eight => SilkKey.Number8,
            KeyboardKey.Nine => SilkKey.Number9,

            KeyboardKey.F1 => SilkKey.F1,
            KeyboardKey.F2 => SilkKey.F2,
            KeyboardKey.F3 => SilkKey.F3,
            KeyboardKey.F4 => SilkKey.F4,
            KeyboardKey.F5 => SilkKey.F5,
            KeyboardKey.F6 => SilkKey.F6,
            KeyboardKey.F7 => SilkKey.F7,
            KeyboardKey.F8 => SilkKey.F8,
            KeyboardKey.F9 => SilkKey.F9,
            KeyboardKey.F10 => SilkKey.F10,
            KeyboardKey.F11 => SilkKey.F11,
            KeyboardKey.F12 => SilkKey.F12,

            KeyboardKey.Minus => SilkKey.Minus,
            KeyboardKey.Equal => SilkKey.Equal,
            KeyboardKey.LeftBracket => SilkKey.LeftBracket,
            KeyboardKey.RightBracket => SilkKey.RightBracket,
            KeyboardKey.Backslash => SilkKey.BackSlash,
            KeyboardKey.Semicolon => SilkKey.Semicolon,
            KeyboardKey.Apostrophe => SilkKey.Apostrophe,
            KeyboardKey.Comma => SilkKey.Comma,
            KeyboardKey.Period => SilkKey.Period,
            KeyboardKey.Slash => SilkKey.Slash,
            KeyboardKey.Grave => SilkKey.GraveAccent,

            _ => SilkKey.Unknown
        };
    }
}
