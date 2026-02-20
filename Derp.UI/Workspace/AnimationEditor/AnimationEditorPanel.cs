using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;

namespace Derp.UI;

internal static class AnimationEditorPanel
{
    private const float CornerRadius = 10f;
    private const float Padding = 12f;
    private const float HeaderHeight = 32f;

    public static ImRect DrawPanel(ImRect rect, ReadOnlySpan<char> title, out ImRect contentRect)
    {
        DrawBackground(rect);

        float textY = rect.Y + (HeaderHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(title, rect.X + Padding, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float contentY = rect.Y + HeaderHeight;
        contentRect = new ImRect(rect.X + Padding, contentY, MathF.Max(0f, rect.Width - Padding * 2f), MathF.Max(0f, rect.Bottom - contentY - Padding));
        return new ImRect(rect.X + Padding, rect.Y, MathF.Max(0f, rect.Width - Padding * 2f), HeaderHeight);
    }

    public static void DrawPanelBackground(ImRect rect)
    {
        DrawBackground(rect);
    }

    private static void DrawBackground(ImRect rect)
    {
        uint cardColor = ImStyle.Lerp(Im.Style.Background, 0xFFFFFFFF, 0.06f);
        uint border = ImStyle.WithAlphaF(Im.Style.Border, 0.65f);
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, CornerRadius, cardColor);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, CornerRadius, border, Im.Style.BorderWidth);
    }
}

