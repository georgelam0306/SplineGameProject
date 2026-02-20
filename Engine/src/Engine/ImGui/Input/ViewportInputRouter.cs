using System.Numerics;
using DerpLib.ImGui.Core;
using Silk.NET.Input;

namespace DerpLib.ImGui.Input;

/// <summary>
/// Routes input from global (screen-space) coordinates to viewport-local coordinates.
/// Handles viewport focus, double-click detection, and keyboard routing.
/// </summary>
public sealed class ViewportInputRouter
{
    private readonly IGlobalInput _globalInput;
    private readonly List<IViewportInputTarget> _viewports = new();

    // Focus tracking
    private IViewportInputTarget? _hoveredViewport;
    private IViewportInputTarget? _focusedViewport;

    // Double-click detection
    private const float DoubleClickTime = 0.3f;
    private const float DoubleClickDistance = 5f;
    private float _lastClickTime;
    private Vector2 _lastClickPos;

    // Timing
    private float _time;

    // Keyboard state (from focused window)
    private IKeyboard? _keyboard;

    /// <summary>
    /// The viewport currently under the mouse cursor.
    /// Null if mouse is outside all viewports.
    /// </summary>
    public IViewportInputTarget? HoveredViewport => _hoveredViewport;

    /// <summary>
    /// The viewport that has keyboard focus.
    /// </summary>
    public IViewportInputTarget? FocusedViewport => _focusedViewport;

    /// <summary>
    /// Global mouse position in screen coordinates.
    /// Use for cross-viewport operations (window extraction, etc).
    /// </summary>
    public Vector2 GlobalMousePosition => _globalInput.GlobalMousePosition;

    public ViewportInputRouter(IGlobalInput globalInput)
    {
        _globalInput = globalInput;
    }

    /// <summary>
    /// Register a viewport to receive input.
    /// </summary>
    public void RegisterViewport(IViewportInputTarget viewport)
    {
        if (!_viewports.Contains(viewport))
        {
            _viewports.Add(viewport);

            // First viewport gets focus by default
            _focusedViewport ??= viewport;
        }
    }

    /// <summary>
    /// Unregister a viewport.
    /// </summary>
    public void UnregisterViewport(IViewportInputTarget viewport)
    {
        _viewports.Remove(viewport);

        if (_focusedViewport == viewport)
            _focusedViewport = _viewports.Count > 0 ? _viewports[0] : null;

        if (_hoveredViewport == viewport)
            _hoveredViewport = null;
    }

    /// <summary>
    /// Set the keyboard source for the focused viewport.
    /// Call this when the focused window's keyboard is available.
    /// </summary>
    public void SetKeyboard(IKeyboard? keyboard)
    {
        _keyboard = keyboard;
    }

    /// <summary>
    /// Update all viewport input states. Call once per frame.
    /// </summary>
    public void Update(float deltaTime)
    {
        _time += deltaTime;

        // Update global input (reads from SDL)
        _globalInput.Update();

        var globalMouse = _globalInput.GlobalMousePosition;

        // Find which viewport the mouse is over
        _hoveredViewport = FindViewportAt(globalMouse);

        // Focus changes on left click
        if (_globalInput.IsMouseButtonPressed(MouseButton.Left) && _hoveredViewport != null)
        {
            SetFocus(_hoveredViewport);
        }

        // Detect double-click
        bool isDoubleClick = false;
        if (_globalInput.IsMouseButtonPressed(MouseButton.Left))
        {
            float timeSinceLastClick = _time - _lastClickTime;
            float distFromLastClick = Vector2.Distance(globalMouse, _lastClickPos);

            isDoubleClick = timeSinceLastClick < DoubleClickTime
                         && distFromLastClick < DoubleClickDistance;

            _lastClickTime = _time;
            _lastClickPos = globalMouse;
        }

        // Update each viewport's input
        foreach (var viewport in _viewports)
        {
            BuildInputForViewport(viewport, globalMouse, deltaTime, isDoubleClick);
        }
    }

    /// <summary>
    /// Explicitly set focus to a viewport.
    /// </summary>
    public void SetFocus(IViewportInputTarget viewport)
    {
        if (_focusedViewport != viewport)
        {
            if (_focusedViewport != null)
                _focusedViewport.HasKeyboardFocus = false;

            _focusedViewport = viewport;
            viewport.HasKeyboardFocus = true;
        }
    }

    private IViewportInputTarget? FindViewportAt(Vector2 screenPos)
    {
        // Check in reverse order (last added = highest z-order)
        for (int i = _viewports.Count - 1; i >= 0; i--)
        {
            var viewport = _viewports[i];
            if (viewport.ScreenBounds.Contains(screenPos))
                return viewport;
        }
        return null;
    }

    private void BuildInputForViewport(
        IViewportInputTarget viewport,
        Vector2 globalMouse,
        float deltaTime,
        bool isDoubleClick)
    {
        ref var input = ref viewport.Input;

        // Clear previous frame
        input.Clear();

        // Convert global → local coordinates
        var localMouse = globalMouse - viewport.ScreenBounds.Position;

        // Mouse state
        input.MousePos = localMouse;
        input.MouseDelta = _globalInput.MouseDelta;
        input.ScrollDelta = _globalInput.ScrollDelta;
        input.IsDoubleClick = isDoubleClick;

        // Left button
        input.MousePressed = _globalInput.IsMouseButtonPressed(MouseButton.Left);
        input.MouseDown = _globalInput.IsMouseButtonDown(MouseButton.Left);
        input.MouseReleased = _globalInput.IsMouseButtonReleased(MouseButton.Left);

        // Right button
        input.MouseRightPressed = _globalInput.IsMouseButtonPressed(MouseButton.Right);
        input.MouseRightDown = _globalInput.IsMouseButtonDown(MouseButton.Right);
        input.MouseRightReleased = _globalInput.IsMouseButtonReleased(MouseButton.Right);

        // Middle button
        input.MouseMiddlePressed = _globalInput.IsMouseButtonPressed(MouseButton.Middle);
        input.MouseMiddleDown = _globalInput.IsMouseButtonDown(MouseButton.Middle);
        input.MouseMiddleReleased = _globalInput.IsMouseButtonReleased(MouseButton.Middle);

        // Timing
        input.DeltaTime = deltaTime;
        input.Time = _time;

        // Keyboard only for focused viewport
        if (viewport == _focusedViewport && _keyboard != null)
        {
            PopulateKeyboardInput(ref input);
        }
    }

    private void PopulateKeyboardInput(ref ImInput input)
    {
        if (_keyboard == null) return;

        // Navigation/editing keys
        input.KeyBackspace = _keyboard.IsKeyPressed(Key.Backspace);
        input.KeyDelete = _keyboard.IsKeyPressed(Key.Delete);
        input.KeyEnter = _keyboard.IsKeyPressed(Key.Enter);
        input.KeyEscape = _keyboard.IsKeyPressed(Key.Escape);
        input.KeyTab = _keyboard.IsKeyPressed(Key.Tab);
        input.KeyBackspaceDown = _keyboard.IsKeyPressed(Key.Backspace);
        input.KeyDeleteDown = _keyboard.IsKeyPressed(Key.Delete);

        // Arrow keys
        input.KeyLeft = _keyboard.IsKeyPressed(Key.Left);
        input.KeyRight = _keyboard.IsKeyPressed(Key.Right);
        input.KeyUp = _keyboard.IsKeyPressed(Key.Up);
        input.KeyDown = _keyboard.IsKeyPressed(Key.Down);

        // Text navigation
        input.KeyHome = _keyboard.IsKeyPressed(Key.Home);
        input.KeyEnd = _keyboard.IsKeyPressed(Key.End);
        input.KeyPageUp = _keyboard.IsKeyPressed(Key.PageUp);
        input.KeyPageDown = _keyboard.IsKeyPressed(Key.PageDown);

        // Modifiers (held, not pressed)
        input.KeyCtrl =
            _keyboard.IsKeyPressed(Key.ControlLeft) || _keyboard.IsKeyPressed(Key.ControlRight) ||
            _keyboard.IsKeyPressed(Key.SuperLeft) || _keyboard.IsKeyPressed(Key.SuperRight);
        input.KeyShift = _keyboard.IsKeyPressed(Key.ShiftLeft) || _keyboard.IsKeyPressed(Key.ShiftRight);
        input.KeyAlt = _keyboard.IsKeyPressed(Key.AltLeft) || _keyboard.IsKeyPressed(Key.AltRight);

        // Shortcuts (Ctrl + key)
        if (input.KeyCtrl)
        {
            input.KeyCtrlA = _keyboard.IsKeyPressed(Key.A);
            input.KeyCtrlC = _keyboard.IsKeyPressed(Key.C);
            input.KeyCtrlD = _keyboard.IsKeyPressed(Key.D);
            input.KeyCtrlV = _keyboard.IsKeyPressed(Key.V);
            input.KeyCtrlX = _keyboard.IsKeyPressed(Key.X);
            input.KeyCtrlZ = _keyboard.IsKeyPressed(Key.Z);
            input.KeyCtrlY = _keyboard.IsKeyPressed(Key.Y);
            input.KeyCtrlB = _keyboard.IsKeyPressed(Key.B);
            input.KeyCtrlI = _keyboard.IsKeyPressed(Key.I);
            input.KeyCtrlU = _keyboard.IsKeyPressed(Key.U);
        }
    }

    /// <summary>
    /// Add a text input character. Call from window's character callback.
    /// Routes to focused viewport.
    /// </summary>
    public void AddInputChar(char c)
    {
        if (_focusedViewport != null)
        {
            _focusedViewport.Input.AddInputChar(c);
        }
    }
}
