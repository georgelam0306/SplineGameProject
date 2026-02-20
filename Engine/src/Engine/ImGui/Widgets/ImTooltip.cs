using System.Numerics;
using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

public static class ImTooltip
{
    private static int _hoveredId;
    private static float _hoverTime;
    private static bool _showing;

    public static float ShowDelay = 0.5f;
    public static float Padding = 6f;
    public static Vector2 Offset = new(12f, 12f);

    public static void Begin(int widgetId, bool isHovered)
    {
        if (isHovered)
        {
            if (_hoveredId == widgetId)
            {
                _hoverTime += Im.Context.DeltaTime;
                _showing = _hoverTime >= ShowDelay;
            }
            else
            {
                _hoveredId = widgetId;
                _hoverTime = 0f;
                _showing = false;
            }
        }
        else if (_hoveredId == widgetId)
        {
            _hoveredId = 0;
            _hoverTime = 0f;
            _showing = false;
        }
    }

    public static bool ShouldShow(int widgetId)
    {
        return _showing && _hoveredId == widgetId;
    }

    public static void Draw(string text)
    {
        if (!_showing || string.IsNullOrEmpty(text))
        {
            return;
        }

        DrawInternal(text.AsSpan());
    }

    public static void DrawImmediate(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        DrawInternal(text.AsSpan());
    }

    public static bool BeginForRect(ImRect rect, int widgetId)
    {
        bool isHovered = rect.Contains(Im.MousePos);

        Begin(widgetId, isHovered);
        return ShouldShow(widgetId);
    }

    private static void DrawInternal(ReadOnlySpan<char> text)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        var input = ctx.Input;
        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            return;
        }

        float textWidth = ImTextMetrics.MeasureWidth(ctx.Font, text, style.FontSize);
        float boxWidth = textWidth + Padding * 2f;
        float boxHeight = style.FontSize + Padding * 2f;

        var mousePosViewport = Im.MousePosViewport;
        float viewportX = mousePosViewport.X + Offset.X;
        float viewportY = mousePosViewport.Y + Offset.Y;

        Vector2 viewportSize = viewport.Size;
        if (viewportX + boxWidth > viewportSize.X - 4f)
        {
            viewportX = mousePosViewport.X - boxWidth - 4f;
        }
        if (viewportY + boxHeight > viewportSize.Y - 4f)
        {
            viewportY = mousePosViewport.Y - boxHeight - 4f;
        }
        if (viewportX < 4f)
        {
            viewportX = 4f;
        }
        if (viewportY < 4f)
        {
            viewportY = 4f;
        }

        var drawList = viewport.CurrentDrawList;
        int previousSortKey = drawList.GetSortKey();

        drawList.SetSortKey(1_000_000_000);
        drawList.ClearClipRect();

        Im.PushInverseTransform();

        Im.DrawRoundedRect(viewportX, viewportY, boxWidth, boxHeight, style.CornerRadius, style.Surface);
        Im.DrawRoundedRectStroke(viewportX, viewportY, boxWidth, boxHeight, style.CornerRadius, style.Border, style.BorderWidth);
        Im.Text(text, viewportX + Padding, viewportY + Padding, style.TextPrimary);

        Im.PopTransform();

        // Restore clip rect from context (if any)
        var clip = ctx.ClipRect;
        if (clip.Width > 0f && clip.Height > 0f)
        {
            var clipVec = new Vector4(
                clip.X * Im.Scale,
                clip.Y * Im.Scale,
                clip.Width * Im.Scale,
                clip.Height * Im.Scale);
            drawList.SetClipRect(clipVec);
        }
        else
        {
            drawList.ClearClipRect();
        }

        drawList.SetSortKey(previousSortKey);
    }
}
