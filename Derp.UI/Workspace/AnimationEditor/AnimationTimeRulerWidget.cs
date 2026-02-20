using System;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;

namespace Derp.UI;

internal static class AnimationTimeRulerWidget
{
    public static void Draw(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline? timeline, ImRect rect)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Background);
        Im.DrawRect(rect.X, rect.Y + rect.Height - 1f, rect.Width, 1f, Im.Style.Border);

        if (timeline == null)
        {
            Im.Text("Select a timeline.", rect.X + Im.Style.Padding, rect.Y + 6f, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        float zoomHeight = AnimationEditorLayout.ZoomRangeSliderHeight;
        float timeRulerHeight = AnimationEditorLayout.TimeRulerRowHeight;

        float minWorkAreaHeight = 8f;
        if (rect.Height < zoomHeight + timeRulerHeight + minWorkAreaHeight)
        {
            zoomHeight = 0f;
        }

        float y = rect.Y;
        var zoomRect = new ImRect(rect.X, y, rect.Width, zoomHeight);
        y += zoomHeight;
        var timeRulerRect = new ImRect(rect.X, y, rect.Width, MathF.Max(0f, timeRulerHeight));
        y += timeRulerRect.Height;
        var workAreaRect = new ImRect(rect.X, y, rect.Width, MathF.Max(0f, rect.Bottom - y));

        float pixelsPerFrame = AnimationEditorHelpers.GetPixelsPerFrame(state);
        int duration = Math.Max(1, timeline.DurationFrames);

        if (zoomHeight > 0f)
        {
            Im.PushClipRect(zoomRect);
            DrawZoomRangeSlider(state, timeline, zoomRect, timeRulerRect.Width, pixelsPerFrame);
            Im.PopClipRect();
            pixelsPerFrame = AnimationEditorHelpers.GetPixelsPerFrame(state);
        }

        var input = Im.Context.Input;
        if (timeRulerRect.Contains(Im.MousePos) && input.KeyCtrl && input.ScrollDelta != 0f && !state.IsDraggingPlayhead)
        {
            float oldPixelsPerFrame = pixelsPerFrame;
            int viewStartFrame = Math.Max(0, state.ViewStartFrame);
            float anchorFrame = viewStartFrame;
            if (oldPixelsPerFrame > 0.0001f)
            {
                anchorFrame = viewStartFrame + (Im.MousePos.X - timeRulerRect.X) / oldPixelsPerFrame;
            }

            float factor = MathF.Pow(1.15f, input.ScrollDelta);
            state.Zoom = Math.Clamp(state.Zoom * factor, 0.10f, 32f);

            pixelsPerFrame = AnimationEditorHelpers.GetPixelsPerFrame(state);
            if (pixelsPerFrame > 0.0001f)
            {
                int newStart = (int)MathF.Round(anchorFrame - (Im.MousePos.X - timeRulerRect.X) / pixelsPerFrame);
                state.ViewStartFrame = Math.Clamp(newStart, 0, duration);
            }
        }

        if (timeRulerRect.Contains(Im.MousePos) && input.KeyCtrl && input.MouseMiddlePressed && !state.IsDraggingPlayhead)
        {
            state.PanDrag.Active = true;
            state.PanDrag.StartMouseX = Im.MousePos.X;
            state.PanDrag.StartViewStartFrame = Math.Max(0, state.ViewStartFrame);
        }

        if (state.PanDrag.Active)
        {
            if (!input.MouseMiddleDown || !input.KeyCtrl)
            {
                state.PanDrag.Active = false;
            }
            else if (pixelsPerFrame > 0.0001f)
            {
                float dx = Im.MousePos.X - state.PanDrag.StartMouseX;
                int deltaFrames = (int)MathF.Round(dx / pixelsPerFrame);
                state.ViewStartFrame = Math.Clamp(state.PanDrag.StartViewStartFrame - deltaFrames, 0, duration);
            }
        }

        int startFrame = Math.Max(0, state.ViewStartFrame);
        int endFrame = Math.Min(duration, startFrame + (int)(timeRulerRect.Width / pixelsPerFrame) + 1);

        if (timeRulerRect.Height > 0f)
        {
            Im.PushClipRect(timeRulerRect);

            int tickStep = GetTickStepFrames(pixelsPerFrame);
            int labelStep = GetLabelStepFrames(pixelsPerFrame, timeline.SnapFps);

            int firstTick = AlignUp(startFrame, tickStep);
            for (int frame = firstTick; frame <= endFrame; frame += tickStep)
            {
                float x = timeRulerRect.X + (frame - startFrame) * pixelsPerFrame;
                if (x < timeRulerRect.X || x > timeRulerRect.Right)
                {
                    continue;
                }

                bool major = (frame % (tickStep * 5)) == 0;
                float h = major ? timeRulerRect.Height : timeRulerRect.Height * 0.4f;
                uint color = major ? Im.Style.Border : 0xFF333333;
                Im.DrawLine(x, timeRulerRect.Bottom, x, timeRulerRect.Bottom - h, 1f, color);

                if (labelStep > 0 && major && (frame % labelStep) == 0)
                {
                    float tx = x + 2f;
                    float ty = timeRulerRect.Y + 1f;
                    AnimationEditorText.DrawTimecode(frame, timeline.SnapFps, tx, ty, Im.Style.TextSecondary);
                }
            }

            Im.PopClipRect();
        }

        if (!Im.MouseDown)
        {
            state.IsDraggingPlayhead = false;
            state.KeyDrag = default;
        }

        if (timeRulerRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            state.IsPlaying = false;
            state.IsDraggingPlayhead = true;
            state.Selected = default;
        }

        if (state.IsDraggingPlayhead && Im.MouseDown)
        {
            int frame = startFrame + (int)((Im.MousePos.X - timeRulerRect.X) / pixelsPerFrame + 0.5f);
            state.CurrentFrame = Math.Clamp(frame, 0, duration);
        }

        if (workAreaRect.Height > 0f)
        {
            Im.PushClipRect(workAreaRect);
            DrawWorkAreaRow(state, timeline, workAreaRect, pixelsPerFrame, startFrame);
            Im.PopClipRect();
        }

        // Draw playhead across the full ruler area (including work area bar).
        Im.PushClipRect(rect);
        DrawPlayhead(state, timeline, rect, pixelsPerFrame, startFrame);
        Im.PopClipRect();
    }

    private static int GetTickStepFrames(float pixelsPerFrame)
    {
        // Ensure minor ticks don't get too dense when zoomed out.
        if (pixelsPerFrame >= 10f)
        {
            return 1;
        }
        if (pixelsPerFrame >= 6f)
        {
            return 2;
        }
        if (pixelsPerFrame >= 3f)
        {
            return 5;
        }
        if (pixelsPerFrame >= 1.5f)
        {
            return 10;
        }
        if (pixelsPerFrame >= 0.75f)
        {
            return 20;
        }
        if (pixelsPerFrame >= 0.35f)
        {
            return 50;
        }
        return 100;
    }

    private static int GetLabelStepFrames(float pixelsPerFrame, int fps)
    {
        if (fps <= 0)
        {
            fps = 60;
        }

        // Roughly keep labels ~80px apart.
        float framesPer80px = 80f / MathF.Max(0.001f, pixelsPerFrame);
        int step = fps;
        while (step < 3600 && step < framesPer80px)
        {
            step *= 2;
        }

        if (step <= 0)
        {
            step = fps;
        }

        return step;
    }

    private static int AlignUp(int value, int step)
    {
        if (step <= 1)
        {
            return value;
        }

        int rem = value % step;
        if (rem == 0)
        {
            return value;
        }
        return value + (step - rem);
    }

    private static void DrawWorkAreaRow(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, ImRect rect, float pixelsPerFrame, int viewStartFrame)
    {
        int duration = Math.Max(1, timeline.DurationFrames);
        int start = Math.Clamp(timeline.WorkStartFrame, 0, duration);
        int end = Math.Clamp(timeline.WorkEndFrame, 0, duration);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, 0xFF121212);
        Im.DrawRect(rect.X, rect.Bottom - 1f, rect.Width, 1f, Im.Style.Border);

        float x0 = rect.X + (start - viewStartFrame) * pixelsPerFrame;
        float x1 = rect.X + (end - viewStartFrame) * pixelsPerFrame;

        float fillX = MathF.Max(rect.X, x0);
        float fillRight = MathF.Min(rect.Right, x1);
        float fillW = MathF.Max(0f, fillRight - fillX);
        Im.DrawRect(fillX, rect.Y, fillW, rect.Height, 0x442266FF);

        float handleWidth = 6f;
        var leftHandle = new ImRect(x0 - handleWidth * 0.5f, rect.Y, handleWidth, rect.Height);
        var rightHandle = new ImRect(x1 - handleWidth * 0.5f, rect.Y, handleWidth, rect.Height);

        Im.DrawRect(leftHandle.X, leftHandle.Y, leftHandle.Width, leftHandle.Height, 0xFF4488FF);
        Im.DrawRect(rightHandle.X, rightHandle.Y, rightHandle.Width, rightHandle.Height, 0xFF4488FF);

        float rangeX0 = MathF.Min(x0, x1);
        float rangeX1 = MathF.Max(x0, x1);
        var rangeRect = new ImRect(rangeX0, rect.Y, MathF.Max(0f, rangeX1 - rangeX0), rect.Height);

        bool mousePressed = Im.MousePressed;
        if (mousePressed)
        {
            if (leftHandle.Contains(Im.MousePos))
            {
                state.WorkDrag.Kind = AnimationEditorState.WorkAreaDragKind.ResizeLeft;
            }
            else if (rightHandle.Contains(Im.MousePos))
            {
                state.WorkDrag.Kind = AnimationEditorState.WorkAreaDragKind.ResizeRight;
            }
            else if (rangeRect.Contains(Im.MousePos))
            {
                state.WorkDrag.Kind = AnimationEditorState.WorkAreaDragKind.Pan;
            }
            else
            {
                state.WorkDrag.Kind = AnimationEditorState.WorkAreaDragKind.None;
            }

            if (state.WorkDrag.Kind != AnimationEditorState.WorkAreaDragKind.None)
            {
                state.WorkDrag.StartMouseX = Im.MousePos.X;
                state.WorkDrag.StartWorkStartFrame = start;
                state.WorkDrag.StartWorkEndFrame = end;
            }
        }

        if (!Im.MouseDown && state.WorkDrag.Kind != AnimationEditorState.WorkAreaDragKind.None)
        {
            state.WorkDrag.Kind = AnimationEditorState.WorkAreaDragKind.None;
        }

        if (!Im.MouseDown || state.WorkDrag.Kind == AnimationEditorState.WorkAreaDragKind.None)
        {
            return;
        }

        float dx = Im.MousePos.X - state.WorkDrag.StartMouseX;
        int deltaFrames = (int)MathF.Round(dx / pixelsPerFrame);

        int newStart = state.WorkDrag.StartWorkStartFrame;
        int newEnd = state.WorkDrag.StartWorkEndFrame;

        if (state.WorkDrag.Kind == AnimationEditorState.WorkAreaDragKind.ResizeLeft)
        {
            newStart = Math.Clamp(newStart + deltaFrames, 0, newEnd);
        }
        else if (state.WorkDrag.Kind == AnimationEditorState.WorkAreaDragKind.ResizeRight)
        {
            newEnd = Math.Clamp(newEnd + deltaFrames, newStart, duration);
        }
        else
        {
            int widthFrames = Math.Max(0, newEnd - newStart);
            int shiftedStart = newStart + deltaFrames;
            shiftedStart = Math.Clamp(shiftedStart, 0, duration - widthFrames);
            newStart = shiftedStart;
            newEnd = shiftedStart + widthFrames;
        }

        timeline.WorkStartFrame = newStart;
        timeline.WorkEndFrame = newEnd;
    }

    private static void DrawZoomRangeSlider(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, ImRect rect, float rulerWidth, float pixelsPerFrame)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        int duration = Math.Max(1, timeline.DurationFrames);

        int viewStartFrame = Math.Clamp(state.ViewStartFrame, 0, duration);
        int visibleFrames = Math.Max(1, (int)MathF.Ceiling(rulerWidth / pixelsPerFrame));
        int viewEndFrame;
        if (visibleFrames >= duration - viewStartFrame)
        {
            viewEndFrame = duration;
        }
        else
        {
            viewEndFrame = viewStartFrame + visibleFrames;
        }
        if (viewEndFrame <= viewStartFrame)
        {
            viewEndFrame = Math.Min(duration, viewStartFrame + 1);
        }

        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, 0xFF141414);
        Im.DrawRect(rect.X, rect.Bottom - 1f, rect.Width, 1f, Im.Style.Border);

        float startT = (float)viewStartFrame / duration;
        float endT = (float)viewEndFrame / duration;

        float selX0 = rect.X + startT * rect.Width;
        float selX1 = rect.X + endT * rect.Width;
        if (selX1 < selX0)
        {
            (selX0, selX1) = (selX1, selX0);
        }

        float selWidth = MathF.Max(1f, selX1 - selX0);
        var selectionRect = new ImRect(selX0, rect.Y, selWidth, rect.Height);

        uint selectionFill = 0xFF2A2A2A;
        uint selectionBorder = 0xFF3A3A3A;
        uint capColor = 0xFF1E5DFF;

        Im.DrawRect(selectionRect.X, selectionRect.Y, selectionRect.Width, selectionRect.Height, selectionFill);
        Im.DrawRoundedRectStroke(selectionRect.X, selectionRect.Y, selectionRect.Width, selectionRect.Height, 0f, selectionBorder, 1f);

        float capWidth = 10f;
        float capWidthClamped = MathF.Min(capWidth, selectionRect.Width * 0.5f);
        var leftCap = new ImRect(selectionRect.X, rect.Y, capWidthClamped, rect.Height);
        var rightCap = new ImRect(selectionRect.Right - capWidthClamped, rect.Y, capWidthClamped, rect.Height);

        Im.DrawRect(leftCap.X, leftCap.Y, leftCap.Width, leftCap.Height, capColor);
        Im.DrawRect(rightCap.X, rightCap.Y, rightCap.Width, rightCap.Height, capColor);

        float grabWidth = MathF.Max(10f, capWidthClamped + 6f);
        var leftGrab = new ImRect(selectionRect.X - grabWidth * 0.5f, rect.Y, grabWidth, rect.Height);
        var rightGrab = new ImRect(selectionRect.Right - grabWidth * 0.5f, rect.Y, grabWidth, rect.Height);

        bool mousePressed = Im.MousePressed;
        if (mousePressed)
        {
            if (leftGrab.Contains(Im.MousePos))
            {
                state.ZoomDrag.Kind = AnimationEditorState.ZoomRangeDragKind.ResizeLeft;
            }
            else if (rightGrab.Contains(Im.MousePos))
            {
                state.ZoomDrag.Kind = AnimationEditorState.ZoomRangeDragKind.ResizeRight;
            }
            else if (selectionRect.Contains(Im.MousePos))
            {
                state.ZoomDrag.Kind = AnimationEditorState.ZoomRangeDragKind.Pan;
            }
            else
            {
                state.ZoomDrag.Kind = AnimationEditorState.ZoomRangeDragKind.None;
            }

            if (state.ZoomDrag.Kind != AnimationEditorState.ZoomRangeDragKind.None)
            {
                state.ZoomDrag.StartMouseX = Im.MousePos.X;
                state.ZoomDrag.StartViewStartFrame = viewStartFrame;
                state.ZoomDrag.StartViewEndFrame = viewEndFrame;
            }
        }

        if (!Im.MouseDown && state.ZoomDrag.Kind != AnimationEditorState.ZoomRangeDragKind.None)
        {
            state.ZoomDrag.Kind = AnimationEditorState.ZoomRangeDragKind.None;
        }

        if (!Im.MouseDown || state.ZoomDrag.Kind == AnimationEditorState.ZoomRangeDragKind.None)
        {
            return;
        }

        float dx = Im.MousePos.X - state.ZoomDrag.StartMouseX;
        int deltaFrames = (int)MathF.Round(dx / rect.Width * duration);

        int newStart = state.ZoomDrag.StartViewStartFrame;
        int newEnd = state.ZoomDrag.StartViewEndFrame;

        if (state.ZoomDrag.Kind == AnimationEditorState.ZoomRangeDragKind.ResizeLeft)
        {
            newStart = Math.Clamp(newStart + deltaFrames, 0, newEnd - 1);
        }
        else if (state.ZoomDrag.Kind == AnimationEditorState.ZoomRangeDragKind.ResizeRight)
        {
            newEnd = Math.Clamp(newEnd + deltaFrames, newStart + 1, duration);
        }
        else
        {
            int widthFrames = Math.Max(1, newEnd - newStart);
            int shiftedStart = newStart + deltaFrames;
            shiftedStart = Math.Clamp(shiftedStart, 0, duration - widthFrames);
            newStart = shiftedStart;
            newEnd = shiftedStart + widthFrames;
        }

        int newVisibleFrames = Math.Max(1, newEnd - newStart);
        state.ViewStartFrame = newStart;
        state.Zoom = MathF.Max(0.0001f, (rulerWidth / newVisibleFrames) / 8f);
    }

    private static void DrawPlayhead(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, ImRect rect, float pixelsPerFrame, int viewStartFrame)
    {
        int duration = Math.Max(1, timeline.DurationFrames);
        state.CurrentFrame = Math.Clamp(state.CurrentFrame, 0, duration);
        float x = rect.X + (state.CurrentFrame - viewStartFrame) * pixelsPerFrame;
        Im.DrawLine(x, rect.Y, x, rect.Bottom, 2f, 0xFF33CCFF);
    }
}
