using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Allocation-free scrollbar widget. Coordinates are in the current Im coordinate space.
/// </summary>
public static class ImScrollbar
{
    /// <summary>
    /// Draw a vertical scrollbar and update <paramref name="scrollOffset"/>.
    /// Returns true if the scroll offset changed.
    /// </summary>
    public static bool DrawVertical(
        int widgetId,
        ImRect rect,
        ref float scrollOffset,
        float viewSize,
        float contentSize)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        var input = ctx.Input;

        var mousePos = Im.MousePos;

        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, style.ScrollbarTrack);

        if (contentSize <= viewSize || rect.Height <= 0f)
        {
            scrollOffset = 0f;
            return false;
        }

        float maxScroll = contentSize - viewSize;
        if (maxScroll <= 0f)
        {
            scrollOffset = 0f;
            return false;
        }

        scrollOffset = Math.Clamp(scrollOffset, 0f, maxScroll);

        float visibleRatio = viewSize / contentSize;
        float thumbHeight = rect.Height * visibleRatio;
        if (thumbHeight < style.MinButtonHeight)
        {
            thumbHeight = style.MinButtonHeight;
        }
        if (thumbHeight > rect.Height)
        {
            thumbHeight = rect.Height;
        }

        float scrollRatio = scrollOffset / maxScroll;
        float thumbY = rect.Y + scrollRatio * (rect.Height - thumbHeight);
        var thumbRect = new ImRect(rect.X, thumbY, rect.Width, thumbHeight);

        bool hovered = thumbRect.Contains(mousePos);
        bool trackHovered = rect.Contains(mousePos);
        bool changed = false;

        if (hovered)
        {
            ctx.SetHot(widgetId);
            if (input.MousePressed && ctx.ActiveId == 0)
            {
                ctx.SetActive(widgetId);
            }
        }
        else if (trackHovered && input.MousePressed && ctx.ActiveId == 0)
        {
            if (mousePos.Y < thumbY)
            {
                float newScrollOffset = scrollOffset - viewSize;
                if (newScrollOffset < 0f)
                {
                    newScrollOffset = 0f;
                }
                scrollOffset = newScrollOffset;
                changed = true;
            }
            else if (mousePos.Y > thumbY + thumbHeight)
            {
                float newScrollOffset = scrollOffset + viewSize;
                if (newScrollOffset > maxScroll)
                {
                    newScrollOffset = maxScroll;
                }
                scrollOffset = newScrollOffset;
                changed = true;
            }
        }

        if (ctx.IsActive(widgetId))
        {
            if (input.MouseDown)
            {
                float newThumbY = mousePos.Y - thumbHeight * 0.5f;
                float newScrollRatio = (newThumbY - rect.Y) / (rect.Height - thumbHeight);
                newScrollRatio = Math.Clamp(newScrollRatio, 0f, 1f);
                float newScrollOffset = newScrollRatio * maxScroll;

                if (newScrollOffset != scrollOffset)
                {
                    scrollOffset = newScrollOffset;
                    changed = true;
                }
            }
            else
            {
                ctx.ClearActive();
            }
        }

        scrollOffset = Math.Clamp(scrollOffset, 0f, maxScroll);

        scrollRatio = scrollOffset / maxScroll;
        thumbY = rect.Y + scrollRatio * (rect.Height - thumbHeight);

        uint thumbColor = ctx.IsActive(widgetId) ? style.Active : (ctx.IsHot(widgetId) ? style.Hover : style.ScrollbarThumb);
        float thumbRadius = style.CornerRadius * 0.5f;
        float maxThumbRadius = Math.Min(rect.Width * 0.5f, thumbHeight * 0.5f);
        if (thumbRadius > maxThumbRadius)
        {
            thumbRadius = maxThumbRadius;
        }
        Im.DrawRoundedRect(rect.X, thumbY, rect.Width, thumbHeight, thumbRadius, thumbColor);

        return changed;
    }
}
