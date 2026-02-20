using System;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;

namespace DerpLib.ImGui.Widgets;

public static class ImProgressBar
{
    public static void Draw(float value, float width = 150f, float height = 0f)
    {
        Draw(value, width, height, label: ReadOnlySpan<char>.Empty);
    }

    public static void Draw(ImFormatHandler label, float value, float width = 150f, float height = 0f)
    {
        Draw(value, width, height, label.Text);
    }

    public static void DrawFill(float value, float height = 0f)
    {
        float width = ImLayout.RemainingWidth();
        Draw(value, width, height, label: ReadOnlySpan<char>.Empty);
    }

    public static void DrawWithPercent(float value, float width = 150f, float height = 0f)
    {
        ImFormatHandler handler = $"{value * 100f:F0}%";
        Draw(value, width, height, handler.Text);
    }

    private static void Draw(float value, float width, float height, ReadOnlySpan<char> label)
    {
        var style = Im.Style;

        float resolvedHeight = height > 0f ? height : style.MinButtonHeight;
        var rect = ImLayout.AllocateRect(width, resolvedHeight);

        value = Math.Clamp(value, 0f, 1f);

        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, style.ScrollbarTrack);

        if (value > 0f)
        {
            float fillWidth = rect.Width * value;
            Im.DrawRoundedRect(rect.X, rect.Y, fillWidth, rect.Height, style.CornerRadius, style.Primary);
        }

        if (!label.IsEmpty)
        {
            float labelWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, label, style.FontSize);
            float labelX = rect.X + (rect.Width - labelWidth) * 0.5f;
            float labelY = rect.Y + (rect.Height - style.FontSize) * 0.5f;
            Im.Text(label, labelX, labelY, style.TextPrimary);
        }
    }
}
