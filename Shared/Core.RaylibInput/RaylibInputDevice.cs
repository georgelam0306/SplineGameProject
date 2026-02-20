using System;
using System.Numerics;
using Raylib_cs;
using RaylibAxis = Raylib_cs.GamepadAxis;
using RaylibButton = Raylib_cs.GamepadButton;
using RaylibKey = Raylib_cs.KeyboardKey;
using RaylibMouseButton = Raylib_cs.MouseButton;

namespace Core.Input;

/// <summary>
/// Abstracts Raylib input functions for unified device handling.
/// Caches state to support consistent reads within a frame.
/// </summary>
public sealed class RaylibInputDevice
    : IInputDevice
{
    private int _gamepadIndex = 0;

    // Modifier key cache
    private bool _shiftHeld;
    private bool _ctrlHeld;
    private bool _altHeld;

    // Mouse state
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private float _mouseScroll;

    // Cursor confinement
    private bool _cursorConfined;
    private const float ConfinementMargin = 80f;

    // Software cursor
    private bool _softwareCursorEnabled;
    private Vector2 _virtualCursorPosition;

    public int GamepadIndex
    {
        get => _gamepadIndex;
        set => _gamepadIndex = value;
    }

    public bool IsGamepadConnected => Raylib.IsGamepadAvailable(_gamepadIndex);

    /// <summary>
    /// When enabled, the mouse cursor will be confined to the window bounds.
    /// </summary>
    public bool CursorConfined
    {
        get => _cursorConfined;
        set => _cursorConfined = value;
    }

    /// <summary>
    /// Returns true if software cursor mode is enabled (real cursor hidden).
    /// </summary>
    public bool SoftwareCursorEnabled => _softwareCursorEnabled;

    /// <summary>
    /// Enables software cursor mode: locks and hides real cursor, tracks position via delta.
    /// </summary>
    public void EnableSoftwareCursor()
    {
        if (_softwareCursorEnabled) return;

        _softwareCursorEnabled = true;
        _virtualCursorPosition = Raylib.GetMousePosition();
        Raylib.DisableCursor(); // Hides AND locks cursor to window
    }

    /// <summary>
    /// Disables software cursor mode: unlocks and shows real cursor.
    /// </summary>
    public void DisableSoftwareCursor()
    {
        if (!_softwareCursorEnabled) return;

        _softwareCursorEnabled = false;
        Raylib.EnableCursor(); // Shows AND unlocks cursor
    }

    /// <summary>
    /// Call at the start of each frame before processing actions.
    /// </summary>
    public void Update()
    {
        // Cache modifier states
        _shiftHeld = Raylib.IsKeyDown(RaylibKey.LeftShift) || Raylib.IsKeyDown(RaylibKey.RightShift);
        _ctrlHeld = Raylib.IsKeyDown(RaylibKey.LeftControl) || Raylib.IsKeyDown(RaylibKey.RightControl);
        _altHeld = Raylib.IsKeyDown(RaylibKey.LeftAlt) || Raylib.IsKeyDown(RaylibKey.RightAlt);

        // Cache mouse state
        _mouseDelta = Raylib.GetMouseDelta();
        _mouseScroll = Raylib.GetMouseWheelMove();

        if (_softwareCursorEnabled)
        {
            // Track virtual cursor position via delta
            _virtualCursorPosition += _mouseDelta;

            // Clamp to window bounds
            int screenWidth = Raylib.GetScreenWidth();
            int screenHeight = Raylib.GetScreenHeight();
            _virtualCursorPosition.X = Math.Clamp(_virtualCursorPosition.X, 0, screenWidth);
            _virtualCursorPosition.Y = Math.Clamp(_virtualCursorPosition.Y, 0, screenHeight);

            _mousePosition = _virtualCursorPosition;
        }
        else
        {
            _mousePosition = Raylib.GetMousePosition();

            // Confine cursor to window bounds (only when not using software cursor)
            if (_cursorConfined)
            {
                ConfineCursorToWindow();
            }
        }
    }

    private void ConfineCursorToWindow()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        float clampedX = Math.Clamp(_mousePosition.X, ConfinementMargin, screenWidth - ConfinementMargin);
        float clampedY = Math.Clamp(_mousePosition.Y, ConfinementMargin, screenHeight - ConfinementMargin);

        if (clampedX != _mousePosition.X || clampedY != _mousePosition.Y)
        {
            Raylib.SetMousePosition((int)clampedX, (int)clampedY);
            _mousePosition = new Vector2(clampedX, clampedY);
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

    private InputValue ReadKeyboard(int keyCode)
    {
        RaylibKey key = ToRaylibKey((KeyboardKey)keyCode);
        if (key == RaylibKey.Null)
        {
            return InputValue.Zero;
        }

        bool isDown = Raylib.IsKeyDown(key);
        return InputValue.FromButton(isDown);
    }

    private InputValue ReadMouse(ref BindingPath path)
    {
        if (path.IsAxis)
        {
            return path.AxisCode switch
            {
                0 => InputValue.FromVector2(_mousePosition),    // Position
                1 => InputValue.FromVector2(_mouseDelta),       // Delta
                2 => InputValue.FromAxis(_mouseScroll),         // Scroll
                _ => InputValue.Zero
            };
        }

        RaylibMouseButton button = ToRaylibMouseButton((MouseButton)path.ControlCode);
        bool isDown = Raylib.IsMouseButtonDown(button);
        return InputValue.FromButton(isDown);
    }

    private InputValue ReadGamepad(ref BindingPath path)
    {
        if (!Raylib.IsGamepadAvailable(_gamepadIndex))
        {
            return InputValue.Zero;
        }

        if (path.IsAxis)
        {
            float axisValue = Raylib.GetGamepadAxisMovement(_gamepadIndex, (RaylibAxis)path.AxisCode);

            // For directional bindings (leftStick/up), convert to button-like value
            if (path.IsPositive)
            {
                return InputValue.FromAxis(Math.Max(0f, axisValue));
            }
            else
            {
                return InputValue.FromAxis(Math.Max(0f, -axisValue));
            }
        }

        bool isDown = Raylib.IsGamepadButtonDown(_gamepadIndex, (RaylibButton)path.ControlCode);
        return InputValue.FromButton(isDown);
    }

    // Convenience methods for direct access (not action-based)
    public bool IsKeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(ToRaylibKey(key));
    public bool IsKeyDown(KeyboardKey key) => Raylib.IsKeyDown(ToRaylibKey(key));
    public bool IsKeyReleased(KeyboardKey key) => Raylib.IsKeyReleased(ToRaylibKey(key));

    public bool IsMouseButtonPressed(MouseButton button) => Raylib.IsMouseButtonPressed(ToRaylibMouseButton(button));
    public bool IsMouseButtonDown(MouseButton button) => Raylib.IsMouseButtonDown(ToRaylibMouseButton(button));
    public bool IsMouseButtonReleased(MouseButton button) => Raylib.IsMouseButtonReleased(ToRaylibMouseButton(button));

    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;
    public float MouseScroll => _mouseScroll;

    public bool IsGamepadButtonPressed(GamepadButton button) =>
        Raylib.IsGamepadAvailable(_gamepadIndex) && Raylib.IsGamepadButtonPressed(_gamepadIndex, (RaylibButton)button);
    public bool IsGamepadButtonDown(GamepadButton button) =>
        Raylib.IsGamepadAvailable(_gamepadIndex) && Raylib.IsGamepadButtonDown(_gamepadIndex, (RaylibButton)button);
    public bool IsGamepadButtonReleased(GamepadButton button) =>
        Raylib.IsGamepadAvailable(_gamepadIndex) && Raylib.IsGamepadButtonReleased(_gamepadIndex, (RaylibButton)button);
    public float GetGamepadAxis(GamepadAxis axis) =>
        Raylib.IsGamepadAvailable(_gamepadIndex) ? Raylib.GetGamepadAxisMovement(_gamepadIndex, (RaylibAxis)axis) : 0f;

    private static RaylibKey ToRaylibKey(KeyboardKey key)
    {
        return key switch
        {
            KeyboardKey.A => RaylibKey.A,
            KeyboardKey.B => RaylibKey.B,
            KeyboardKey.C => RaylibKey.C,
            KeyboardKey.D => RaylibKey.D,
            KeyboardKey.E => RaylibKey.E,
            KeyboardKey.F => RaylibKey.F,
            KeyboardKey.G => RaylibKey.G,
            KeyboardKey.H => RaylibKey.H,
            KeyboardKey.I => RaylibKey.I,
            KeyboardKey.J => RaylibKey.J,
            KeyboardKey.K => RaylibKey.K,
            KeyboardKey.L => RaylibKey.L,
            KeyboardKey.M => RaylibKey.M,
            KeyboardKey.N => RaylibKey.N,
            KeyboardKey.O => RaylibKey.O,
            KeyboardKey.P => RaylibKey.P,
            KeyboardKey.Q => RaylibKey.Q,
            KeyboardKey.R => RaylibKey.R,
            KeyboardKey.S => RaylibKey.S,
            KeyboardKey.T => RaylibKey.T,
            KeyboardKey.U => RaylibKey.U,
            KeyboardKey.V => RaylibKey.V,
            KeyboardKey.W => RaylibKey.W,
            KeyboardKey.X => RaylibKey.X,
            KeyboardKey.Y => RaylibKey.Y,
            KeyboardKey.Z => RaylibKey.Z,

            KeyboardKey.Space => RaylibKey.Space,
            KeyboardKey.Enter => RaylibKey.Enter,
            KeyboardKey.Escape => RaylibKey.Escape,
            KeyboardKey.Tab => RaylibKey.Tab,
            KeyboardKey.Backspace => RaylibKey.Backspace,
            KeyboardKey.Delete => RaylibKey.Delete,
            KeyboardKey.Insert => RaylibKey.Insert,
            KeyboardKey.Home => RaylibKey.Home,
            KeyboardKey.End => RaylibKey.End,
            KeyboardKey.PageUp => RaylibKey.PageUp,
            KeyboardKey.PageDown => RaylibKey.PageDown,

            KeyboardKey.Up => RaylibKey.Up,
            KeyboardKey.Down => RaylibKey.Down,
            KeyboardKey.Left => RaylibKey.Left,
            KeyboardKey.Right => RaylibKey.Right,

            KeyboardKey.LeftShift => RaylibKey.LeftShift,
            KeyboardKey.RightShift => RaylibKey.RightShift,
            KeyboardKey.LeftControl => RaylibKey.LeftControl,
            KeyboardKey.RightControl => RaylibKey.RightControl,
            KeyboardKey.LeftAlt => RaylibKey.LeftAlt,
            KeyboardKey.RightAlt => RaylibKey.RightAlt,

            KeyboardKey.Zero => RaylibKey.Zero,
            KeyboardKey.One => RaylibKey.One,
            KeyboardKey.Two => RaylibKey.Two,
            KeyboardKey.Three => RaylibKey.Three,
            KeyboardKey.Four => RaylibKey.Four,
            KeyboardKey.Five => RaylibKey.Five,
            KeyboardKey.Six => RaylibKey.Six,
            KeyboardKey.Seven => RaylibKey.Seven,
            KeyboardKey.Eight => RaylibKey.Eight,
            KeyboardKey.Nine => RaylibKey.Nine,

            KeyboardKey.F1 => RaylibKey.F1,
            KeyboardKey.F2 => RaylibKey.F2,
            KeyboardKey.F3 => RaylibKey.F3,
            KeyboardKey.F4 => RaylibKey.F4,
            KeyboardKey.F5 => RaylibKey.F5,
            KeyboardKey.F6 => RaylibKey.F6,
            KeyboardKey.F7 => RaylibKey.F7,
            KeyboardKey.F8 => RaylibKey.F8,
            KeyboardKey.F9 => RaylibKey.F9,
            KeyboardKey.F10 => RaylibKey.F10,
            KeyboardKey.F11 => RaylibKey.F11,
            KeyboardKey.F12 => RaylibKey.F12,

            KeyboardKey.Minus => RaylibKey.Minus,
            KeyboardKey.Equal => RaylibKey.Equal,
            KeyboardKey.LeftBracket => RaylibKey.LeftBracket,
            KeyboardKey.RightBracket => RaylibKey.RightBracket,
            KeyboardKey.Backslash => RaylibKey.Backslash,
            KeyboardKey.Semicolon => RaylibKey.Semicolon,
            KeyboardKey.Apostrophe => RaylibKey.Apostrophe,
            KeyboardKey.Comma => RaylibKey.Comma,
            KeyboardKey.Period => RaylibKey.Period,
            KeyboardKey.Slash => RaylibKey.Slash,
            KeyboardKey.Grave => RaylibKey.Grave,

            _ => RaylibKey.Null
        };
    }

    private static RaylibMouseButton ToRaylibMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => RaylibMouseButton.Left,
            MouseButton.Right => RaylibMouseButton.Right,
            MouseButton.Middle => RaylibMouseButton.Middle,
            MouseButton.Back => RaylibMouseButton.Back,
            MouseButton.Forward => RaylibMouseButton.Forward,
            _ => RaylibMouseButton.Left
        };
    }
}
