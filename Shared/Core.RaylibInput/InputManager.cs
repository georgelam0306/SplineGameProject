using System.Collections.Generic;
using System.Numerics;

namespace Core.Input;

/// <summary>
/// Raylib-based input manager. Owns action maps, device, scheme manager, and context stack.
/// Zero-allocation per-frame update.
/// Use this when using Raylib for windowing/input.
/// </summary>
public sealed class InputManager : IInputManager
{
    private const int MaxActionMaps = 16;
    private const int MaxContextStack = 8;

    private readonly InputActionMap[] _actionMaps;
    private int _actionMapCount;

    private readonly RaylibInputDevice _device;
    private readonly DeviceSchemeManager _schemeManager;

    // Action lookup by name
    private readonly Dictionary<StringHandle, int> _mapLookup;

    // Context stack (push/pop semantics for input contexts)
    private readonly List<ContextStackEntry> _contextStack;

    // Global callback for all actions
    private ActionCallbackHandler? _globalCallback;

    // Time tracking
    private double _time;

    // Input buffering (optional, enabled via EnableBuffering)
    private InputHistoryBuffer? _historyBuffer;
    private DiscreteActionBuffer? _discreteBuffer;
    private ulong _frameNumber;

    public RaylibInputDevice Device => _device;
    public DeviceSchemeManager SchemeManager => _schemeManager;

    /// <summary>The current context stack (read-only view).</summary>
    public IReadOnlyList<ContextStackEntry> ContextStack => _contextStack;

    /// <summary>The top context on the stack, or null if empty.</summary>
    public StringHandle? TopContext => _contextStack.Count > 0 ? _contextStack[^1].Name : null;

    /// <summary>Input history buffer (null if buffering not enabled).</summary>
    public InputHistoryBuffer? HistoryBuffer => _historyBuffer;

    /// <summary>Discrete action buffer (null if buffering not enabled).</summary>
    public DiscreteActionBuffer? DiscreteBuffer => _discreteBuffer;

    /// <summary>Current frame number (set by CaptureSnapshot).</summary>
    public ulong FrameNumber => _frameNumber;

    public InputManager()
    {
        _device = new RaylibInputDevice();
        _schemeManager = new DeviceSchemeManager(_device);
        _actionMaps = new InputActionMap[MaxActionMaps];
        _actionMapCount = 0;
        _mapLookup = new Dictionary<StringHandle, int>(MaxActionMaps);
        _contextStack = new List<ContextStackEntry>(MaxContextStack);
    }

    /// <summary>
    /// Create and register an action map.
    /// </summary>
    public InputActionMap CreateActionMap(StringHandle name)
    {
        if (_actionMapCount >= MaxActionMaps)
        {
            return null!;
        }

        var map = new InputActionMap(name);
        _mapLookup[name] = _actionMapCount;
        _actionMaps[_actionMapCount++] = map;
        return map;
    }

    /// <summary>
    /// Get an action map by name.
    /// </summary>
    public InputActionMap? GetActionMap(StringHandle name)
    {
        if (_mapLookup.TryGetValue(name, out int index))
        {
            return _actionMaps[index];
        }
        return null;
    }

    public InputActionMap? this[string name] => GetActionMap(name);

    /// <summary>
    /// Enable an action map (context).
    /// </summary>
    public void EnableActionMap(StringHandle name)
    {
        GetActionMap(name)?.Enable();
    }

    /// <summary>
    /// Disable an action map (context).
    /// </summary>
    public void DisableActionMap(StringHandle name)
    {
        GetActionMap(name)?.Disable();
    }

    /// <summary>
    /// Switch to a single active action map, disabling all others.
    /// </summary>
    public void SwitchToActionMap(StringHandle name)
    {
        for (int i = 0; i < _actionMapCount; i++)
        {
            if (_actionMaps[i].Name == name)
            {
                _actionMaps[i].Enable();
            }
            else
            {
                _actionMaps[i].Disable();
            }
        }
    }

    #region Context Stack

    /// <summary>
    /// Push a context onto the stack.
    /// The corresponding action map (with the same name) will be enabled
    /// and initialized with current device state to avoid a "zero frame".
    /// </summary>
    public void PushContext(StringHandle name, ContextBlockPolicy policy = default)
    {
        if (_contextStack.Count >= MaxContextStack)
        {
            return;
        }

        _contextStack.Add(new ContextStackEntry(name, policy));
        GetActionMap(name)?.Enable(_device);
    }

    /// <summary>
    /// Pop the top context from the stack.
    /// </summary>
    public void PopContext()
    {
        if (_contextStack.Count == 0)
        {
            return;
        }

        var entry = _contextStack[^1];
        _contextStack.RemoveAt(_contextStack.Count - 1);

        // Disable action map if no longer on stack
        if (!IsOnStack(entry.Name))
        {
            GetActionMap(entry.Name)?.Disable();
        }
    }

    /// <summary>
    /// Pop down to and including the named context.
    /// </summary>
    public void PopContext(StringHandle name)
    {
        for (int i = _contextStack.Count - 1; i >= 0; i--)
        {
            var entry = _contextStack[i];
            _contextStack.RemoveAt(i);

            // Disable action map if no longer on stack
            if (!IsOnStack(entry.Name))
            {
                GetActionMap(entry.Name)?.Disable();
            }

            if (entry.Name == name)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Check if a context is active (on the stack AND not blocked by any context above it).
    /// </summary>
    public bool IsContextActive(StringHandle name)
    {
        // Find the context on the stack
        int contextIndex = -1;
        for (int i = 0; i < _contextStack.Count; i++)
        {
            if (_contextStack[i].Name == name)
            {
                contextIndex = i;
                break;
            }
        }

        if (contextIndex < 0)
        {
            return false; // Not on stack
        }

        // Check if any context above it blocks this one
        for (int i = contextIndex + 1; i < _contextStack.Count; i++)
        {
            if (_contextStack[i].Policy.IsBlocked(name))
            {
                return false; // Blocked by a context above
            }
        }

        return true;
    }

    /// <summary>
    /// Check if a context is on the stack (regardless of blocking).
    /// </summary>
    public bool IsOnStack(StringHandle name)
    {
        for (int i = 0; i < _contextStack.Count; i++)
        {
            if (_contextStack[i].Name == name)
            {
                return true;
            }
        }
        return false;
    }

    #endregion

    /// <summary>
    /// Set a global callback for all action events.
    /// </summary>
    public void SetGlobalCallback(ActionCallbackHandler? callback)
    {
        _globalCallback = callback;
    }

    /// <summary>
    /// Call at the start of each frame.
    /// </summary>
    public void Update(double deltaTime)
    {
        _time += deltaTime;

        // Update device state
        _device.Update();

        // Update scheme manager (auto-switch detection)
        _schemeManager.Update(_time);

        // Update action maps based on context stack
        // If context stack is empty, fall back to updating all enabled maps
        if (_contextStack.Count == 0)
        {
            for (int i = 0; i < _actionMapCount; i++)
            {
                _actionMaps[i].Update(_device, _time, _globalCallback);
            }
        }
        else
        {
            // Only update action maps for active contexts
            for (int i = 0; i < _actionMapCount; i++)
            {
                var map = _actionMaps[i];
                if (IsContextActive(map.Name))
                {
                    map.Update(_device, _time, _globalCallback);
                }
                // Blocked contexts keep their current values (don't update)
            }
        }
    }

    /// <summary>
    /// Convenience method to read an action's current value.
    /// </summary>
    public InputValue ReadAction(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Value ?? InputValue.Zero;
    }

    /// <summary>
    /// Convenience method to check if an action was started this frame (just pressed).
    /// </summary>
    public bool WasPerformed(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Phase == ActionPhase.Started;
    }

    /// <summary>
    /// Convenience method to check if an action is currently active (held).
    /// </summary>
    public bool IsActive(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Phase == ActionPhase.Started || action?.Phase == ActionPhase.Performed;
    }

    /// <summary>
    /// Convenience method to check if an action was canceled this frame (just released).
    /// </summary>
    public bool WasCanceled(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Phase == ActionPhase.Canceled;
    }

    /// <summary>
    /// Get the current phase of an action.
    /// </summary>
    public ActionPhase GetActionPhase(StringHandle mapName, StringHandle actionName)
    {
        var map = GetActionMap(mapName);
        var action = map?.GetAction(actionName);
        return action?.Phase ?? ActionPhase.Disabled;
    }

    // Direct device access for non-action-based input
    public Vector2 MousePosition => _device.MousePosition;
    public Vector2 MouseDelta => _device.MouseDelta;
    public float MouseScroll => _device.MouseScroll;

    // Raylib-typed methods
    public bool IsKeyPressed(KeyboardKey key) => _device.IsKeyPressed(key);
    public bool IsKeyDown(KeyboardKey key) => _device.IsKeyDown(key);
    public bool IsKeyReleased(KeyboardKey key) => _device.IsKeyReleased(key);

    // IInputManager interface methods (int-based for abstraction)
    bool IInputManager.IsKeyPressed(int key) => _device.IsKeyPressed((KeyboardKey)key);
    bool IInputManager.IsKeyDown(int key) => _device.IsKeyDown((KeyboardKey)key);
    bool IInputManager.IsKeyReleased(int key) => _device.IsKeyReleased((KeyboardKey)key);

    // Raylib-typed methods
    public bool IsMouseButtonPressed(MouseButton button) => _device.IsMouseButtonPressed(button);
    public bool IsMouseButtonDown(MouseButton button) => _device.IsMouseButtonDown(button);
    public bool IsMouseButtonReleased(MouseButton button) => _device.IsMouseButtonReleased(button);

    // IInputManager interface methods (int-based for abstraction)
    bool IInputManager.IsMouseButtonPressed(int button) => _device.IsMouseButtonPressed((MouseButton)button);
    bool IInputManager.IsMouseButtonDown(int button) => _device.IsMouseButtonDown((MouseButton)button);
    bool IInputManager.IsMouseButtonReleased(int button) => _device.IsMouseButtonReleased((MouseButton)button);

    // Gamepad - Raylib-typed methods
    public bool IsGamepadButtonPressed(GamepadButton button) => _device.IsGamepadButtonPressed(button);
    public bool IsGamepadButtonDown(GamepadButton button) => _device.IsGamepadButtonDown(button);
    public float GetGamepadAxis(GamepadAxis axis) => _device.GetGamepadAxis(axis);
    public bool IsGamepadConnected => _device.IsGamepadConnected;

    // Gamepad - IInputManager interface methods (int-based)
    public int GamepadIndex
    {
        get => _device.GamepadIndex;
        set => _device.GamepadIndex = value;
    }

    bool IInputManager.IsGamepadButtonDown(int button) => _device.IsGamepadButtonDown((GamepadButton)button);
    bool IInputManager.IsGamepadButtonPressed(int button) => _device.IsGamepadButtonPressed((GamepadButton)button);
    bool IInputManager.IsGamepadButtonReleased(int button) => _device.IsGamepadButtonReleased((GamepadButton)button);
    float IInputManager.GetGamepadAxis(int axis) => _device.GetGamepadAxis((GamepadAxis)axis);

    #region Input Buffering

    /// <summary>
    /// Enable input buffering with specified history capacity.
    /// Call once during initialization.
    /// </summary>
    /// <param name="historyFrames">Number of frames to store in history buffer.</param>
    public void EnableBuffering(int historyFrames = InputHistoryBuffer.DefaultCapacity)
    {
        _historyBuffer = new InputHistoryBuffer(historyFrames);
        _discreteBuffer = new DiscreteActionBuffer();
    }

    /// <summary>
    /// Register an action for discrete event buffering (last-wins semantics).
    /// Call during setup for actions that need consumption by simulation ticks.
    /// </summary>
    public void RegisterDiscreteAction(StringHandle actionName)
    {
        _discreteBuffer?.RegisterAction(actionName);
    }

    /// <summary>
    /// Capture current action states to the history buffer.
    /// Call after Update() at the end of each render frame.
    /// </summary>
    /// <param name="frameNumber">The current frame number.</param>
    public void CaptureSnapshot(ulong frameNumber)
    {
        if (_historyBuffer == null)
            return;

        _frameNumber = frameNumber;
        _historyBuffer.BeginFrame(frameNumber);

        // Iterate all action maps and capture enabled action states
        for (int m = 0; m < _actionMapCount; m++)
        {
            var map = _actionMaps[m];
            if (!map.Enabled)
                continue;

            for (int a = 0; a < map.ActionCount; a++)
            {
                var action = map.GetActionByIndex(a);
                if (action == null)
                    continue;

                var snapshot = new ActionSnapshot(
                    frameNumber,
                    action.Name,
                    action.Phase,
                    action.Type,
                    action.Value
                );

                _historyBuffer.AddSnapshot(in snapshot);

                // Buffer discrete events (Started/Canceled)
                if (_discreteBuffer != null &&
                    (action.Phase == ActionPhase.Started || action.Phase == ActionPhase.Canceled))
                {
                    _discreteBuffer.Buffer(in snapshot);
                }
            }
        }

        _historyBuffer.EndFrame();
    }

    /// <summary>
    /// Check if an action was started within the last N frames.
    /// Requires buffering to be enabled.
    /// </summary>
    public bool WasActionStartedWithinFrames(StringHandle actionName, int withinFrames = 1)
    {
        return _historyBuffer?.WasStartedWithinFrames(actionName, withinFrames) ?? false;
    }

    /// <summary>
    /// Check if an action was canceled within the last N frames.
    /// Requires buffering to be enabled.
    /// </summary>
    public bool WasActionCanceledWithinFrames(StringHandle actionName, int withinFrames = 1)
    {
        return _historyBuffer?.WasCanceledWithinFrames(actionName, withinFrames) ?? false;
    }

    /// <summary>
    /// Get the most recent snapshot for an action from the history buffer.
    /// </summary>
    public bool TryGetLatestSnapshot(StringHandle actionName, out ActionSnapshot snapshot)
    {
        if (_historyBuffer != null)
        {
            return _historyBuffer.TryGetLatest(actionName, out snapshot);
        }
        snapshot = default;
        return false;
    }

    #endregion
}
