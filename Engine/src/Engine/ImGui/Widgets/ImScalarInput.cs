using System;
using System.Globalization;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using Silk.NET.Input;

namespace DerpLib.ImGui.Widgets;

public static class ImScalarInput
{
    private static ReadOnlySpan<char> MixedPlaceholder => "—".AsSpan();

    private static readonly int[] StateIds = new int[128];
    private static readonly ScalarState[] States = new ScalarState[128];
    private static int _stateCount;

    private unsafe struct ScalarState
    {
        public int CaretPos;
        public int SelStart;
        public int SelEnd;
        public byte HasSelection;
        public byte WasFocusedPrev;
        public byte BlankMode;
        public float DragStartValue;
        public float DragStartMouseX;
        public float DragStartMouseY;
        public float DragAccumX;
        public byte Pressed;
        public byte Dragging;
        public byte CursorLocked;
        public int TextLen;
        public fixed char Text[32];
    }

    public static float DragSensitivity = 0.02f;

    public static bool DrawAt(
        string label,
        string id,
        float x,
        float y,
        float labelWidth,
        float inputWidth,
        ref float value,
        float min = float.MinValue,
        float max = float.MaxValue,
        string format = "F2")
    {
        var style = Im.Style;
        float height = style.MinButtonHeight;

        var labelRect = new ImRect(x, y, labelWidth, height);
        float labelTextY = labelRect.Y + (height - style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, labelTextY, style.FontSize, style.TextPrimary);

        return DrawAt(id, x + labelWidth, y, inputWidth, ref value, min, max, format);
    }

    public static bool DrawAt(
        string id,
        float x,
        float y,
        float width,
        ref float value,
        float min = float.MinValue,
        float max = float.MaxValue,
        string format = "F2")
    {
        return DrawAt(id, x, y, width, rightOverlayWidth: 0f, ref value, min, max, format);
    }

    public static bool DrawAt(
        string label,
        string id,
        float x,
        float y,
        float labelWidth,
        float inputWidth,
        float rightOverlayWidth,
        ref float value,
        float min = float.MinValue,
        float max = float.MaxValue,
        string format = "F2")
    {
        var style = Im.Style;
        float height = style.MinButtonHeight;

        var labelRect = new ImRect(x, y, labelWidth, height);
        float labelTextY = labelRect.Y + (height - style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, labelTextY, style.FontSize, style.TextPrimary);

        return DrawAt(id, x + labelWidth, y, inputWidth, rightOverlayWidth, ref value, min, max, format);
    }

    public static bool DrawAt(
        string id,
        float x,
        float y,
        float width,
        float rightOverlayWidth,
        ref float value,
        float min = float.MinValue,
        float max = float.MaxValue,
        string format = "F2")
    {
        return DrawAt(id, x, y, width, rightOverlayWidth, ref value, min, max, format, mixed: false, disabled: false);
    }

    public static bool DrawAt(
        string id,
        float x,
        float y,
        float width,
        float rightOverlayWidth,
        ref float value,
        float min,
        float max,
        string format,
        bool mixed)
    {
        return DrawAt(id, x, y, width, rightOverlayWidth, ref value, min, max, format, mixed, disabled: false);
    }

    public static bool DrawAt(
        string id,
        float x,
        float y,
        float width,
        float rightOverlayWidth,
        ref float value,
        float min,
        float max,
        string format,
        bool mixed,
        bool disabled)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        float height = style.MinButtonHeight;
        var rect = new ImRect(x, y, width, height);
        float interactiveWidth = MathF.Max(0f, width - MathF.Max(0f, rightOverlayWidth));
        var interactiveRect = new ImRect(x, y, interactiveWidth, height);

        int widgetId = ctx.GetId(id);
        int stateIndex = FindOrCreateState(widgetId);
        ref ScalarState state = ref States[stateIndex];

        if (disabled)
        {
            if (ctx.IsFocused(widgetId))
            {
                ctx.FocusId = 0;
            }
            if (ctx.IsActive(widgetId))
            {
                ctx.ClearActive();
            }

            state.Pressed = 0;
            state.Dragging = 0;
            if (state.CursorLocked != 0)
            {
                ImMouseCursorLock.End(widgetId);
                state.CursorLocked = 0;
            }
        }
        else
        {
            ctx.RegisterFocusable(widgetId);
        }

        bool hovered = interactiveRect.Contains(Im.MousePos);
        if (hovered && !disabled)
        {
            ctx.SetHot(widgetId);
        }

        bool isFocused = !disabled && ctx.IsFocused(widgetId);
        bool changed = false;

        if (!disabled && !isFocused)
        {
            if (hovered)
            {
                Im.SetCursor(StandardCursor.HResize);
            }

            if (!mixed)
            {
                if (hovered && Im.MousePressed && state.Dragging == 0)
                {
                    ctx.SetActive(widgetId);
                    state.Pressed = 1;
                    state.DragStartMouseX = Im.MousePos.X;
                    state.DragStartMouseY = Im.MousePos.Y;
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
                            state.DragAccumX = 0f;
                            state.CursorLocked = ImMouseCursorLock.Begin(widgetId) ? (byte)1 : (byte)0;
                        }
                    }
                    else
                    {
                        state.Pressed = 0;
                        ctx.RequestFocus(widgetId);
                        Span<char> initBuf = stackalloc char[32];
                        value.TryFormat(initBuf, out int initLen, format);
                        state.CaretPos = initLen;
                        state.SelStart = 0;
                        state.SelEnd = initLen;
                        state.HasSelection = 1;
                        state.BlankMode = 0;
                    }
                }

                if (state.Dragging != 0)
                {
                    ctx.SetActive(widgetId);
                    Im.SetCursor(StandardCursor.HResize);
                    if (Im.MouseDown)
                    {
                        state.DragAccumX += state.CursorLocked != 0 ? ImMouseCursorLock.ConsumeDelta(widgetId).X : ctx.Input.MouseDelta.X;
                        float newValue = state.DragStartValue + state.DragAccumX * DragSensitivity;
                        newValue = Math.Clamp(newValue, min, max);
                        if (MathF.Abs(newValue - value) > 0.0001f)
                        {
                            value = newValue;
                            changed = true;
                        }
                    }
                    else
                    {
                        state.Dragging = 0;
                        if (state.CursorLocked != 0)
                        {
                            ImMouseCursorLock.End(widgetId);
                            state.CursorLocked = 0;
                        }
                    }
                }
            }
            else
            {
                state.Pressed = 0;
                state.Dragging = 0;
                if (state.CursorLocked != 0)
                {
                    ImMouseCursorLock.End(widgetId);
                    state.CursorLocked = 0;
                }
                if (hovered && Im.MousePressed)
                {
                    ctx.SetActive(widgetId);
                    ctx.RequestFocus(widgetId);
                    state.BlankMode = 1;
                    state.CaretPos = 0;
                    state.SelStart = 0;
                    state.SelEnd = 0;
                    state.HasSelection = 0;
                }
            }
        }
        else if (!disabled)
        {
            ctx.SetActive(widgetId);
            var input = ctx.Input;

            if (state.WasFocusedPrev == 0)
            {
                if (mixed)
                {
                    state.BlankMode = 1;
                    state.CaretPos = 0;
                    state.SelStart = 0;
                    state.SelEnd = 0;
                    state.HasSelection = 0;
                    state.TextLen = 0;
                }
                else
                {
                    state.BlankMode = 0;
                    Span<char> initBuf = stackalloc char[32];
                    value.TryFormat(initBuf, out int initLen, format);
                    initLen = Math.Clamp(initLen, 0, 31);
                    unsafe
                    {
                        fixed (char* dst = state.Text)
                        {
                            for (int i = 0; i < initLen; i++)
                            {
                                dst[i] = initBuf[i];
                            }
                        }
                    }
                    state.TextLen = initLen;
                    state.CaretPos = initLen;
                    state.SelStart = 0;
                    state.SelEnd = initLen;
                    state.HasSelection = 1;
                }
            }

            if (Im.MousePressed && rect.Contains(Im.MousePos))
            {
                state.HasSelection = 0;
            }

            bool textChanged = false;

            unsafe
            {
                fixed (char* textPtr = state.Text)
                {
                    var editBuffer = new Span<char>(textPtr, 32);
                    int length = Math.Clamp(state.TextLen, 0, 31);

                    for (int i = 0; i < input.InputCharCount; i++)
                    {
                        char c = input.InputChars[i];
                        if ((c >= '0' && c <= '9') || c == '-' || c == '.' || c == ',')
                        {
                            if (state.BlankMode != 0)
                            {
                                state.BlankMode = 0;
                                length = 0;
                                state.CaretPos = 0;
                                state.SelStart = 0;
                                state.SelEnd = 0;
                                state.HasSelection = 0;
                            }

                            if (state.HasSelection != 0)
                            {
                                DeleteSelection(editBuffer, ref length, ref state);
                                textChanged = true;
                            }

                            if (length < 31)
                            {
                                for (int j = length; j > state.CaretPos; j--)
                                {
                                    editBuffer[j] = editBuffer[j - 1];
                                }
                                editBuffer[state.CaretPos] = c;
                                length++;
                                state.CaretPos++;
                                textChanged = true;
                            }
                        }
                    }

                    if (input.KeyBackspace)
                    {
                        if (state.HasSelection != 0)
                        {
                            DeleteSelection(editBuffer, ref length, ref state);
                            textChanged = true;
                        }
                        else if (state.CaretPos > 0)
                        {
                            for (int i = state.CaretPos - 1; i < length - 1; i++)
                            {
                                editBuffer[i] = editBuffer[i + 1];
                            }
                            length--;
                            state.CaretPos--;
                            textChanged = true;
                        }
                    }

                    if (input.KeyLeft)
                    {
                        if (state.HasSelection != 0)
                        {
                            state.CaretPos = Math.Clamp(state.SelStart, 0, length);
                            state.HasSelection = 0;
                        }
                        else if (state.CaretPos > 0)
                        {
                            state.CaretPos--;
                        }
                    }
                    if (input.KeyRight)
                    {
                        if (state.HasSelection != 0)
                        {
                            state.CaretPos = Math.Clamp(state.SelEnd, 0, length);
                            state.HasSelection = 0;
                        }
                        else if (state.CaretPos < length)
                        {
                            state.CaretPos++;
                        }
                    }

                    state.TextLen = length;

                    if (textChanged)
                    {
                        if (TryParseFloatInvariant(editBuffer.Slice(0, length), out float parsed))
                        {
                            parsed = Math.Clamp(parsed, min, max);
                            if (MathF.Abs(parsed - value) > 0.0001f)
                            {
                                value = parsed;
                                changed = true;
                            }
                        }
                    }

                    if (input.KeyEscape || input.KeyEnter)
                    {
                        ctx.FocusId = 0;
                        if (ctx.IsActive(widgetId))
                        {
                            ctx.ClearActive();
                        }
                        state.HasSelection = 0;
                    }

                    if (Im.MousePressed && !rect.Contains(Im.MousePos))
                    {
                        ctx.FocusId = 0;
                        if (ctx.IsActive(widgetId))
                        {
                            ctx.ClearActive();
                        }
                        state.HasSelection = 0;
                    }

                    // If we just unfocused, snap the edit buffer back to the formatted value.
                    if (state.WasFocusedPrev != 0 && !ctx.IsFocused(widgetId))
                    {
                        Span<char> snapBuf = stackalloc char[32];
                        value.TryFormat(snapBuf, out int snapLen, format);
                        snapLen = Math.Clamp(snapLen, 0, 31);
                        for (int i = 0; i < snapLen; i++)
                        {
                            editBuffer[i] = snapBuf[i];
                        }
                        state.TextLen = snapLen;
                    }
                }
            }
        }

        value = Math.Clamp(value, min, max);
        state.CaretPos = Math.Clamp(state.CaretPos, 0, 31);

        uint background = style.Surface;
        if (hovered && !isFocused && !disabled)
        {
            background = ImStyle.Lerp(style.Surface, 0xFF000000, 0.16f);
        }
        uint border = disabled ? style.Border : (isFocused ? style.Primary : style.Border);
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, border, style.BorderWidth);

        if (!isFocused && mixed)
        {
            ReadOnlySpan<char> placeholder = MixedPlaceholder;
            float placeholderWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, placeholder, style.FontSize);
            float placeholderX = interactiveRect.X + MathF.Max(style.Padding, (interactiveRect.Width - placeholderWidth) * 0.5f);
            float placeholderY = rect.Y + (rect.Height - style.FontSize) * 0.5f;
            Im.Text(placeholder, placeholderX, placeholderY, style.FontSize, style.TextSecondary);

            if (ctx.IsActive(widgetId) && !Im.MouseDown && !isFocused && state.Pressed == 0 && state.Dragging == 0)
            {
                ctx.ClearActive();
            }

            state.WasFocusedPrev = ctx.IsFocused(widgetId) ? (byte)1 : (byte)0;
            return changed;
        }

        Span<char> display = stackalloc char[32];
        int displayLen = 0;
        uint textColor = disabled ? style.TextSecondary : style.TextPrimary;
        if (isFocused)
        {
            if (state.BlankMode != 0)
            {
                displayLen = 0;
            }
            else
            {
                displayLen = Math.Clamp(state.TextLen, 0, 31);
                unsafe
                {
                    fixed (char* src = state.Text)
                    {
                        for (int i = 0; i < displayLen; i++)
                        {
                            display[i] = src[i];
                        }
                    }
                }
            }
        }
        else
        {
            value.TryFormat(display, out displayLen, format);
        }

        ReadOnlySpan<char> displaySpan = display.Slice(0, displayLen);

        float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan, style.FontSize);
        float textX = interactiveRect.X + MathF.Max(style.Padding, (interactiveRect.Width - textWidth) * 0.5f);
        float textY = rect.Y + (rect.Height - style.FontSize) * 0.5f;

        if (isFocused && state.HasSelection != 0 && state.SelStart != state.SelEnd)
        {
            int selStart = Math.Clamp(state.SelStart, 0, displayLen);
            int selEnd = Math.Clamp(state.SelEnd, 0, displayLen);
            if (selEnd < selStart)
            {
                int temp = selStart;
                selStart = selEnd;
                selEnd = temp;
            }

            float selX0 = textX;
            float selX1 = textX;
            if (selStart > 0)
            {
                selX0 += ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan.Slice(0, selStart), style.FontSize);
            }
            if (selEnd > 0)
            {
                selX1 += ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan.Slice(0, selEnd), style.FontSize);
            }

            float selY = rect.Y + 3f;
            float selHeight = rect.Height - 6f;
            uint selColor = ImStyle.WithAlphaF(style.Primary, 0.25f);
            Im.DrawRect(selX0, selY, selX1 - selX0, selHeight, selColor);
        }

        Im.Text(displaySpan, textX, textY, style.FontSize, textColor);

        if (isFocused)
        {
            float caretX = textX;
            if (state.CaretPos > 0 && state.CaretPos <= displayLen)
            {
                caretX += ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan.Slice(0, state.CaretPos), style.FontSize);
            }
            float caretY = rect.Y + 4f;
            float caretHeight = rect.Height - 8f;
            Im.DrawLine(caretX, caretY, caretX, caretY + caretHeight, 1f, style.Primary);
        }

        if (ctx.IsActive(widgetId) && !Im.MouseDown && !isFocused && state.Pressed == 0 && state.Dragging == 0)
        {
            ctx.ClearActive();
        }

        state.WasFocusedPrev = ctx.IsFocused(widgetId) ? (byte)1 : (byte)0;
        return changed;
    }

    private static bool TryParseFloatInvariant(ReadOnlySpan<char> src, out float value)
    {
        Span<char> temp = stackalloc char[32];
        int len = 0;
        int max = Math.Min(src.Length, 31);
        for (int i = 0; i < max; i++)
        {
            char c = src[i];
            if (c == ',')
            {
                c = '.';
            }
            temp[len++] = c;
        }

        return float.TryParse(temp.Slice(0, len), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool DrawIntAt(
        string label,
        string id,
        float x,
        float y,
        float labelWidth,
        float inputWidth,
        ref int value,
        int min = int.MinValue,
        int max = int.MaxValue)
    {
        float floatValue = value;
        bool changed = DrawAt(label, id, x, y, labelWidth, inputWidth, ref floatValue, min, max, "F0");
        if (changed)
        {
            int newValue = (int)MathF.Round(floatValue);
            newValue = Math.Clamp(newValue, min, max);
            value = newValue;
        }
        return changed;
    }

    public static bool DrawIntAt(
        string label,
        string id,
        float x,
        float y,
        float labelWidth,
        float inputWidth,
        float rightOverlayWidth,
        ref int value,
        int min = int.MinValue,
        int max = int.MaxValue)
    {
        float floatValue = value;
        bool changed = DrawAt(label, id, x, y, labelWidth, inputWidth, rightOverlayWidth, ref floatValue, min, max, "F0");
        if (changed)
        {
            int newValue = (int)MathF.Round(floatValue);
            newValue = Math.Clamp(newValue, min, max);
            value = newValue;
        }
        return changed;
    }

    private static int FindOrCreateState(int id)
    {
        for (int i = 0; i < _stateCount; i++)
        {
            if (StateIds[i] == id)
            {
                return i;
            }
        }

        if (_stateCount >= StateIds.Length)
        {
            return 0;
        }

        int index = _stateCount++;
        StateIds[index] = id;
        States[index] = default;
        return index;
    }

    private static void DeleteSelection(Span<char> buffer, ref int length, ref ScalarState state)
    {
        int start = state.SelStart;
        int end = state.SelEnd;
        if (end < start)
        {
            int temp = start;
            start = end;
            end = temp;
        }

        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, 0, length);
        if (end <= start)
        {
            state.HasSelection = 0;
            return;
        }

        int deleteCount = end - start;
        for (int i = start; i < length - deleteCount; i++)
        {
            buffer[i] = buffer[i + deleteCount];
        }

        length -= deleteCount;
        state.CaretPos = start;
        state.SelStart = start;
        state.SelEnd = start;
        state.HasSelection = 0;
    }
}
