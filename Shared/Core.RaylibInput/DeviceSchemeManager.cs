using System;
using Raylib_cs;
using RaylibAxis = Raylib_cs.GamepadAxis;
using RaylibButton = Raylib_cs.GamepadButton;
using RaylibMouseButton = Raylib_cs.MouseButton;

namespace Core.Input;

/// <summary>
/// Manages switching between device schemes.
/// Detects when user switches from keyboard/mouse to gamepad and vice versa.
/// </summary>
public sealed class DeviceSchemeManager
{
    private const int MaxSchemes = 4;
    private const float MouseMovementThreshold = 1f;
    private const float GamepadDeadZone = 0.2f;

    private readonly DeviceScheme[] _schemes;
    private int _schemeCount;
    private int _activeSchemeIndex;
    private readonly RaylibInputDevice _device;

    // Auto-switch detection
    private bool _autoSwitchEnabled = true;
    private double _lastKeyboardMouseTime;
    private double _lastGamepadTime;

    public DeviceScheme ActiveScheme => _schemeCount > 0 ? _schemes[_activeSchemeIndex] : DeviceScheme.All;
    public bool AutoSwitchEnabled
    {
        get => _autoSwitchEnabled;
        set => _autoSwitchEnabled = value;
    }

    public event Action<DeviceScheme>? OnSchemeChanged;

    public DeviceSchemeManager(RaylibInputDevice device)
    {
        _device = device;
        _schemes = new DeviceScheme[MaxSchemes];
        _schemeCount = 0;
        _activeSchemeIndex = 0;

        // Register default schemes
        RegisterScheme(DeviceScheme.KeyboardMouse);
        RegisterScheme(DeviceScheme.Gamepad);
    }

    public void RegisterScheme(DeviceScheme scheme)
    {
        if (_schemeCount >= MaxSchemes)
        {
            return;
        }
        _schemes[_schemeCount++] = scheme;
    }

    public void SetActiveScheme(StringHandle schemeName)
    {
        for (int i = 0; i < _schemeCount; i++)
        {
            if (_schemes[i].Name == schemeName)
            {
                if (_activeSchemeIndex != i)
                {
                    _activeSchemeIndex = i;
                    OnSchemeChanged?.Invoke(_schemes[i]);
                }
                return;
            }
        }
    }

    /// <summary>
    /// Check for device activity and auto-switch if enabled.
    /// Call each frame.
    /// </summary>
    public void Update(double time)
    {
        if (!_autoSwitchEnabled)
        {
            return;
        }

        // Detect keyboard/mouse activity
        bool keyboardMouseActive = DetectKeyboardMouseActivity();
        if (keyboardMouseActive)
        {
            _lastKeyboardMouseTime = time;
        }

        // Detect gamepad activity
        bool gamepadActive = DetectGamepadActivity();
        if (gamepadActive)
        {
            _lastGamepadTime = time;
        }

        // Switch based on most recent activity
        DeviceScheme currentScheme = _schemes[_activeSchemeIndex];

        if (gamepadActive && currentScheme.Name != DeviceScheme.Gamepad.Name)
        {
            SetActiveScheme(DeviceScheme.Gamepad.Name);
        }
        else if (keyboardMouseActive && currentScheme.Name != DeviceScheme.KeyboardMouse.Name)
        {
            SetActiveScheme(DeviceScheme.KeyboardMouse.Name);
        }
    }

    private bool DetectKeyboardMouseActivity()
    {
        // Check for any key press
        int key = Raylib.GetKeyPressed();
        if (key != 0)
        {
            return true;
        }

        // Check for mouse button
        if (Raylib.IsMouseButtonPressed(RaylibMouseButton.Left) ||
            Raylib.IsMouseButtonPressed(RaylibMouseButton.Right) ||
            Raylib.IsMouseButtonPressed(RaylibMouseButton.Middle))
        {
            return true;
        }

        // Check for mouse movement
        var delta = Raylib.GetMouseDelta();
        if (delta.X * delta.X + delta.Y * delta.Y > MouseMovementThreshold)
        {
            return true;
        }

        // Check for scroll wheel
        if (Math.Abs(Raylib.GetMouseWheelMove()) > 0f)
        {
            return true;
        }

        return false;
    }

    private bool DetectGamepadActivity()
    {
        if (!_device.IsGamepadConnected)
        {
            return false;
        }

        int gamepadIndex = _device.GamepadIndex;

        // Check buttons (check common buttons, not all 16)
        for (int i = 0; i < 16; i++)
        {
            if (Raylib.IsGamepadButtonPressed(gamepadIndex, (RaylibButton)i))
            {
                return true;
            }
        }

        // Check axes (with dead zone)
        for (int i = 0; i < 6; i++)
        {
            float value = Raylib.GetGamepadAxisMovement(gamepadIndex, (RaylibAxis)i);
            if (Math.Abs(value) > GamepadDeadZone)
            {
                return true;
            }
        }

        return false;
    }
}
