using Serilog;
using Silk.NET.SDL;

namespace DerpLib.Input;

/// <summary>
/// Internal SDL gamepad state manager. Handles up to 4 controllers with
/// double-buffered state for pressed/released detection and hotplug support.
/// </summary>
internal sealed class SdlGamepadState : IDisposable
{
    private const int MaxGamepads = 4;

    private readonly ILogger _log;
    private readonly Sdl _sdl;
    private bool _initialized;

    // Per-gamepad state
    private readonly nint[] _controllers = new nint[MaxGamepads];
    private readonly int[] _instanceIds = new int[MaxGamepads];
    private readonly string?[] _names = new string?[MaxGamepads];

    // Button states (double-buffered per gamepad)
    private const int MaxButtons = 18;
    private readonly bool[,] _buttonState = new bool[MaxGamepads, MaxButtons];
    private readonly bool[,] _buttonStatePrev = new bool[MaxGamepads, MaxButtons];

    // Axis states (current frame only, no edge detection needed)
    private const int MaxAxes = 6;
    private readonly float[,] _axisState = new float[MaxGamepads, MaxAxes];

    public SdlGamepadState(ILogger log)
    {
        _log = log;
        _sdl = Sdl.GetApi();

        // Initialize instance IDs to invalid
        for (int i = 0; i < MaxGamepads; i++)
        {
            _instanceIds[i] = -1;
        }
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        if (_sdl.Init(Sdl.InitGamecontroller) < 0)
        {
            _log.Error("Failed to initialize SDL GameController: {Error}", _sdl.GetErrorS());
            return;
        }

        _log.Information("SDL GameController subsystem initialized");
        _initialized = true;

        // Scan for already-connected controllers
        ScanForControllers();
    }

    private void ScanForControllers()
    {
        int numJoysticks = _sdl.NumJoysticks();
        for (int i = 0; i < numJoysticks; i++)
        {
            if (_sdl.IsGameController(i) == SdlBool.True)
            {
                TryOpenController(i);
            }
        }
    }

    private void TryOpenController(int deviceIndex)
    {
        // Find an empty slot
        int slot = -1;
        for (int i = 0; i < MaxGamepads; i++)
        {
            if (_controllers[i] == nint.Zero)
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
        {
            _log.Warning("Max gamepads reached, cannot open controller at device index {Index}", deviceIndex);
            return;
        }

        unsafe
        {
            var controller = _sdl.GameControllerOpen(deviceIndex);
            if (controller != null)
            {
                _controllers[slot] = (nint)controller;
                var joystick = _sdl.GameControllerGetJoystick(controller);
                _instanceIds[slot] = _sdl.JoystickInstanceID(joystick);
                _names[slot] = _sdl.GameControllerNameS(controller);
                _log.Information("Gamepad {Slot} connected: {Name} (instance {Id})", slot, _names[slot], _instanceIds[slot]);
            }
            else
            {
                _log.Error("Failed to open controller at device index {Index}: {Error}", deviceIndex, _sdl.GetErrorS());
            }
        }
    }

    private void CloseController(int instanceId)
    {
        for (int i = 0; i < MaxGamepads; i++)
        {
            if (_instanceIds[i] == instanceId)
            {
                unsafe
                {
                    _sdl.GameControllerClose((GameController*)_controllers[i]);
                }
                _log.Information("Gamepad {Slot} disconnected: {Name}", i, _names[i]);
                _controllers[i] = nint.Zero;
                _instanceIds[i] = -1;
                _names[i] = null;

                // Clear state
                for (int b = 0; b < MaxButtons; b++)
                {
                    _buttonState[i, b] = false;
                    _buttonStatePrev[i, b] = false;
                }
                for (int a = 0; a < MaxAxes; a++)
                {
                    _axisState[i, a] = 0f;
                }
                break;
            }
        }
    }

    /// <summary>
    /// Poll SDL events and update gamepad state. Call once per frame from EngineHost.PollEvents().
    /// </summary>
    public void Update()
    {
        if (!_initialized)
            return;

        // Process SDL events for controller connect/disconnect
        unsafe
        {
            Event evt;
            while (_sdl.PollEvent(&evt) != 0)
            {
                switch ((EventType)evt.Type)
                {
                    case EventType.Controllerdeviceadded:
                        TryOpenController(evt.Cdevice.Which);
                        break;

                    case EventType.Controllerdeviceremoved:
                        CloseController(evt.Cdevice.Which);
                        break;
                }
            }
        }

        // Copy current state to previous
        Array.Copy(_buttonState, _buttonStatePrev, _buttonState.Length);

        // Update current state for all connected controllers
        for (int i = 0; i < MaxGamepads; i++)
        {
            if (_controllers[i] == nint.Zero)
                continue;

            unsafe
            {
                var controller = (GameController*)_controllers[i];

                // Update buttons
                _buttonState[i, (int)GamepadButton.LeftFaceUp] = _sdl.GameControllerGetButton(controller, GameControllerButton.DpadUp) != 0;
                _buttonState[i, (int)GamepadButton.LeftFaceRight] = _sdl.GameControllerGetButton(controller, GameControllerButton.DpadRight) != 0;
                _buttonState[i, (int)GamepadButton.LeftFaceDown] = _sdl.GameControllerGetButton(controller, GameControllerButton.DpadDown) != 0;
                _buttonState[i, (int)GamepadButton.LeftFaceLeft] = _sdl.GameControllerGetButton(controller, GameControllerButton.DpadLeft) != 0;
                _buttonState[i, (int)GamepadButton.RightFaceUp] = _sdl.GameControllerGetButton(controller, GameControllerButton.Y) != 0;
                _buttonState[i, (int)GamepadButton.RightFaceRight] = _sdl.GameControllerGetButton(controller, GameControllerButton.B) != 0;
                _buttonState[i, (int)GamepadButton.RightFaceDown] = _sdl.GameControllerGetButton(controller, GameControllerButton.A) != 0;
                _buttonState[i, (int)GamepadButton.RightFaceLeft] = _sdl.GameControllerGetButton(controller, GameControllerButton.X) != 0;
                _buttonState[i, (int)GamepadButton.LeftTrigger1] = _sdl.GameControllerGetButton(controller, GameControllerButton.Leftshoulder) != 0;
                _buttonState[i, (int)GamepadButton.RightTrigger1] = _sdl.GameControllerGetButton(controller, GameControllerButton.Rightshoulder) != 0;
                _buttonState[i, (int)GamepadButton.MiddleLeft] = _sdl.GameControllerGetButton(controller, GameControllerButton.Back) != 0;
                _buttonState[i, (int)GamepadButton.Middle] = _sdl.GameControllerGetButton(controller, GameControllerButton.Guide) != 0;
                _buttonState[i, (int)GamepadButton.MiddleRight] = _sdl.GameControllerGetButton(controller, GameControllerButton.Start) != 0;
                _buttonState[i, (int)GamepadButton.LeftThumb] = _sdl.GameControllerGetButton(controller, GameControllerButton.Leftstick) != 0;
                _buttonState[i, (int)GamepadButton.RightThumb] = _sdl.GameControllerGetButton(controller, GameControllerButton.Rightstick) != 0;

                // Update axes (normalized to -1..1 or 0..1 for triggers)
                _axisState[i, (int)GamepadAxis.LeftX] = _sdl.GameControllerGetAxis(controller, GameControllerAxis.Leftx) / 32767f;
                _axisState[i, (int)GamepadAxis.LeftY] = _sdl.GameControllerGetAxis(controller, GameControllerAxis.Lefty) / 32767f;
                _axisState[i, (int)GamepadAxis.RightX] = _sdl.GameControllerGetAxis(controller, GameControllerAxis.Rightx) / 32767f;
                _axisState[i, (int)GamepadAxis.RightY] = _sdl.GameControllerGetAxis(controller, GameControllerAxis.Righty) / 32767f;
                _axisState[i, (int)GamepadAxis.LeftTrigger] = _sdl.GameControllerGetAxis(controller, GameControllerAxis.Triggerleft) / 32767f;
                _axisState[i, (int)GamepadAxis.RightTrigger] = _sdl.GameControllerGetAxis(controller, GameControllerAxis.Triggerright) / 32767f;

                // Treat triggers as buttons when > 0.5
                _buttonState[i, (int)GamepadButton.LeftTrigger2] = _axisState[i, (int)GamepadAxis.LeftTrigger] > 0.5f;
                _buttonState[i, (int)GamepadButton.RightTrigger2] = _axisState[i, (int)GamepadAxis.RightTrigger] > 0.5f;
            }
        }
    }

    public bool IsGamepadAvailable(int gamepad)
    {
        if (gamepad < 0 || gamepad >= MaxGamepads)
            return false;
        return _controllers[gamepad] != nint.Zero;
    }

    public string? GetGamepadName(int gamepad)
    {
        if (gamepad < 0 || gamepad >= MaxGamepads)
            return null;
        return _names[gamepad];
    }

    public bool IsGamepadButtonDown(int gamepad, GamepadButton button)
    {
        if (gamepad < 0 || gamepad >= MaxGamepads || _controllers[gamepad] == nint.Zero)
            return false;
        int b = (int)button;
        if (b < 0 || b >= MaxButtons)
            return false;
        return _buttonState[gamepad, b];
    }

    public bool IsGamepadButtonPressed(int gamepad, GamepadButton button)
    {
        if (gamepad < 0 || gamepad >= MaxGamepads || _controllers[gamepad] == nint.Zero)
            return false;
        int b = (int)button;
        if (b < 0 || b >= MaxButtons)
            return false;
        return _buttonState[gamepad, b] && !_buttonStatePrev[gamepad, b];
    }

    public bool IsGamepadButtonReleased(int gamepad, GamepadButton button)
    {
        if (gamepad < 0 || gamepad >= MaxGamepads || _controllers[gamepad] == nint.Zero)
            return false;
        int b = (int)button;
        if (b < 0 || b >= MaxButtons)
            return false;
        return !_buttonState[gamepad, b] && _buttonStatePrev[gamepad, b];
    }

    public float GetGamepadAxisMovement(int gamepad, GamepadAxis axis)
    {
        if (gamepad < 0 || gamepad >= MaxGamepads || _controllers[gamepad] == nint.Zero)
            return 0f;
        int a = (int)axis;
        if (a < 0 || a >= MaxAxes)
            return 0f;
        return _axisState[gamepad, a];
    }

    public int GetGamepadAxisCount(int gamepad)
    {
        if (gamepad < 0 || gamepad >= MaxGamepads || _controllers[gamepad] == nint.Zero)
            return 0;
        return MaxAxes;
    }

    public int GetGamepadButtonCount(int gamepad)
    {
        if (gamepad < 0 || gamepad >= MaxGamepads || _controllers[gamepad] == nint.Zero)
            return 0;
        return MaxButtons;
    }

    public void Dispose()
    {
        if (!_initialized)
            return;

        // Close all controllers
        for (int i = 0; i < MaxGamepads; i++)
        {
            if (_controllers[i] != nint.Zero)
            {
                unsafe
                {
                    _sdl.GameControllerClose((GameController*)_controllers[i]);
                }
                _controllers[i] = nint.Zero;
            }
        }

        _sdl.QuitSubSystem(Sdl.InitGamecontroller);
        _initialized = false;
    }
}
