using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Rendering;
using DerpLib.Sdf;

namespace DerpLib.ImGui.Viewport;

/// <summary>
/// Represents a rendering target for ImGUI (a native OS window).
/// Each viewport has its own SdfBuffer and input state.
/// </summary>
public class ImViewport
{
    private static int _nextId;

    /// <summary>Unique viewport ID.</summary>
    public int Id { get; }

    /// <summary>
    /// The ViewportResources ID this viewport is associated with.
    /// -1 for primary viewport, otherwise the ViewportResources.Id for secondary viewports.
    /// Used to match windows to their viewport.
    /// </summary>
    public int ResourcesId { get; set; } = -1;

    /// <summary>Viewport name (for debugging).</summary>
    public string Name { get; set; }

    /// <summary>SDF buffer for rendering to this viewport.</summary>
    public SdfBuffer Buffer { get; set; }

    //=== Draw Lists (one per layer) ===

    private const int LayerCount = 4; // Must match ImDrawLayer enum count
    private readonly ImDrawList[] _drawLists = new ImDrawList[LayerCount];

    /// <summary>Current draw layer for new draw commands.</summary>
    public ImDrawLayer CurrentLayer { get; private set; } = ImDrawLayer.WindowContent;

    /// <summary>Get the draw list for a specific layer.</summary>
    public ImDrawList GetDrawList(ImDrawLayer layer) => _drawLists[(int)layer];

    /// <summary>Get the current draw list (for the current layer).</summary>
    public ImDrawList CurrentDrawList => _drawLists[(int)CurrentLayer];

    /// <summary>Set the current draw layer for subsequent draw commands.</summary>
    public void SetDrawLayer(ImDrawLayer layer) => CurrentLayer = layer;

    /// <summary>Clear all draw lists and reset to default layer.</summary>
    public void ClearDrawLists()
    {
        for (int i = 0; i < LayerCount; i++)
            _drawLists[i].Clear();
        CurrentLayer = ImDrawLayer.WindowContent;
    }

    /// <summary>Flush all draw lists to this viewport's SdfBuffer in layer order.</summary>
    public void FlushDrawLists()
    {
        for (int i = 0; i < LayerCount; i++)
            _drawLists[i].Flush(Buffer);
    }

    /// <summary>Input state for this viewport (mouse pos is viewport-local).</summary>
    public ImInput Input;

    /// <summary>Viewport size in logical coordinates.</summary>
    public Vector2 Size { get; set; }

    /// <summary>Screen position of this viewport (for multi-viewport hit testing).</summary>
    public Vector2 ScreenPosition { get; set; }

    /// <summary>Content scale (DPI factor, e.g., 2.0 on Retina).</summary>
    public float ContentScale { get; set; } = 1f;

    /// <summary>Is this the primary/main viewport?</summary>
    public bool IsPrimary { get; init; }

    public ImViewport(string name, bool isPrimary = false)
    {
        Id = _nextId++;
        Name = name;
        IsPrimary = isPrimary;
        Buffer = null!; // Set externally

        // Initialize draw lists
        for (int i = 0; i < LayerCount; i++)
            _drawLists[i] = new ImDrawList();
    }

    /// <summary>
    /// Viewport bounds in screen coordinates (for multi-viewport).
    /// </summary>
    public ImRect ScreenBounds => new(ScreenPosition.X, ScreenPosition.Y, Size.X, Size.Y);

    /// <summary>
    /// Check if a screen-space point is inside this viewport.
    /// </summary>
    public bool ContainsScreenPoint(Vector2 screenPoint)
    {
        return ScreenBounds.Contains(screenPoint);
    }

    /// <summary>
    /// Convert screen coordinates to viewport-local coordinates.
    /// </summary>
    public Vector2 ScreenToLocal(Vector2 screenPoint)
    {
        return screenPoint - ScreenPosition;
    }
}
