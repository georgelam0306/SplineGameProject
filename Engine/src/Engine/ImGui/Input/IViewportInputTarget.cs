using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Input;

/// <summary>
/// Interface for viewports that can receive input from ViewportInputRouter.
/// Decouples input system from full Viewport implementation.
/// </summary>
public interface IViewportInputTarget
{
    /// <summary>
    /// Unique identifier for this viewport.
    /// </summary>
    int Id { get; }

    /// <summary>
    /// Screen-space bounds of this viewport (position and size in screen coordinates).
    /// </summary>
    ImRect ScreenBounds { get; }

    /// <summary>
    /// The input state to be filled by the router each frame.
    /// </summary>
    ref ImInput Input { get; }

    /// <summary>
    /// Whether this viewport currently has keyboard focus.
    /// Set by the router.
    /// </summary>
    bool HasKeyboardFocus { get; set; }
}
