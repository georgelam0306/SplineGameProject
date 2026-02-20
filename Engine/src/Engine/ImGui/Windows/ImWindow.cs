using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Rendering;

namespace DerpLib.ImGui.Windows;

/// <summary>
/// Flags controlling window behavior.
/// </summary>
[Flags]
public enum ImWindowFlags
{
    None = 0,
    NoTitleBar = 1 << 0,
    NoResize = 1 << 1,
    NoMove = 1 << 2,
    NoClose = 1 << 3,
    NoCollapse = 1 << 4,
    NoScrollbar = 1 << 5,
    NoBackground = 1 << 6,
    AlwaysOnTop = 1 << 7,
}

/// <summary>
/// Represents a single window in the ImGUI system.
/// Windows can be dragged, resized, and contain widgets.
/// </summary>
public class ImWindow
{
    /// <summary>Unique ID for this window (hashed from title).</summary>
    public int Id;

    /// <summary>Window title (displayed in title bar).</summary>
    public string Title;

    /// <summary>Window bounds in logical coordinates.</summary>
    public ImRect Rect;

    /// <summary>Behavior flags.</summary>
    public ImWindowFlags Flags;

    /// <summary>Is the window currently visible/open?</summary>
    public bool IsOpen;

    /// <summary>Is the window collapsed (only title bar visible)?</summary>
    public bool IsCollapsed;

    /// <summary>Is the window currently being dragged by title bar?</summary>
    public bool IsDragging;

    /// <summary>Is the window currently being resized?</summary>
    public bool IsResizing;

    /// <summary>Which edge/corner is being resized (0=none, 1-8 = edges/corners).</summary>
    public int ResizeEdge;

    /// <summary>Z-order (higher = on top). Managed by ImWindowManager.</summary>
    public int ZOrder;

    /// <summary>Whether this window is currently docked.</summary>
    public bool IsDocked;

    /// <summary>ID of the dock leaf containing this window (-1 = not docked).</summary>
    public int DockNodeId = -1;

    /// <summary>Minimum window size.</summary>
    public Vector2 MinSize;

    /// <summary>Maximum window size (0 = no limit).</summary>
    public Vector2 MaxSize;

    /// <summary>Content scroll offset.</summary>
    public Vector2 ScrollOffset;

    /// <summary>Content size (for scrolling).</summary>
    public Vector2 ContentSize;

    /// <summary>Maximum scroll offset based on ContentSize and visible content rect.</summary>
    public Vector2 MaxScrollOffset;

    /// <summary>True if the window currently has a vertical scrollbar.</summary>
    public bool HasVerticalScroll;

    /// <summary>True if the window currently has a horizontal scrollbar.</summary>
    public bool HasHorizontalScroll;

    internal float VerticalScrollbarGrabOffset;
    internal float HorizontalScrollbarGrabOffset;

    /// <summary>Drag offset from title bar click position.</summary>
    internal Vector2 DragOffset;

    /// <summary>Original rect before resize started.</summary>
    internal ImRect ResizeStartRect;

    /// <summary>Mouse position when resize started.</summary>
    internal Vector2 ResizeStartMouse;

    public ImWindow(string title, float x, float y, float width, float height, ImWindowFlags flags = ImWindowFlags.None, int? explicitId = null)
    {
        Id = explicitId ?? HashTitle(title);
        Title = title;
        Rect = new ImRect(x, y, width, height);
        Flags = flags;
        IsOpen = true;
        IsCollapsed = false;
        MinSize = new Vector2(100, 50);
        MaxSize = Vector2.Zero;
    }

    /// <summary>
    /// Get the title bar bounds.
    /// </summary>
    public ImRect GetTitleBarRect(float titleBarHeight)
    {
        return new ImRect(Rect.X, Rect.Y, Rect.Width, titleBarHeight);
    }

    /// <summary>
    /// Get the title bar bounds for an arbitrary rect (e.g. viewport-local rendering rect).
    /// </summary>
    public static ImRect GetTitleBarRect(ImRect rect, float titleBarHeight)
    {
        return new ImRect(rect.X, rect.Y, rect.Width, titleBarHeight);
    }

    /// <summary>
    /// Get the content area bounds (below title bar, minus scrollbar if any).
    /// </summary>
    public ImRect GetContentRect(float titleBarHeight, float scrollbarWidth)
    {
        if (IsCollapsed)
            return ImRect.Zero;

        float y = Rect.Y + titleBarHeight;
        float height = Rect.Height - titleBarHeight;

        bool hasScrollbar = ContentSize.Y > height && !Flags.HasFlag(ImWindowFlags.NoScrollbar);
        float width = hasScrollbar ? Rect.Width - scrollbarWidth : Rect.Width;

        return new ImRect(Rect.X, y, width, height);
    }

    /// <summary>
    /// Get the content area bounds for an arbitrary rect (e.g. viewport-local rendering rect).
    /// </summary>
    public ImRect GetContentRect(ImRect rect, float titleBarHeight, float scrollbarWidth)
    {
        if (IsCollapsed)
        {
            return ImRect.Zero;
        }

        float y = rect.Y + titleBarHeight;
        float height = rect.Height - titleBarHeight;

        bool hasScrollbar = ContentSize.Y > height && !Flags.HasFlag(ImWindowFlags.NoScrollbar);
        float width = hasScrollbar ? rect.Width - scrollbarWidth : rect.Width;

        return new ImRect(rect.X, y, width, height);
    }

    /// <summary>
    /// Check if a point is over a resize edge/corner.
    /// Returns 0 for no resize, 1-4 for edges, 5-8 for corners.
    /// </summary>
    public int HitTestResize(Vector2 point, float grabSize)
    {
        if (Flags.HasFlag(ImWindowFlags.NoResize))
            return 0;

        float x = Rect.X;
        float y = Rect.Y;
        float w = Rect.Width;
        float h = Rect.Height;

        // Check if point is near each edge
        bool nearLeft = point.X >= x - grabSize && point.X < x + grabSize;
        bool nearRight = point.X >= x + w - grabSize && point.X < x + w + grabSize;
        bool nearTop = point.Y >= y - grabSize && point.Y < y + grabSize;
        bool nearBottom = point.Y >= y + h - grabSize && point.Y < y + h + grabSize;

        // Check if point is within the window's spans (with grab extension for corners)
        bool inVerticalSpan = point.Y >= y - grabSize && point.Y < y + h + grabSize;
        bool inHorizontalSpan = point.X >= x - grabSize && point.X < x + w + grabSize;

        // Corners (higher priority) - must be near both edges
        if (nearLeft && nearTop) return 5;      // NW
        if (nearRight && nearTop) return 6;     // NE
        if (nearLeft && nearBottom) return 7;   // SW
        if (nearRight && nearBottom) return 8;  // SE

        // Edges - must be near the edge AND within the perpendicular span
        if (nearLeft && inVerticalSpan) return 1;   // W
        if (nearRight && inVerticalSpan) return 2;  // E
        if (nearTop && inHorizontalSpan) return 3;  // N
        if (nearBottom && inHorizontalSpan) return 4; // S

        return 0;
    }

    /// <summary>
    /// Apply resize delta based on which edge is being dragged.
    /// </summary>
    public void ApplyResize(int edge, Vector2 mouseDelta)
    {
        float minW = MinSize.X;
        float minH = MinSize.Y;
        float maxW = MaxSize.X > 0 ? MaxSize.X : float.MaxValue;
        float maxH = MaxSize.Y > 0 ? MaxSize.Y : float.MaxValue;

        switch (edge)
        {
            case 1: // W - left edge
                float newW1 = Math.Clamp(Rect.Width - mouseDelta.X, minW, maxW);
                float deltaW1 = Rect.Width - newW1;
                Rect = new ImRect(Rect.X + deltaW1, Rect.Y, newW1, Rect.Height);
                break;

            case 2: // E - right edge
                Rect = new ImRect(Rect.X, Rect.Y,
                    Math.Clamp(Rect.Width + mouseDelta.X, minW, maxW), Rect.Height);
                break;

            case 3: // N - top edge
                float newH3 = Math.Clamp(Rect.Height - mouseDelta.Y, minH, maxH);
                float deltaH3 = Rect.Height - newH3;
                Rect = new ImRect(Rect.X, Rect.Y + deltaH3, Rect.Width, newH3);
                break;

            case 4: // S - bottom edge
                Rect = new ImRect(Rect.X, Rect.Y, Rect.Width,
                    Math.Clamp(Rect.Height + mouseDelta.Y, minH, maxH));
                break;

            case 5: // NW corner
                ApplyResize(1, mouseDelta);
                ApplyResize(3, mouseDelta);
                break;

            case 6: // NE corner
                ApplyResize(2, mouseDelta);
                ApplyResize(3, mouseDelta);
                break;

            case 7: // SW corner
                ApplyResize(1, mouseDelta);
                ApplyResize(4, mouseDelta);
                break;

            case 8: // SE corner
                ApplyResize(2, mouseDelta);
                ApplyResize(4, mouseDelta);
                break;
        }
    }

    private static int HashTitle(string title)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = (int)2166136261;
            foreach (char c in title)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return hash;
        }
    }

    /// <summary>
    /// Get the ID that would be assigned to a window with the given title.
    /// Useful for pre-registering windows with the dock controller.
    /// </summary>
    public static int GetIdForTitle(string title) => HashTitle(title);
}
