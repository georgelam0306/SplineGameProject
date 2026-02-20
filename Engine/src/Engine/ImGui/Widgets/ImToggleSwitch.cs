using System;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;

namespace DerpLib.ImGui.Widgets;

public static class ImToggleSwitch
{
    public static bool Draw(string label, ref bool value)
    {
        var style = Im.Style;

        float switchWidth = style.CheckboxSize * 1.8f;
        float switchHeight = style.CheckboxSize;

        float labelWidth = string.IsNullOrEmpty(label)
            ? 0f
            : ImTextMetrics.MeasureWidth(Im.Context.Font, label.AsSpan(), style.FontSize);

        float totalWidth = switchWidth + (labelWidth > 0f ? style.Spacing + labelWidth : 0f);
        float totalHeight = Math.Max(switchHeight, style.FontSize);

        var rect = ImLayout.AllocateRect(totalWidth, totalHeight);
        return DrawAt(label, rect.X, rect.Y, ref value);
    }

    public static bool DrawAt(string label, float x, float y, ref bool value)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        var input = ctx.Input;

        int widgetId = ctx.GetId(label);

        float switchWidth = style.CheckboxSize * 1.8f;
        float switchHeight = style.CheckboxSize;
        float thumbSize = switchHeight - 4f;

        float labelWidth = string.IsNullOrEmpty(label)
            ? 0f
            : ImTextMetrics.MeasureWidth(ctx.Font, label.AsSpan(), style.FontSize);

        float totalWidth = switchWidth + (labelWidth > 0f ? style.Spacing + labelWidth : 0f);
        float totalHeight = Math.Max(switchHeight, style.FontSize);

        var rect = new ImRect(x, y, totalWidth, totalHeight);
        var switchRect = new ImRect(rect.X, rect.Y + (rect.Height - switchHeight) * 0.5f, switchWidth, switchHeight);

        bool hovered = switchRect.Contains(Im.MousePos);
        bool changed = false;

        if (hovered)
        {
            ctx.SetHot(widgetId);
            if (input.MousePressed && ctx.ActiveId == 0)
            {
                ctx.SetActive(widgetId);
            }
        }

        if (ctx.IsActive(widgetId) && input.MouseReleased)
        {
            if (hovered)
            {
                value = !value;
                changed = true;
            }
            ctx.ClearActive();
        }

        uint trackColor = value ? style.Primary : style.ScrollbarTrack;
        float trackRadius = switchHeight * 0.5f;
        Im.DrawRoundedRect(switchRect.X, switchRect.Y, switchRect.Width, switchRect.Height, trackRadius, trackColor);

        float thumbX = value
            ? switchRect.X + switchWidth - thumbSize - 2f
            : switchRect.X + 2f;

        uint thumbColor = ctx.IsActive(widgetId) ? style.Active : (ctx.IsHot(widgetId) ? style.Hover : style.Surface);
        Im.DrawCircle(thumbX + thumbSize * 0.5f, switchRect.Y + 2f + thumbSize * 0.5f, thumbSize * 0.5f, thumbColor);

        if (!string.IsNullOrEmpty(label))
        {
            float labelX = x + switchWidth + style.Spacing;
            float labelY = y + (totalHeight - style.FontSize) * 0.5f;
            Im.Text(label.AsSpan(), labelX, labelY, style.TextPrimary);
        }

        return changed;
    }
}
