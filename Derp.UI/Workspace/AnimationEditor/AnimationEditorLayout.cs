using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;

namespace Derp.UI;

internal static class AnimationEditorLayout
{
    public const float TopBarHeight = 34f;
    public const float RowHeight = 22f;
    public const float TimelineSelectorWidth = 220f;
    public const float PropertiesPanelWidth = 320f;
    public const float InterpolationPanelWidth = 280f;
    public const float ZoomRangeSliderHeight = 12f;
    public const float TimeRulerRowHeight = 14f;
    public const float WorkAreaRowHeight = 14f;
    public const float RulerHeight = 40f;

    public static void SplitPanels(ImRect bodyRect, out ImRect timelineSelectorRect, out ImRect propertiesRect, out ImRect centerRect, out ImRect interpolationRect)
    {
        float spacing = Im.Style.Spacing;

        float selectorWidth = MathF.Min(TimelineSelectorWidth, MathF.Max(160f, bodyRect.Width * 0.18f));
        float propertiesWidth = MathF.Min(PropertiesPanelWidth, MathF.Max(220f, bodyRect.Width * 0.24f));
        float interpolationWidth = MathF.Min(InterpolationPanelWidth, MathF.Max(200f, bodyRect.Width * 0.22f));

        float centerWidth = MathF.Max(0f, bodyRect.Width - selectorWidth - propertiesWidth - interpolationWidth - spacing * 3f);

        timelineSelectorRect = new ImRect(bodyRect.X, bodyRect.Y, selectorWidth, bodyRect.Height);
        propertiesRect = new ImRect(timelineSelectorRect.Right + spacing, bodyRect.Y, propertiesWidth, bodyRect.Height);
        centerRect = new ImRect(propertiesRect.Right + spacing, bodyRect.Y, centerWidth, bodyRect.Height);
        interpolationRect = new ImRect(centerRect.Right + spacing, bodyRect.Y, interpolationWidth, bodyRect.Height);
    }

    public static void SplitCenterPanel(ImRect centerRect, out ImRect rulerRect, out ImRect canvasRect)
    {
        rulerRect = new ImRect(centerRect.X, centerRect.Y, centerRect.Width, RulerHeight);
        canvasRect = new ImRect(centerRect.X, rulerRect.Bottom, centerRect.Width, MathF.Max(0f, centerRect.Bottom - rulerRect.Bottom));
    }
}
