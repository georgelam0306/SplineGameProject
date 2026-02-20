using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;

namespace Derp.UI;

internal static class AnimationsLibraryDragDropPreviewWidget
{
    public static void Draw(UiWorkspace workspace)
    {
        if (!AnimationsLibraryDragDrop.IsDragging || AnimationsLibraryDragDrop.Kind != AnimationsLibraryDragDrop.DragKind.Timeline)
        {
            return;
        }

        if (AnimationsLibraryDragDropPreviewState.IsGlobalSuppressed(Im.Context.FrameCount))
        {
            return;
        }

        int timelineId = AnimationsLibraryDragDrop.TimelineId;
        if (timelineId <= 0)
        {
            return;
        }

        ReadOnlySpan<char> name = "Timeline";
        if (workspace.TryGetActiveAnimationDocument(out var animations) &&
            animations.TryGetTimelineById(timelineId, out var timeline) &&
            timeline != null &&
            !string.IsNullOrEmpty(timeline.Name))
        {
            name = timeline.Name.AsSpan();
        }

        float fontSize = Im.Style.FontSize;
        float paddingX = 10f;
        float paddingY = 7f;

        float textWidth = name.Length * (fontSize * 0.55f);
        float rectW = textWidth + paddingX * 2f;
        float rectH = fontSize + paddingY * 2f;

        var mouse = Im.MousePos;
        float x = mouse.X + 14f;
        float y = mouse.Y + 18f;

        uint fill = ImStyle.WithAlphaF(Im.Style.Surface, 0.92f);
        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.85f);
        uint text = Im.Style.TextPrimary;

        Im.DrawRoundedRect(x, y, rectW, rectH, Im.Style.CornerRadius, fill);
        Im.DrawRoundedRectStroke(x, y, rectW, rectH, Im.Style.CornerRadius, stroke, Im.Style.BorderWidth);
        Im.Text(name, x + paddingX, y + paddingY, fontSize, text);
    }
}

