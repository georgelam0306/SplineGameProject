using System.Numerics;
using Core;

namespace Core.Input;

/// <summary>
/// Interface for input managers that support action-based input, context stacks,
/// and optional input buffering for simulation tick decoupling.
/// </summary>
public interface IInputManager
{
    #region Update

    /// <summary>
    /// Call at the start of each frame to update device state and process actions.
    /// </summary>
    void Update(double deltaTime);

    #endregion

    #region Action Maps

    /// <summary>
    /// Create and register a new action map.
    /// </summary>
    InputActionMap CreateActionMap(StringHandle name);

    /// <summary>
    /// Get an action map by name.
    /// </summary>
    InputActionMap? GetActionMap(StringHandle name);

    /// <summary>
    /// Get an action map by string name (convenience).
    /// </summary>
    InputActionMap? this[string name] { get; }

    /// <summary>
    /// Enable an action map by name.
    /// </summary>
    void EnableActionMap(StringHandle name);

    /// <summary>
    /// Disable an action map by name.
    /// </summary>
    void DisableActionMap(StringHandle name);

    /// <summary>
    /// Switch to a single active action map, disabling all others.
    /// </summary>
    void SwitchToActionMap(StringHandle name);

    #endregion

    #region Context Stack

    /// <summary>
    /// Push a context onto the stack. The corresponding action map will be enabled.
    /// </summary>
    void PushContext(StringHandle name, ContextBlockPolicy policy = default);

    /// <summary>
    /// Pop the top context from the stack.
    /// </summary>
    void PopContext();

    /// <summary>
    /// Pop down to and including the named context.
    /// </summary>
    void PopContext(StringHandle name);

    /// <summary>
    /// Check if a context is active (on the stack AND not blocked).
    /// </summary>
    bool IsContextActive(StringHandle name);

    /// <summary>
    /// Check if a context is on the stack (regardless of blocking).
    /// </summary>
    bool IsOnStack(StringHandle name);

    /// <summary>
    /// The top context on the stack, or null if empty.
    /// </summary>
    StringHandle? TopContext { get; }

    #endregion

    #region Action Queries

    /// <summary>
    /// Read an action's current value.
    /// </summary>
    InputValue ReadAction(StringHandle mapName, StringHandle actionName);

    /// <summary>
    /// Check if an action was started this frame (just pressed).
    /// </summary>
    bool WasPerformed(StringHandle mapName, StringHandle actionName);

    /// <summary>
    /// Check if an action is currently active (held).
    /// </summary>
    bool IsActive(StringHandle mapName, StringHandle actionName);

    /// <summary>
    /// Check if an action was canceled this frame (just released).
    /// </summary>
    bool WasCanceled(StringHandle mapName, StringHandle actionName);

    /// <summary>
    /// Get the current phase of an action.
    /// </summary>
    ActionPhase GetActionPhase(StringHandle mapName, StringHandle actionName);

    #endregion

    #region Direct Device Access

    /// <summary>
    /// Current mouse position in screen coordinates.
    /// </summary>
    Vector2 MousePosition { get; }

    /// <summary>
    /// Mouse movement since last frame.
    /// </summary>
    Vector2 MouseDelta { get; }

    /// <summary>
    /// Scroll wheel delta since last frame.
    /// </summary>
    float MouseScroll { get; }

    /// <summary>
     /// Check if a key is currently held down.
     /// Key codes are <see cref="KeyboardKey"/> values cast to int.
     /// </summary>
    bool IsKeyDown(int key);

    /// <summary>
     /// Check if a key was pressed this frame.
     /// Key codes are <see cref="KeyboardKey"/> values cast to int.
     /// </summary>
    bool IsKeyPressed(int key);

    /// <summary>
     /// Check if a key was released this frame.
     /// Key codes are <see cref="KeyboardKey"/> values cast to int.
     /// </summary>
    bool IsKeyReleased(int key);

    /// <summary>
     /// Check if a mouse button is currently held down.
     /// Button codes are <see cref="MouseButton"/> values cast to int.
     /// </summary>
    bool IsMouseButtonDown(int button);

    /// <summary>
     /// Check if a mouse button was pressed this frame.
     /// Button codes are <see cref="MouseButton"/> values cast to int.
     /// </summary>
    bool IsMouseButtonPressed(int button);

    /// <summary>
     /// Check if a mouse button was released this frame.
     /// Button codes are <see cref="MouseButton"/> values cast to int.
     /// </summary>
    bool IsMouseButtonReleased(int button);

    #endregion

    #region Gamepad

    /// <summary>
    /// Whether a gamepad is connected at the current gamepad index.
    /// </summary>
    bool IsGamepadConnected { get; }

    /// <summary>
    /// The gamepad index to use for direct gamepad queries (default 0).
    /// </summary>
    int GamepadIndex { get; set; }

    /// <summary>
     /// Check if a gamepad button is currently held down.
     /// Button codes are <see cref="GamepadButton"/> values cast to int.
     /// </summary>
    bool IsGamepadButtonDown(int button);

    /// <summary>
     /// Check if a gamepad button was pressed this frame.
     /// Button codes are <see cref="GamepadButton"/> values cast to int.
     /// </summary>
    bool IsGamepadButtonPressed(int button);

    /// <summary>
     /// Check if a gamepad button was released this frame.
     /// Button codes are <see cref="GamepadButton"/> values cast to int.
     /// </summary>
    bool IsGamepadButtonReleased(int button);

    /// <summary>
     /// Get the current axis value (-1.0 to 1.0 for sticks, 0.0 to 1.0 for triggers).
     /// Axis codes are <see cref="GamepadAxis"/> values cast to int.
     /// </summary>
    float GetGamepadAxis(int axis);

    #endregion

    #region Input Buffering

    /// <summary>
    /// Enable input buffering with specified history capacity.
    /// </summary>
    void EnableBuffering(int historyFrames = InputHistoryBuffer.DefaultCapacity);

    /// <summary>
    /// Register an action for discrete event buffering.
    /// </summary>
    void RegisterDiscreteAction(StringHandle actionName);

    /// <summary>
    /// Capture current action states to the history buffer.
    /// </summary>
    void CaptureSnapshot(ulong frameNumber);

    /// <summary>
    /// Input history buffer (null if buffering not enabled).
    /// </summary>
    InputHistoryBuffer? HistoryBuffer { get; }

    /// <summary>
    /// Discrete action buffer (null if buffering not enabled).
    /// </summary>
    DiscreteActionBuffer? DiscreteBuffer { get; }

    /// <summary>
    /// Current frame number (set by CaptureSnapshot).
    /// </summary>
    ulong FrameNumber { get; }

    /// <summary>
    /// Check if an action was started within the last N frames.
    /// </summary>
    bool WasActionStartedWithinFrames(StringHandle actionName, int withinFrames = 1);

    /// <summary>
    /// Check if an action was canceled within the last N frames.
    /// </summary>
    bool WasActionCanceledWithinFrames(StringHandle actionName, int withinFrames = 1);

    /// <summary>
    /// Get the most recent snapshot for an action.
    /// </summary>
    bool TryGetLatestSnapshot(StringHandle actionName, out ActionSnapshot snapshot);

    #endregion

    #region Callbacks

    /// <summary>
    /// Set a global callback for all action events.
    /// </summary>
    void SetGlobalCallback(ActionCallbackHandler? callback);

    #endregion
}
