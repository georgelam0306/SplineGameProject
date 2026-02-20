using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Allocation-free scroll view for absolute-positioned content.
/// Coordinates are in the current Im coordinate space (window-local if inside a window, otherwise viewport-local).
/// Handles clipping and optional wheel/scrollbar interaction.
/// </summary>
public static class ImScrollView
{
    /// <summary>
    /// Begin a scroll view. Pushes a clip rect for <paramref name="contentRect"/>.
    /// Returns the unscrolled content origin Y (typically <c>contentRect.Y</c>).
    /// </summary>
    public static float Begin(
        ImRect contentRect,
        float contentHeight,
        ref float scrollY,
        bool handleMouseWheel)
    {
        var ctx = Im.Context;
        var input = ctx.Input;

        float viewHeight = contentRect.Height;
        float maxScroll = Math.Max(0f, contentHeight - viewHeight);

        if (handleMouseWheel && contentRect.Contains(Im.MousePos) && input.ScrollDelta != 0f)
        {
            scrollY -= input.ScrollDelta * 30f;
        }

        scrollY = Math.Clamp(scrollY, 0f, maxScroll);

        Im.PushClipRect(contentRect);
        Im.PushTransform(0f, -scrollY);
        return contentRect.Y;
    }

    /// <summary>
    /// End a scroll view. Pops the clip rect and draws a vertical scrollbar in <paramref name="scrollbarRect"/>.
    /// </summary>
    public static void End(
        int scrollbarWidgetId,
        ImRect scrollbarRect,
        float viewHeight,
        float contentHeight,
        ref float scrollY)
    {
        float maxScroll = Math.Max(0f, contentHeight - viewHeight);
        scrollY = Math.Clamp(scrollY, 0f, maxScroll);

        Im.PopTransform();
        Im.PopClipRect();

        if (maxScroll <= 0f)
        {
            return;
        }

        ImScrollbar.DrawVertical(
            scrollbarWidgetId,
            scrollbarRect,
            ref scrollY,
            viewHeight,
            contentHeight);
    }
}
