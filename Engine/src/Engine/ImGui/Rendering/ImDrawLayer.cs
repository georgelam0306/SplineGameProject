namespace DerpLib.ImGui.Rendering;

/// <summary>
/// Draw layers for z-ordering ImGUI elements.
/// Lower values render first (behind), higher values render on top.
/// </summary>
public enum ImDrawLayer
{
    /// <summary>Background elements, dock space backgrounds.</summary>
    Background = 0,

    /// <summary>Window content (widgets inside windows).</summary>
    WindowContent = 1,

    /// <summary>Floating window frames and chrome.</summary>
    FloatingWindows = 2,

    /// <summary>Popups, dropdowns, tooltips, overlays.</summary>
    Overlay = 3
}
