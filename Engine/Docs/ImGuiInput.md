# ImGUI Input System Architecture

## Overview

This document describes the input handling architecture for the multi-viewport ImGUI system. The design uses **viewport-local coordinates** for widgets while leveraging **SDL global mouse state** for cross-viewport operations.

---

## Design Principles

1. **Widgets use local coordinates** - No widget code needs to know about viewport positions
2. **One conversion point** - Global→local conversion happens in `ViewportInputRouter` only
3. **Passive input** - ImGUI doesn't fetch input; the router provides it each frame
4. **SDL for global state** - Platform-independent global mouse position and button state

---

## Architecture Layers

```
┌─────────────────────────────────────────────────────────────────┐
│                         SDL (Global Input)                       │
│  - SDL_GetGlobalMouseState() → screen coordinates               │
│  - SDL_GetMouseState() → button states                          │
│  - Works across all native windows                              │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ViewportInputRouter                           │
│  - Queries SDL each frame                                       │
│  - Determines HoveredViewport and FocusedViewport               │
│  - Converts global→local for each viewport                      │
│  - Routes keyboard to focused viewport only                     │
└──────────┬──────────────────┬──────────────────┬────────────────┘
           │                  │                  │
           ▼                  ▼                  ▼
    ┌────────────┐     ┌────────────┐     ┌────────────┐
    │ ImContext  │     │ ImContext  │     │ ImContext  │
    │ Viewport 0 │     │ Viewport 1 │     │ Viewport N │
    ├────────────┤     ├────────────┤     ├────────────┤
    │ ImInput    │     │ ImInput    │     │ ImInput    │
    │ (local)    │     │ (local)    │     │ (local)    │
    └────────────┘     └────────────┘     └────────────┘
           │                  │                  │
           ▼                  ▼                  ▼
    ┌─────────────────────────────────────────────────┐
    │              Widgets (Button, Slider, etc.)     │
    │  - Use ImContext.Input.MousePos (local coords)  │
    │  - No knowledge of viewports or screen space    │
    └─────────────────────────────────────────────────┘
```

---

## Core Types

### IGlobalInput (Interface)

Low-level abstraction over SDL:

```csharp
public interface IGlobalInput
{
    /// <summary>
    /// Mouse position in screen coordinates (pixels from top-left of primary monitor).
    /// </summary>
    Vector2 GlobalMousePosition { get; }

    /// <summary>
    /// Mouse movement since last frame.
    /// </summary>
    Vector2 MouseDelta { get; }

    /// <summary>
    /// Scroll wheel delta this frame.
    /// </summary>
    float ScrollDelta { get; }

    /// <summary>
    /// Returns true if button is currently held down.
    /// </summary>
    bool IsMouseButtonDown(MouseButton button);

    /// <summary>
    /// Returns true only on the frame the button was pressed.
    /// </summary>
    bool IsMouseButtonPressed(MouseButton button);

    /// <summary>
    /// Returns true only on the frame the button was released.
    /// </summary>
    bool IsMouseButtonReleased(MouseButton button);

    /// <summary>
    /// Update input state. Call once per frame before processing.
    /// </summary>
    void Update();
}

public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}
```

### SdlGlobalInput (Implementation)

```csharp
public class SdlGlobalInput : IGlobalInput
{
    private Vector2 _currentPos;
    private Vector2 _lastPos;
    private uint _currentButtons;
    private uint _lastButtons;
    private float _scrollDelta;

    public Vector2 GlobalMousePosition => _currentPos;
    public Vector2 MouseDelta => _currentPos - _lastPos;
    public float ScrollDelta => _scrollDelta;

    public bool IsMouseButtonDown(MouseButton button)
    {
        uint mask = 1u << (int)button;
        return (_currentButtons & mask) != 0;
    }

    public bool IsMouseButtonPressed(MouseButton button)
    {
        uint mask = 1u << (int)button;
        return (_currentButtons & mask) != 0 && (_lastButtons & mask) == 0;
    }

    public bool IsMouseButtonReleased(MouseButton button)
    {
        uint mask = 1u << (int)button;
        return (_currentButtons & mask) == 0 && (_lastButtons & mask) != 0;
    }

    public void Update()
    {
        _lastPos = _currentPos;
        _lastButtons = _currentButtons;

        // SDL global mouse state
        _currentButtons = SDL_GetGlobalMouseState(out int x, out int y);
        _currentPos = new Vector2(x, y);

        // Scroll comes from event queue (handled separately)
        // _scrollDelta set by event handler
    }
}
```

---

### ImInput (Per-Viewport Input State)

What each `ImContext` receives - all in **viewport-local coordinates**:

```csharp
public struct ImInput
{
    //=== Mouse (viewport-local coordinates) ===

    /// <summary>
    /// Mouse position relative to viewport top-left.
    /// (0,0) = top-left corner of this viewport.
    /// </summary>
    public Vector2 MousePos;

    /// <summary>
    /// Mouse movement since last frame (same in local and global).
    /// </summary>
    public Vector2 MouseDelta;

    /// <summary>
    /// True only on the frame left mouse button was pressed.
    /// </summary>
    public bool MousePressed;

    /// <summary>
    /// True while left mouse button is held down.
    /// </summary>
    public bool MouseDown;

    /// <summary>
    /// True only on the frame left mouse button was released.
    /// </summary>
    public bool MouseReleased;

    /// <summary>
    /// Right mouse button states.
    /// </summary>
    public bool MouseRightPressed, MouseRightDown, MouseRightReleased;

    /// <summary>
    /// Middle mouse button states.
    /// </summary>
    public bool MouseMiddlePressed, MouseMiddleDown, MouseMiddleReleased;

    /// <summary>
    /// Scroll wheel delta this frame. Positive = scroll up.
    /// </summary>
    public float ScrollDelta;

    //=== Keyboard (only valid for focused viewport) ===

    /// <summary>
    /// Navigation and editing keys.
    /// </summary>
    public bool KeyBackspace, KeyDelete, KeyEnter, KeyEscape, KeyTab;
    public bool KeyLeft, KeyRight, KeyUp, KeyDown;
    public bool KeyHome, KeyEnd, KeyPageUp, KeyPageDown;

    /// <summary>
    /// Modifier keys.
    /// </summary>
    public bool KeyCtrl, KeyShift, KeyAlt;

    /// <summary>
    /// Common shortcuts.
    /// </summary>
    public bool KeyCtrlA, KeyCtrlC, KeyCtrlV, KeyCtrlX, KeyCtrlZ, KeyCtrlY;

    /// <summary>
    /// Text input characters this frame (for text fields).
    /// Fixed-size buffer to avoid allocation.
    /// </summary>
    public unsafe fixed char InputChars[16];
    public int InputCharCount;

    //=== Timing ===

    /// <summary>
    /// Time since last frame in seconds.
    /// </summary>
    public float DeltaTime;

    /// <summary>
    /// Total elapsed time in seconds.
    /// </summary>
    public float Time;

    //=== Helpers ===

    /// <summary>
    /// Returns true if this is a double-click (two clicks within threshold).
    /// </summary>
    public bool IsDoubleClick;
}
```

---

### ViewportInputRouter

Central coordinator that fills `ImInput` for each viewport:

```csharp
public class ViewportInputRouter
{
    private readonly IGlobalInput _globalInput;
    private readonly ViewportManager _viewportManager;

    // Double-click detection
    private const float DoubleClickTime = 0.3f;
    private const float DoubleClickDistance = 5f;
    private float _lastClickTime;
    private Vector2 _lastClickPos;

    // Timing
    private float _time;

    /// <summary>
    /// The viewport currently under the mouse cursor (null if outside all viewports).
    /// </summary>
    public Viewport? HoveredViewport { get; private set; }

    /// <summary>
    /// The viewport that has keyboard focus.
    /// </summary>
    public Viewport? FocusedViewport { get; private set; }

    /// <summary>
    /// Global mouse position in screen coordinates.
    /// Used for cross-viewport operations like window extraction.
    /// </summary>
    public Vector2 GlobalMousePosition => _globalInput.GlobalMousePosition;

    public ViewportInputRouter(IGlobalInput globalInput, ViewportManager viewportManager)
    {
        _globalInput = globalInput;
        _viewportManager = viewportManager;
    }

    /// <summary>
    /// Update all viewport input states. Call once per frame.
    /// </summary>
    public void Update(float deltaTime)
    {
        _time += deltaTime;
        _globalInput.Update();

        var globalMouse = _globalInput.GlobalMousePosition;

        // Find which viewport the mouse is over
        HoveredViewport = FindViewportAt(globalMouse);

        // Focus changes on click
        if (_globalInput.IsMouseButtonPressed(MouseButton.Left) && HoveredViewport != null)
        {
            FocusedViewport = HoveredViewport;
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
        foreach (var viewport in _viewportManager.AllViewports)
        {
            var input = BuildInputForViewport(viewport, globalMouse, deltaTime, isDoubleClick);
            viewport.ImContext.Input = input;
        }
    }

    private ImInput BuildInputForViewport(
        Viewport viewport,
        Vector2 globalMouse,
        float deltaTime,
        bool isDoubleClick)
    {
        // Convert global → local coordinates
        var localMouse = globalMouse - viewport.ScreenBounds.Position;

        var input = new ImInput
        {
            // Mouse (local coords)
            MousePos = localMouse,
            MouseDelta = _globalInput.MouseDelta,
            MousePressed = _globalInput.IsMouseButtonPressed(MouseButton.Left),
            MouseDown = _globalInput.IsMouseButtonDown(MouseButton.Left),
            MouseReleased = _globalInput.IsMouseButtonReleased(MouseButton.Left),
            MouseRightPressed = _globalInput.IsMouseButtonPressed(MouseButton.Right),
            MouseRightDown = _globalInput.IsMouseButtonDown(MouseButton.Right),
            MouseRightReleased = _globalInput.IsMouseButtonReleased(MouseButton.Right),
            MouseMiddlePressed = _globalInput.IsMouseButtonPressed(MouseButton.Middle),
            MouseMiddleDown = _globalInput.IsMouseButtonDown(MouseButton.Middle),
            MouseMiddleReleased = _globalInput.IsMouseButtonReleased(MouseButton.Middle),
            ScrollDelta = _globalInput.ScrollDelta,
            IsDoubleClick = isDoubleClick,

            // Timing
            DeltaTime = deltaTime,
            Time = _time,
        };

        // Keyboard only goes to focused viewport
        if (viewport == FocusedViewport)
        {
            PopulateKeyboardInput(ref input, viewport);
        }

        return input;
    }

    private void PopulateKeyboardInput(ref ImInput input, Viewport viewport)
    {
        // Get keyboard state from the focused window
        var kb = viewport.Window.KeyboardState;

        input.KeyBackspace = kb.IsKeyPressed(Key.Backspace);
        input.KeyDelete = kb.IsKeyPressed(Key.Delete);
        input.KeyEnter = kb.IsKeyPressed(Key.Enter);
        input.KeyEscape = kb.IsKeyPressed(Key.Escape);
        input.KeyTab = kb.IsKeyPressed(Key.Tab);

        input.KeyLeft = kb.IsKeyPressed(Key.Left);
        input.KeyRight = kb.IsKeyPressed(Key.Right);
        input.KeyUp = kb.IsKeyPressed(Key.Up);
        input.KeyDown = kb.IsKeyPressed(Key.Down);
        input.KeyHome = kb.IsKeyPressed(Key.Home);
        input.KeyEnd = kb.IsKeyPressed(Key.End);

        input.KeyCtrl = kb.IsKeyDown(Key.ControlLeft) || kb.IsKeyDown(Key.ControlRight);
        input.KeyShift = kb.IsKeyDown(Key.ShiftLeft) || kb.IsKeyDown(Key.ShiftRight);
        input.KeyAlt = kb.IsKeyDown(Key.AltLeft) || kb.IsKeyDown(Key.AltRight);

        // Shortcuts
        input.KeyCtrlA = input.KeyCtrl && kb.IsKeyPressed(Key.A);
        input.KeyCtrlC = input.KeyCtrl && kb.IsKeyPressed(Key.C);
        input.KeyCtrlV = input.KeyCtrl && kb.IsKeyPressed(Key.V);
        input.KeyCtrlX = input.KeyCtrl && kb.IsKeyPressed(Key.X);
        input.KeyCtrlZ = input.KeyCtrl && kb.IsKeyPressed(Key.Z);
        input.KeyCtrlY = input.KeyCtrl && kb.IsKeyPressed(Key.Y);

        // Text input (from window's character callback)
        // Populated separately via AddInputCharacter()
    }

    private Viewport? FindViewportAt(Vector2 screenPos)
    {
        // Check in reverse order (top-most first based on z-order)
        foreach (var viewport in _viewportManager.AllViewports.Reverse())
        {
            if (viewport.ScreenBounds.Contains(screenPos))
                return viewport;
        }
        return null;
    }
}
```

---

## Coordinate Spaces

### Screen Space (Global)
- Origin: Top-left of primary monitor
- Used by: `ViewportInputRouter`, window extraction, cross-viewport drag
- Source: `SDL_GetGlobalMouseState()`

### Viewport-Local Space
- Origin: Top-left of viewport's native window
- Used by: `ImInput.MousePos`, all widgets, layout system
- Conversion: `local = global - viewport.ScreenBounds.Position`

### Widget Space
- Same as viewport-local (widgets don't add another layer)
- Window content uses viewport-local with scroll offset

```
Screen Space                    Viewport-Local Space
┌────────────────────────┐      ┌─────────────────┐
│ (0,0)                  │      │ (0,0)           │
│    ┌───────────┐       │      │                 │
│    │ Viewport  │       │  →   │  Button at      │
│    │ at (100,  │       │      │  (50, 30)       │
│    │    50)    │       │      │                 │
│    │  ┌─────┐  │       │      │  ┌─────┐        │
│    │  │Btn  │  │       │      │  │Btn  │        │
│    │  └─────┘  │       │      │  └─────┘        │
│    └───────────┘       │      └─────────────────┘
└────────────────────────┘
     Button screen pos:         Button local pos:
     (150, 80)                  (50, 30)
```

---

## Input Flow

### Per-Frame Sequence

```
1. ViewportInputRouter.Update(deltaTime)
   │
   ├─► IGlobalInput.Update()
   │   └─► SDL_GetGlobalMouseState() → global mouse position
   │
   ├─► Find HoveredViewport (which viewport is mouse over?)
   │
   ├─► Update FocusedViewport (on click)
   │
   └─► For each viewport:
       └─► Build ImInput with local coordinates
           └─► viewport.ImContext.Input = input

2. For each viewport:
   │
   ├─► Im.Ctx = viewport.ImContext
   │
   ├─► Im.Begin(deltaTime, viewport.Size)
   │
   ├─► User code: ImButton.Draw("Test")
   │   └─► Uses Im.Input.MousePos (local coords)
   │
   └─► Im.End()

3. Render each viewport's SdfBuffer
```

### Widget Input Usage

Widgets only see local coordinates:

```csharp
public static bool Button(string label)
{
    int id = Im.GetId(label);
    var rect = Im.Layout.AllocateRect(100, 30);

    // MousePos is already in viewport-local coordinates
    bool hovered = rect.Contains(Im.Input.MousePos);
    bool clicked = false;

    if (hovered)
    {
        Im.Ctx.HotId = id;
        if (Im.Input.MousePressed && Im.Ctx.ActiveId == 0)
            Im.Ctx.ActiveId = id;
    }

    if (Im.Ctx.ActiveId == id && Im.Input.MouseReleased)
    {
        clicked = hovered;
        Im.Ctx.ActiveId = 0;
    }

    // Draw button...

    return clicked;
}
```

---

## Cross-Viewport Operations

For operations that span viewports (window dragging, extraction), use global coordinates from the router:

```csharp
public class WindowDragHandler
{
    private readonly ViewportInputRouter _inputRouter;

    public void UpdateDrag(ImWindow window, Viewport sourceViewport)
    {
        if (!window.IsDragging) return;

        // Use GLOBAL coordinates for cross-viewport detection
        var globalMouse = _inputRouter.GlobalMousePosition;
        var targetViewport = _inputRouter.HoveredViewport;

        if (targetViewport == null)
        {
            // Outside all viewports - extraction candidate
            ShowExtractionPreview(window, globalMouse);
        }
        else if (targetViewport != sourceViewport)
        {
            // Over a different viewport - dock preview
            ShowDockPreview(window, targetViewport);
        }
        else
        {
            // Still in source viewport - normal drag
            // (use local coords from ImInput)
            window.Position += Im.Input.MouseDelta;
        }
    }
}
```

---

## Input Consumption

Widgets can signal that they're consuming input:

```csharp
public class ImContext
{
    /// <summary>
    /// Set to true when ImGUI wants to capture mouse input.
    /// Game code should check this before processing mouse input.
    /// </summary>
    public bool WantCaptureMouse;

    /// <summary>
    /// Set to true when ImGUI wants to capture keyboard input.
    /// Game code should check this before processing keyboard input.
    /// </summary>
    public bool WantCaptureKeyboard;
}
```

Set by widgets:

```csharp
// In Button
if (hovered)
    Im.Ctx.WantCaptureMouse = true;

// In TextInput (when focused)
if (Im.Ctx.FocusId == id)
    Im.Ctx.WantCaptureKeyboard = true;
```

Game code checks:

```csharp
// In game update loop
if (!Im.Ctx.WantCaptureMouse)
{
    ProcessGameMouseInput();
}

if (!Im.Ctx.WantCaptureKeyboard)
{
    ProcessGameKeyboardInput();
}
```

---

## SDL Integration

### Required SDL Functions

```csharp
// From Silk.NET.SDL

// Get mouse position in screen coordinates
uint SDL_GetGlobalMouseState(out int x, out int y);

// Get mouse button state (returns button mask)
uint SDL_GetMouseState(out int x, out int y);

// Button masks
const uint SDL_BUTTON_LMASK = 1 << 0;
const uint SDL_BUTTON_MMASK = 1 << 1;
const uint SDL_BUTTON_RMASK = 1 << 2;
```

### Scroll Wheel

Scroll wheel comes from SDL events, not polling:

```csharp
public class SdlGlobalInput : IGlobalInput
{
    private float _scrollDelta;

    // Called from SDL event loop
    public void HandleEvent(SDL_Event e)
    {
        if (e.type == SDL_EventType.SDL_MOUSEWHEEL)
        {
            _scrollDelta += e.wheel.y;
        }
    }

    public void Update()
    {
        // ... other updates ...

        // Reset scroll for next frame (it's accumulated from events)
        _scrollDelta = 0;
    }
}
```

---

## File Organization

```
Engine/src/Engine/ImGui/
├── Input/
│   ├── IGlobalInput.cs           # Interface for global input
│   ├── SdlGlobalInput.cs         # SDL implementation
│   ├── ImInput.cs                # Per-viewport input state
│   └── ViewportInputRouter.cs    # Coordinate conversion & routing
```

---

## Summary

| Concept | Description |
|---------|-------------|
| **Global Input** | SDL provides screen-space mouse position and button states |
| **ViewportInputRouter** | Converts global→local, determines hovered/focused viewport |
| **ImInput** | Per-viewport input struct with local coordinates |
| **Widgets** | Use `Im.Input.MousePos` (local), unaware of viewports |
| **Cross-Viewport** | Use `ViewportInputRouter.GlobalMousePosition` for drag/extract |
| **Input Consumption** | `WantCaptureMouse`/`WantCaptureKeyboard` flags for game input |
