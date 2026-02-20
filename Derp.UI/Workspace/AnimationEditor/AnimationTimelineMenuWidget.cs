using System;
using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using Silk.NET.Input;

namespace Derp.UI;

internal static class AnimationTimelineMenuWidget
{
    private const float Width = 240f;
    private const float Padding = 12f;
    private const float RowHeight = 26f;
    private const float FieldHeightPad = 4f;
    private const int MaxTextLen = 15;
    private const float LabelWidth = 112f;

    private static readonly int[] TimecodeStateIds = new int[8];
    private static readonly TimecodeFieldState[] TimecodeStates = new TimecodeFieldState[8];
    private static int _timecodeStateCount;

    private static readonly int[] ScalarStateIds = new int[8];
    private static readonly ScalarFieldState[] ScalarStates = new ScalarFieldState[8];
    private static int _scalarStateCount;

    private struct TimecodeFieldState
    {
        public float DragStartMouseX;
        public int DragStartFrames;
        public byte Pressed;
        public byte Dragging;
    }

    private struct ScalarFieldState
    {
        public float DragStartMouseX;
        public float DragStartValue;
        public byte Pressed;
        public byte Dragging;
    }

    public static void Draw(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline)
    {
        if (!state.TimelineMenuOpen)
        {
            return;
        }

        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            state.TimelineMenuOpen = false;
            return;
        }

        if (state.TimelineMenuNeedsSync)
        {
            SyncBuffers(state, timeline);
            state.TimelineMenuNeedsSync = false;
        }

        var drawList = viewport.CurrentDrawList;
        int previousSortKey = drawList.GetSortKey();
        Vector4 previousClipRect = drawList.GetClipRect();

        drawList.SetSortKey(1_000_000_000);
        drawList.ClearClipRect();

        Vector2 savedTranslation = Im.CurrentTranslation;
        bool pushedCancelTransform = false;
        if (savedTranslation.X != 0f || savedTranslation.Y != 0f)
        {
            Im.PushTransform(-savedTranslation);
            pushedCancelTransform = true;
        }

        float x = state.TimelineMenuPosViewport.X;
        float y = state.TimelineMenuPosViewport.Y;

        float height = Padding * 2f + RowHeight * 5f;
        var rect = new ImRect(x, y, Width, height);
        using var menuOverlayScope = ImPopover.PushOverlayScope(rect);

        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

        DrawTimecodeInputRow(state, timeline, "Current", "anim_tl_current", state.TimelineCurrentBuffer, ref state.TimelineCurrentLength, rowIndex: 0, rect);
        DrawTimecodeInputRow(state, timeline, "Duration", "anim_tl_duration", state.TimelineDurationBuffer, ref state.TimelineDurationLength, rowIndex: 1, rect);
        DrawPlaybackSpeedRow(state, timeline, rowIndex: 2, rect);
        DrawSnapKeysRow(state, timeline, rowIndex: 3, rect);
        DrawQuantizeRow(rowIndex: 4, rect);

        if (ImPopover.ShouldClose(
                openedFrame: state.TimelineMenuOpenFrame,
                closeOnEscape: true,
                closeOnOutsideButtons: ImPopoverCloseButtons.Left,
                consumeCloseClick: false,
                requireNoMouseOwner: false,
                useViewportMouseCoordinates: true,
                insideRect: rect))
        {
            state.TimelineMenuOpen = false;
        }

        drawList.SetClipRect(previousClipRect);
        drawList.SetSortKey(previousSortKey);
        if (pushedCancelTransform)
        {
            Im.PopTransform();
        }
    }

    private static void SyncBuffers(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline)
    {
        state.TimelineCurrentLength = WriteTimecode(state.TimelineCurrentBuffer, state.CurrentFrame, timeline.SnapFps);
        state.TimelineDurationLength = WriteTimecode(state.TimelineDurationBuffer, timeline.DurationFrames, timeline.SnapFps);
        state.TimelineSpeedLength = WriteFloat(state.TimelineSpeedBuffer, timeline.PlaybackSpeed);
        state.TimelineSnapFpsLength = WriteInt(state.TimelineSnapFpsBuffer, timeline.SnapFps);
    }

    private static void DrawTimecodeInputRow(
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        ReadOnlySpan<char> label,
        string inputId,
        char[] bufferArray,
        ref int length,
        int rowIndex,
        ImRect menuRect)
    {
        float rowY = menuRect.Y + Padding + rowIndex * RowHeight;
        var rowRect = new ImRect(menuRect.X + Padding, rowY, MathF.Max(0f, menuRect.Width - Padding * 2f), RowHeight);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label, rowRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        float inputW = MathF.Max(80f, rowRect.Width - LabelWidth);
        float inputH = rowRect.Height - FieldHeightPad;
        float inputX = rowRect.Right - inputW;
        float inputY = rowRect.Y + (rowRect.Height - inputH) * 0.5f;
        var inputRect = new ImRect(inputX, inputY, inputW, inputH);

        int frameValue = rowIndex == 0 ? state.CurrentFrame : timeline.DurationFrames;
        bool changed = DrawTimecodeField(inputId, inputRect, timeline.SnapFps, bufferArray, ref length, ref frameValue);

        if (!changed)
        {
            return;
        }

        if (rowIndex == 0)
        {
            frameValue = Math.Clamp(frameValue, 0, Math.Max(1, timeline.DurationFrames));
            state.CurrentFrame = frameValue;
            return;
        }

        if (frameValue <= 0)
        {
            frameValue = 1;
        }

        timeline.DurationFrames = frameValue;
        timeline.WorkStartFrame = Math.Clamp(timeline.WorkStartFrame, 0, timeline.DurationFrames);
        timeline.WorkEndFrame = Math.Clamp(timeline.WorkEndFrame, 0, timeline.DurationFrames);
        state.CurrentFrame = Math.Clamp(state.CurrentFrame, 0, timeline.DurationFrames);
        length = WriteTimecode(bufferArray, timeline.DurationFrames, timeline.SnapFps);
    }

    private static void DrawPlaybackSpeedRow(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, int rowIndex, ImRect menuRect)
    {
        float rowY = menuRect.Y + Padding + rowIndex * RowHeight;
        var rowRect = new ImRect(menuRect.X + Padding, rowY, MathF.Max(0f, menuRect.Width - Padding * 2f), RowHeight);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Playback Speed".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        float inputW = MathF.Max(80f, rowRect.Width - LabelWidth);
        float inputH = rowRect.Height - FieldHeightPad;
        float inputX = rowRect.Right - inputW;
        float inputY = rowRect.Y + (rowRect.Height - inputH) * 0.5f;
        var inputRect = new ImRect(inputX, inputY, inputW, inputH);

        float speed = timeline.PlaybackSpeed;
        if (DrawFloatFieldWithSuffix(
                id: "anim_tl_speed",
                rect: inputRect,
                buffer: state.TimelineSpeedBuffer,
                length: ref state.TimelineSpeedLength,
                suffix: "x".AsSpan(),
                value: ref speed,
                min: 0f,
                max: 10f,
                dragSensitivity: 0.02f))
        {
            timeline.PlaybackSpeed = speed;
        }
    }

    private static void DrawSnapKeysRow(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, int rowIndex, ImRect menuRect)
    {
        float rowY = menuRect.Y + Padding + rowIndex * RowHeight;
        var rowRect = new ImRect(menuRect.X + Padding, rowY, MathF.Max(0f, menuRect.Width - Padding * 2f), RowHeight);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Snap Keys".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        float inputW = MathF.Max(64f, rowRect.Width - LabelWidth);
        float inputH = rowRect.Height - FieldHeightPad;
        float inputX = rowRect.Right - inputW;
        float inputY = rowRect.Y + (rowRect.Height - inputH) * 0.5f;
        var inputRect = new ImRect(inputX, inputY, inputW, inputH);

        int snapFps = timeline.SnapFps;
        if (DrawIntFieldWithSuffix(
                id: "anim_tl_fps",
                rect: inputRect,
                buffer: state.TimelineSnapFpsBuffer,
                length: ref state.TimelineSnapFpsLength,
                suffix: "fps".AsSpan(),
                value: ref snapFps,
                min: 1,
                max: 240,
                dragPixelsPerStep: 10f))
        {
            timeline.SnapFps = Math.Clamp(snapFps, 1, 240);
            state.TimelineSnapFpsLength = WriteInt(state.TimelineSnapFpsBuffer, timeline.SnapFps);

            var ctx = Im.Context;
            int currentId = ctx.GetId("anim_tl_current");
            if (!ctx.IsFocused(currentId))
            {
                state.TimelineCurrentLength = WriteTimecode(state.TimelineCurrentBuffer, state.CurrentFrame, timeline.SnapFps);
            }

            int durationId = ctx.GetId("anim_tl_duration");
            if (!ctx.IsFocused(durationId))
            {
                state.TimelineDurationLength = WriteTimecode(state.TimelineDurationBuffer, timeline.DurationFrames, timeline.SnapFps);
            }
        }
    }

    private static void DrawQuantizeRow(int rowIndex, ImRect menuRect)
    {
        float rowY = menuRect.Y + Padding + rowIndex * RowHeight;
        var rowRect = new ImRect(menuRect.X + Padding, rowY, MathF.Max(0f, menuRect.Width - Padding * 2f), RowHeight);

        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Quantize".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);
        Im.Text("Off".AsSpan(), rowRect.Right - 22f, textY, Im.Style.FontSize, Im.Style.TextPrimary);
    }

    private static void DrawInputBackground(float x, float y, float w, float h)
    {
        uint bg = ImStyle.Lerp(Im.Style.Background, 0xFFFFFFFF, 0.06f);
        uint border = ImStyle.WithAlphaF(Im.Style.Border, 0.8f);
        Im.DrawRoundedRect(x, y, w, h, 6f, bg);
        Im.DrawRoundedRectStroke(x, y, w, h, 6f, border, Im.Style.BorderWidth);
    }

    private static int WriteTimecode(Span<char> buffer, int frame, int fps)
    {
        if (fps <= 0)
        {
            fps = 60;
        }

        if (frame < 0)
        {
            frame = 0;
        }

        int totalSeconds = frame / fps;
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds - minutes * 60;
        int subFrame = frame - totalSeconds * fps;

        if (buffer.Length < 8)
        {
            return 0;
        }

        buffer[0] = (char)('0' + ((minutes / 10) % 10));
        buffer[1] = (char)('0' + (minutes % 10));
        buffer[2] = ':';
        buffer[3] = (char)('0' + (seconds / 10));
        buffer[4] = (char)('0' + (seconds % 10));
        buffer[5] = ':';
        buffer[6] = (char)('0' + ((subFrame / 10) % 10));
        buffer[7] = (char)('0' + (subFrame % 10));
        return 8;
    }

    private static int WriteInt(Span<char> buffer, int value)
    {
        if (value < 0)
        {
            value = 0;
        }
        if (!value.TryFormat(buffer, out int written))
        {
            return 0;
        }
        return written;
    }

    private static int WriteFloat(Span<char> buffer, float value)
    {
        if (value < 0f)
        {
            value = 0f;
        }
        if (!value.TryFormat(buffer, out int written, "0.##"))
        {
            return 0;
        }
        return written;
    }

    private static bool TryParseTimecode(ReadOnlySpan<char> text, int fps, out int frames)
    {
        frames = 0;
        if (fps <= 0)
        {
            fps = 60;
        }

        int part = 0;
        int partCount = 0;
        Span<int> parts = stackalloc int[3];

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c >= '0' && c <= '9')
            {
                part = part * 10 + (c - '0');
                continue;
            }

            if (c == ':')
            {
                if (partCount < parts.Length)
                {
                    parts[partCount++] = part;
                    part = 0;
                }
                continue;
            }

            if (c == ' ' || c == '\t')
            {
                continue;
            }

            return false;
        }

        if (partCount < parts.Length)
        {
            parts[partCount++] = part;
        }

        if (partCount <= 0)
        {
            return false;
        }

        int minutes = 0;
        int seconds = 0;
        int sub = 0;
        if (partCount == 3)
        {
            minutes = parts[0];
            seconds = parts[1];
            sub = parts[2];
        }
        else if (partCount == 2)
        {
            seconds = parts[0];
            sub = parts[1];
        }
        else
        {
            sub = parts[0];
        }

        frames = minutes * 60 * fps + seconds * fps + sub;
        return true;
    }

    private static bool TryParseInt(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
        {
            i++;
        }

        if (i >= text.Length)
        {
            return false;
        }

        bool any = false;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c >= '0' && c <= '9')
            {
                any = true;
                value = value * 10 + (c - '0');
                continue;
            }

            if (c == ' ' || c == '\t')
            {
                continue;
            }

            return false;
        }

        return any;
    }

    private static bool TryParseFloat(ReadOnlySpan<char> text, out float value)
    {
        value = 0f;
        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
        {
            i++;
        }

        if (i >= text.Length)
        {
            return false;
        }

        int end = text.Length;
        return float.TryParse(text[i..end], out value);
    }

    private static bool DrawTimecodeField(string id, ImRect rect, int fps, Span<char> buffer, ref int length, ref int frames)
    {
        var ctx = Im.Context;
        int widgetId = ctx.GetId(id);
        int stateIndex = FindOrCreateTimecodeState(widgetId);
        ref TimecodeFieldState state = ref TimecodeStates[stateIndex];

        bool isFocused = ctx.IsFocused(widgetId);
        bool hovered = rect.Contains(Im.MousePos);
        if (hovered)
        {
            ctx.SetHot(widgetId);
        }

        bool changed = false;

        if (!isFocused)
        {
            if (hovered)
            {
                Im.SetCursor(StandardCursor.HResize);
            }

            if (hovered && Im.MousePressed && state.Dragging == 0)
            {
                ctx.SetActive(widgetId);
                state.Pressed = 1;
                state.DragStartMouseX = Im.MousePos.X;
                state.DragStartFrames = frames;
            }

            if (state.Pressed != 0)
            {
                ctx.SetActive(widgetId);
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - state.DragStartMouseX;
                    if (MathF.Abs(deltaX) >= 2f)
                    {
                        state.Pressed = 0;
                        state.Dragging = 1;
                    }
                }
                else
                {
                    state.Pressed = 0;
                    ctx.RequestFocus(widgetId);
                    length = WriteTimecode(buffer, frames, fps);
                }
            }

            if (state.Dragging != 0)
            {
                ctx.SetActive(widgetId);
                Im.SetCursor(StandardCursor.HResize);
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - state.DragStartMouseX;
                    int deltaFrames = (int)MathF.Round(deltaX * 0.35f);
                    int newFrames = state.DragStartFrames + deltaFrames;
                    if (newFrames < 0)
                    {
                        newFrames = 0;
                    }
                    if (newFrames != frames)
                    {
                        frames = newFrames;
                        length = WriteTimecode(buffer, frames, fps);
                        changed = true;
                    }
                }
                else
                {
                    state.Dragging = 0;
                }
            }

            DrawInputBackground(rect.X, rect.Y, rect.Width, rect.Height);
            float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
            AnimationEditorText.DrawTimecode(frames, fps, rect.X + 6f, textY, Im.Style.TextPrimary);
            return changed;
        }

        DrawInputBackground(rect.X, rect.Y, rect.Width, rect.Height);

        int maxLen = Math.Min(MaxTextLen, buffer.Length);
        bool textChanged = Im.TextInput(id, buffer, ref length, maxLen, rect.X + 6f, rect.Y, rect.Width - 12f, Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);
        if (textChanged && TryParseTimecode(buffer[..length], fps, out int parsedFrames))
        {
            if (parsedFrames < 0)
            {
                parsedFrames = 0;
            }
            if (parsedFrames != frames)
            {
                frames = parsedFrames;
                changed = true;
            }
        }

        if (ctx.IsActive(widgetId) && !Im.MouseDown)
        {
            ctx.ClearActive();
        }

        return changed;
    }

    private static bool DrawFloatFieldWithSuffix(
        string id,
        ImRect rect,
        Span<char> buffer,
        ref int length,
        ReadOnlySpan<char> suffix,
        ref float value,
        float min,
        float max,
        float dragSensitivity)
    {
        var ctx = Im.Context;
        int widgetId = ctx.GetId(id);
        int stateIndex = FindOrCreateScalarState(widgetId);
        ref ScalarFieldState state = ref ScalarStates[stateIndex];

        bool isFocused = ctx.IsFocused(widgetId);
        bool hovered = rect.Contains(Im.MousePos);
        if (hovered)
        {
            ctx.SetHot(widgetId);
        }

        float suffixTextW = suffix.Length * (Im.Style.FontSize * 0.55f);
        float suffixRegionW = suffixTextW + 12f;
        float suffixTextX = rect.Right - suffixTextW - 6f;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;

        bool changed = false;

        if (!isFocused)
        {
            if (hovered)
            {
                Im.SetCursor(StandardCursor.HResize);
            }

            if (hovered && Im.MousePressed && state.Dragging == 0)
            {
                ctx.SetActive(widgetId);
                state.Pressed = 1;
                state.DragStartMouseX = Im.MousePos.X;
                state.DragStartValue = value;
            }

            if (state.Pressed != 0)
            {
                ctx.SetActive(widgetId);
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - state.DragStartMouseX;
                    if (MathF.Abs(deltaX) >= 2f)
                    {
                        state.Pressed = 0;
                        state.Dragging = 1;
                    }
                }
                else
                {
                    state.Pressed = 0;
                    ctx.RequestFocus(widgetId);
                    length = WriteFloat(buffer, value);
                }
            }

            if (state.Dragging != 0)
            {
                ctx.SetActive(widgetId);
                Im.SetCursor(StandardCursor.HResize);
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - state.DragStartMouseX;
                    float newValue = state.DragStartValue + deltaX * dragSensitivity;
                    newValue = Math.Clamp(newValue, min, max);
                    if (MathF.Abs(newValue - value) > 0.0001f)
                    {
                        value = newValue;
                        length = WriteFloat(buffer, value);
                        changed = true;
                    }
                }
                else
                {
                    state.Dragging = 0;
                }
            }

            DrawInputBackground(rect.X, rect.Y, rect.Width, rect.Height);

            Span<char> display = stackalloc char[32];
            value.TryFormat(display, out int displayLen, "0.##");
            Im.Text(display[..displayLen], rect.X + 6f, textY, Im.Style.FontSize, Im.Style.TextPrimary);
            Im.Text(suffix, suffixTextX, textY, Im.Style.FontSize, Im.Style.TextSecondary);

            return changed;
        }

        DrawInputBackground(rect.X, rect.Y, rect.Width, rect.Height);

        int maxLen = Math.Min(MaxTextLen, buffer.Length);
        var clipRect = new ImRect(rect.X, rect.Y, MathF.Max(0f, rect.Width - suffixRegionW), rect.Height);
        Im.PushClipRect(clipRect);
        bool textChanged = Im.TextInput(id, buffer, ref length, maxLen, rect.X + 6f, rect.Y, rect.Width - 12f, Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);
        Im.PopClipRect();

        if (textChanged && TryParseFloat(buffer[..length], out float parsed))
        {
            parsed = Math.Clamp(parsed, min, max);
            if (MathF.Abs(parsed - value) > 0.0001f)
            {
                value = parsed;
                changed = true;
            }
        }

        Im.Text(suffix, suffixTextX, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        if (ctx.IsActive(widgetId) && !Im.MouseDown)
        {
            ctx.ClearActive();
        }

        return changed;
    }

    private static bool DrawIntFieldWithSuffix(
        string id,
        ImRect rect,
        Span<char> buffer,
        ref int length,
        ReadOnlySpan<char> suffix,
        ref int value,
        int min,
        int max,
        float dragPixelsPerStep)
    {
        var ctx = Im.Context;
        int widgetId = ctx.GetId(id);
        int stateIndex = FindOrCreateScalarState(widgetId);
        ref ScalarFieldState state = ref ScalarStates[stateIndex];

        bool isFocused = ctx.IsFocused(widgetId);
        bool hovered = rect.Contains(Im.MousePos);
        if (hovered)
        {
            ctx.SetHot(widgetId);
        }

        float suffixTextW = suffix.Length * (Im.Style.FontSize * 0.55f);
        float suffixRegionW = suffixTextW + 12f;
        float suffixTextX = rect.Right - suffixTextW - 6f;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;

        bool changed = false;

        if (!isFocused)
        {
            if (hovered)
            {
                Im.SetCursor(StandardCursor.HResize);
            }

            if (hovered && Im.MousePressed && state.Dragging == 0)
            {
                ctx.SetActive(widgetId);
                state.Pressed = 1;
                state.DragStartMouseX = Im.MousePos.X;
                state.DragStartValue = value;
            }

            if (state.Pressed != 0)
            {
                ctx.SetActive(widgetId);
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - state.DragStartMouseX;
                    if (MathF.Abs(deltaX) >= 2f)
                    {
                        state.Pressed = 0;
                        state.Dragging = 1;
                    }
                }
                else
                {
                    state.Pressed = 0;
                    ctx.RequestFocus(widgetId);
                    length = WriteInt(buffer, value);
                }
            }

            if (state.Dragging != 0)
            {
                ctx.SetActive(widgetId);
                Im.SetCursor(StandardCursor.HResize);
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - state.DragStartMouseX;
                    int delta = dragPixelsPerStep <= 0f ? 0 : (int)MathF.Round(deltaX / dragPixelsPerStep);
                    int newValue = (int)state.DragStartValue + delta;
                    newValue = Math.Clamp(newValue, min, max);
                    if (newValue != value)
                    {
                        value = newValue;
                        length = WriteInt(buffer, value);
                        changed = true;
                    }
                }
                else
                {
                    state.Dragging = 0;
                }
            }

            DrawInputBackground(rect.X, rect.Y, rect.Width, rect.Height);

            Span<char> display = stackalloc char[32];
            value.TryFormat(display, out int displayLen);
            Im.Text(display[..displayLen], rect.X + 6f, textY, Im.Style.FontSize, Im.Style.TextPrimary);
            Im.Text(suffix, suffixTextX, textY, Im.Style.FontSize, Im.Style.TextSecondary);

            return changed;
        }

        DrawInputBackground(rect.X, rect.Y, rect.Width, rect.Height);

        int maxLen = Math.Min(MaxTextLen, buffer.Length);
        var clipRect = new ImRect(rect.X, rect.Y, MathF.Max(0f, rect.Width - suffixRegionW), rect.Height);
        Im.PushClipRect(clipRect);
        bool textChanged = Im.TextInput(id, buffer, ref length, maxLen, rect.X + 6f, rect.Y, rect.Width - 12f, Im.ImTextInputFlags.NoBackground | Im.ImTextInputFlags.NoBorder);
        Im.PopClipRect();

        if (textChanged && TryParseInt(buffer[..length], out int parsed))
        {
            parsed = Math.Clamp(parsed, min, max);
            if (parsed != value)
            {
                value = parsed;
                changed = true;
            }
        }

        Im.Text(suffix, suffixTextX, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        if (ctx.IsActive(widgetId) && !Im.MouseDown)
        {
            ctx.ClearActive();
        }

        return changed;
    }

    private static int FindOrCreateTimecodeState(int id)
    {
        for (int i = 0; i < _timecodeStateCount; i++)
        {
            if (TimecodeStateIds[i] == id)
            {
                return i;
            }
        }

        if (_timecodeStateCount >= TimecodeStateIds.Length)
        {
            return 0;
        }

        int index = _timecodeStateCount++;
        TimecodeStateIds[index] = id;
        TimecodeStates[index] = default;
        return index;
    }

    private static int FindOrCreateScalarState(int id)
    {
        for (int i = 0; i < _scalarStateCount; i++)
        {
            if (ScalarStateIds[i] == id)
            {
                return i;
            }
        }

        if (_scalarStateCount >= ScalarStateIds.Length)
        {
            return 0;
        }

        int index = _scalarStateCount++;
        ScalarStateIds[index] = id;
        ScalarStates[index] = default;
        return index;
    }
}
