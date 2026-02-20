using System.Numerics;
using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Layout;

/// <summary>
/// Cursor-based layout stack (single-pass immediate layout).
/// All returned rects are in the current Im coordinate space (typically window-local while inside a window).
/// </summary>
public static class ImLayout
{
    private const int MaxDepth = 16;

    private static readonly LayoutFrame[] Frames = new LayoutFrame[MaxDepth];
    private static int _depth;

    private struct LayoutFrame
    {
        public ImRect Bounds;
        public ImLayoutDirection Direction;
        public float Padding;
        public float Spacing;
        public Vector2 ContentCursor;
        public Vector2 ContentExtent;
        public float CurrentLineHeight;
    }

    public static bool IsActive => _depth > 0;

    public static void BeginVertical()
    {
        var style = ImGui.Im.Style;
        BeginVertical(ImGui.Im.WindowContentRect, style.Padding, style.Spacing);
    }

    public static void BeginHorizontal()
    {
        var style = ImGui.Im.Style;
        BeginHorizontal(ImGui.Im.WindowContentRect, style.Padding, style.Spacing);
    }

    public static void BeginVertical(ImRect bounds, float padding, float spacing)
    {
        Push(bounds, ImLayoutDirection.Vertical, padding, spacing);
    }

    public static void BeginHorizontal(ImRect bounds, float padding, float spacing)
    {
        Push(bounds, ImLayoutDirection.Horizontal, padding, spacing);
    }

    public static void End()
    {
        if (_depth <= 0)
        {
            throw new InvalidOperationException("ImLayout.End() called without matching Begin");
        }

        _depth--;
    }

    public static ImRect AllocateRect(float width, float height)
    {
        if (_depth <= 0)
        {
            throw new InvalidOperationException("ImLayout is not active. Call ImLayout.BeginVertical/BeginHorizontal first.");
        }

        ref var frame = ref Frames[_depth - 1];

        float contentX = frame.ContentCursor.X;
        float contentY = frame.ContentCursor.Y;

        if (width <= 0)
        {
            width = RemainingWidth();
        }

        if (height <= 0)
        {
            height = RemainingHeight();
        }

        var rect = new ImRect(contentX, contentY, width, height);

        float maxX = contentX + width;
        float maxY = contentY + height;
        if (maxX > frame.ContentExtent.X)
        {
            frame.ContentExtent.X = maxX;
        }
        if (maxY > frame.ContentExtent.Y)
        {
            frame.ContentExtent.Y = maxY;
        }

        if (frame.Direction == ImLayoutDirection.Vertical)
        {
            frame.ContentCursor = new Vector2(frame.ContentCursor.X, frame.ContentCursor.Y + height + frame.Spacing);
        }
        else
        {
            frame.ContentCursor = new Vector2(frame.ContentCursor.X + width + frame.Spacing, frame.ContentCursor.Y);
            if (height > frame.CurrentLineHeight)
            {
                frame.CurrentLineHeight = height;
            }
        }

        return rect;
    }

    public static void NewLine()
    {
        if (_depth <= 0)
        {
            throw new InvalidOperationException("ImLayout.NewLine() requires an active layout context.");
        }

        ref var frame = ref Frames[_depth - 1];
        if (frame.Direction != ImLayoutDirection.Horizontal)
        {
            return;
        }

        float startX = frame.Bounds.X + frame.Padding;
        frame.ContentCursor = new Vector2(startX, frame.ContentCursor.Y + frame.CurrentLineHeight + frame.Spacing);
        frame.CurrentLineHeight = 0;
    }

    public static void Space(float amount)
    {
        if (_depth <= 0)
        {
            throw new InvalidOperationException("ImLayout.Space() requires an active layout context.");
        }

        ref var frame = ref Frames[_depth - 1];
        if (frame.Direction == ImLayoutDirection.Vertical)
        {
            frame.ContentCursor = new Vector2(frame.ContentCursor.X, frame.ContentCursor.Y + amount);
        }
        else
        {
            frame.ContentCursor = new Vector2(frame.ContentCursor.X + amount, frame.ContentCursor.Y);
        }
    }

    public static float RemainingWidth()
    {
        if (_depth <= 0)
        {
            return 0;
        }

        ref var frame = ref Frames[_depth - 1];
        return frame.Bounds.Right - frame.Padding - frame.ContentCursor.X;
    }

    public static float RemainingHeight()
    {
        if (_depth <= 0)
        {
            return 0;
        }

        ref var frame = ref Frames[_depth - 1];
        return frame.Bounds.Bottom - frame.Padding - frame.ContentCursor.Y;
    }

    /// <summary>
    /// Get the current cursor position in window-local coordinates.
    /// </summary>
    public static Vector2 GetCursor()
    {
        if (_depth <= 0)
        {
            return Vector2.Zero;
        }

        ref var frame = ref Frames[_depth - 1];
        return frame.ContentCursor;
    }

    /// <summary>
    /// Report that content has been drawn at a specific window-local position.
    /// This updates the content extent for scroll calculation without moving the cursor.
    /// Useful for widgets that draw outside the normal layout flow (e.g., trees).
    /// </summary>
    public static void ReportContentUsed(float windowLocalY, float height)
    {
        if (_depth <= 0) return;

        ref var frame = ref Frames[_depth - 1];
        float maxY = windowLocalY + height;
        if (maxY > frame.ContentExtent.Y)
        {
            frame.ContentExtent = new Vector2(frame.ContentExtent.X, maxY);
        }
    }

    internal static void InternalBeginWindow(ImRect windowContentRect, float padding, float spacing)
    {
        _depth = 0;
        Push(windowContentRect, ImLayoutDirection.Vertical, padding, spacing);
    }

    internal static Vector2 InternalEndWindow()
    {
        if (_depth != 1)
        {
            throw new InvalidOperationException($"ImLayout stack not balanced: depth = {_depth}");
        }

        ref var frame = ref Frames[0];
        float contentWidth = Math.Max(0f, frame.ContentExtent.X - frame.Bounds.X + frame.Padding);
        float contentHeight = Math.Max(0f, frame.ContentExtent.Y - frame.Bounds.Y + frame.Padding);

        _depth = 0;
        return new Vector2(contentWidth, contentHeight);
    }

    private static void Push(ImRect bounds, ImLayoutDirection direction, float padding, float spacing)
    {
        if (_depth >= MaxDepth)
        {
            throw new InvalidOperationException("ImLayout stack overflow");
        }

        var startCursor = new Vector2(bounds.X + padding, bounds.Y + padding);
        Frames[_depth] = new LayoutFrame
        {
            Bounds = bounds,
            Direction = direction,
            Padding = padding,
            Spacing = spacing,
            ContentCursor = startCursor,
            ContentExtent = startCursor,
            CurrentLineHeight = 0
        };

        _depth++;
    }
}
