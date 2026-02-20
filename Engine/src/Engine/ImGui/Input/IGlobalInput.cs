using System.Numerics;

namespace DerpLib.ImGui.Input;

/// <summary>
/// Mouse button identifiers.
/// </summary>
public enum MouseButton
{
    Left = 0,
    Middle = 1,
    Right = 2
}

/// <summary>
/// Interface for platform-level global input.
/// Provides mouse position in screen coordinates (not window-local).
/// </summary>
public interface IGlobalInput
{
    /// <summary>
    /// Mouse position in screen coordinates (pixels from top-left of primary monitor).
    /// Available regardless of which window has focus.
    /// </summary>
    Vector2 GlobalMousePosition { get; }

    /// <summary>
    /// Mouse movement since last Update() call.
    /// </summary>
    Vector2 MouseDelta { get; }

    /// <summary>
    /// Scroll wheel delta since last Update() call. Positive = scroll up.
    /// </summary>
    float ScrollDelta { get; }

    /// <summary>
    /// Returns true if the button is currently held down.
    /// </summary>
    bool IsMouseButtonDown(MouseButton button);

    /// <summary>
    /// Returns true only on the frame the button was pressed (transition from up to down).
    /// </summary>
    bool IsMouseButtonPressed(MouseButton button);

    /// <summary>
    /// Returns true only on the frame the button was released (transition from down to up).
    /// </summary>
    bool IsMouseButtonReleased(MouseButton button);

    /// <summary>
    /// Update input state. Call once per frame before querying.
    /// </summary>
    void Update();

    /// <summary>
    /// Add scroll delta from event. Called by event handler.
    /// </summary>
    void AddScrollDelta(float delta);

    /// <summary>
    /// Reset scroll accumulator. Called after Update() processes it.
    /// </summary>
    void ResetScrollDelta();
}
