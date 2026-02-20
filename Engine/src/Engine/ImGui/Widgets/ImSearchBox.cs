using System;
using System.Collections.Generic;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;

namespace DerpLib.ImGui.Widgets;

public static class ImSearchBox
{
    private static readonly Dictionary<int, SearchBoxState> States = new(16);

    private struct SearchBoxState
    {
        public int CaretPos;
        public int SelectionStart;
        public int SelectionEnd;
        public float ScrollOffsetX;
        public float BackspaceRepeatHeldTime;
        public float BackspaceRepeatTimer;
        public float DeleteRepeatHeldTime;
        public float DeleteRepeatTimer;
    }

    public static bool Draw(string id, Span<char> buffer, ref int length, int maxLength, float width = 150f, string placeholder = "Search...")
    {
        var style = Im.Style;
        float height = style.MinButtonHeight;
        var rect = ImLayout.AllocateRect(width, height);
        return DrawAt(id, buffer, ref length, maxLength, rect.X, rect.Y, rect.Width, placeholder);
    }

    public static bool DrawAt(string id, Span<char> buffer, ref int length, int maxLength, float x, float y, float width, string placeholder = "Search...")
    {
        var ctx = Im.Context;
        var style = Im.Style;
        var input = ctx.Input;
        var mousePos = Im.MousePos;

        int widgetId = ctx.GetId(id);

        float height = style.MinButtonHeight;
        float iconSize = 16f;
        float padding = style.Padding;
        float clearButtonSize = height - 8f;

        var windowTextRect = new ImRect(
            x + iconSize + padding,
            y,
            width - iconSize - padding * 2f - (length > 0 ? clearButtonSize : 0f),
            height);
        var windowClearRect = new ImRect(x + width - clearButtonSize - 4f, y + 4f, clearButtonSize, clearButtonSize);

        var rect = new ImRect(x, y, width, height);

        if (!States.TryGetValue(widgetId, out var state))
        {
            state = new SearchBoxState { CaretPos = length, SelectionStart = -1, SelectionEnd = -1 };
        }

        bool isFocused = ctx.IsFocused(widgetId);
        bool hovered = rect.Contains(mousePos);
        bool clearHovered = length > 0 && windowClearRect.Contains(mousePos);
        bool changed = false;

        if (hovered)
        {
            ctx.SetHot(widgetId);
        }

        if (clearHovered && input.MousePressed)
        {
            length = 0;
            state.CaretPos = 0;
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
            state.ScrollOffsetX = 0f;
            changed = true;
            ctx.ResetCaretBlink();
        }
        else if (hovered && input.MousePressed && !clearHovered)
        {
            ctx.RequestFocus(widgetId);
            ctx.SetActive(widgetId);
            float relativeX = (mousePos.X - windowTextRect.X) + state.ScrollOffsetX;
            state.CaretPos = ImTextEdit.GetCaretPosFromRelativeX(ctx.Font, buffer.Slice(0, length), style.FontSize, letterSpacingPx: 0f, relativeX);
            state.SelectionStart = state.CaretPos;
            state.SelectionEnd = state.CaretPos;
            ctx.ResetCaretBlink();
        }

        // Drag selection (and optional auto-scroll while dragging).
        if (ctx.IsActive(widgetId))
        {
            if (input.MouseDown)
            {
                float visibleMinX = windowTextRect.X + 4f;
                float visibleMaxX = windowTextRect.Right - 4f;

                float mouseX = mousePos.X;
                if (mouseX < visibleMinX)
                {
                    float overshoot = visibleMinX - mouseX;
                    state.ScrollOffsetX -= (200f + overshoot * 12f) * ctx.DeltaTime;
                }
                else if (mouseX > visibleMaxX)
                {
                    float overshoot = mouseX - visibleMaxX;
                    state.ScrollOffsetX += (200f + overshoot * 12f) * ctx.DeltaTime;
                }

                state.ScrollOffsetX = ClampHorizontalScroll(ctx, buffer.Slice(0, length), style.FontSize, state.ScrollOffsetX, windowTextRect.Width);

                float clampedMouseX = Math.Clamp(mouseX, windowTextRect.X, windowTextRect.Right);
                float relativeX = (clampedMouseX - windowTextRect.X) + state.ScrollOffsetX;
                int caretPos = ImTextEdit.GetCaretPosFromRelativeX(ctx.Font, buffer.Slice(0, length), style.FontSize, letterSpacingPx: 0f, relativeX);
                state.CaretPos = caretPos;
                state.SelectionEnd = caretPos;
                ctx.ResetCaretBlink();
            }
            else if (input.MouseReleased)
            {
                ctx.ClearActive();
            }
        }

        if (isFocused)
        {
            ctx.WantCaptureKeyboard = true;
            bool shouldScrollToCaret = false;

            unsafe
            {
                for (int i = 0; i < input.InputCharCount; i++)
                {
                    char c = input.InputChars[i];
                    if (c < 32)
                    {
                        continue;
                    }

                    if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
                    {
                        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
                        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
                        ImTextEdit.DeleteRange(buffer, ref length, selStart, selEnd);
                        state.CaretPos = selStart;
                        state.SelectionStart = -1;
                        state.SelectionEnd = -1;
                    }

                    if (length < maxLength)
                    {
                        ImTextEdit.InsertChar(buffer, ref length, maxLength, state.CaretPos, c);
                        state.CaretPos++;
                        changed = true;
                        shouldScrollToCaret = true;
                        ctx.ResetCaretBlink();
                    }
                }
            }

            if (input.KeyBackspace)
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0.45f;
                if (ApplyBackspace(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }
            else if (input.KeyBackspaceDown)
            {
                state.BackspaceRepeatHeldTime += ctx.DeltaTime;
                state.BackspaceRepeatTimer -= ctx.DeltaTime;

                for (int i = 0; i < 8 && state.BackspaceRepeatTimer <= 0f; i++)
                {
                    float rate = GetKeyRepeatRate(state.BackspaceRepeatHeldTime);
                    state.BackspaceRepeatTimer += 1f / rate;

                    if (!ApplyBackspace(ref state, buffer, ref length))
                    {
                        state.BackspaceRepeatTimer = 0f;
                        break;
                    }

                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }
            else
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0f;
            }

            if (input.KeyDelete)
            {
                state.DeleteRepeatHeldTime = 0f;
                state.DeleteRepeatTimer = 0.45f;
                if (ApplyDelete(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }
            else if (input.KeyDeleteDown)
            {
                state.DeleteRepeatHeldTime += ctx.DeltaTime;
                state.DeleteRepeatTimer -= ctx.DeltaTime;

                for (int i = 0; i < 8 && state.DeleteRepeatTimer <= 0f; i++)
                {
                    float rate = GetKeyRepeatRate(state.DeleteRepeatHeldTime);
                    state.DeleteRepeatTimer += 1f / rate;

                    if (!ApplyDelete(ref state, buffer, ref length))
                    {
                        state.DeleteRepeatTimer = 0f;
                        break;
                    }

                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }
            else
            {
                state.DeleteRepeatHeldTime = 0f;
                state.DeleteRepeatTimer = 0f;
            }

            if (input.KeyLeft && state.CaretPos > 0)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos--;
                if (!input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }
            if (input.KeyRight && state.CaretPos < length)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos++;
                if (!input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }

            if (input.KeyHome)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = 0;
                if (!input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }
            if (input.KeyEnd)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = length;
                if (!input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }

            if (input.KeyCtrlA)
            {
                state.SelectionStart = 0;
                state.SelectionEnd = length;
                state.CaretPos = length;
                shouldScrollToCaret = true;
            }

            if (input.KeyEscape)
            {
                if (length > 0)
                {
                    length = 0;
                    state.CaretPos = 0;
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                    state.ScrollOffsetX = 0f;
                    changed = true;
                }
                else
                {
                    ctx.ClearFocus();
                    ctx.ClearActive();
                }
                ctx.ResetCaretBlink();
            }

            if (input.MousePressed && !hovered)
            {
                ctx.ClearFocus();
                ctx.ClearActive();
            }

            state.CaretPos = Math.Clamp(state.CaretPos, 0, length);
            state.ScrollOffsetX = ClampHorizontalScroll(ctx, buffer.Slice(0, length), style.FontSize, state.ScrollOffsetX, windowTextRect.Width);
            if (shouldScrollToCaret)
            {
                state.ScrollOffsetX = ScrollToCaretX(ctx, buffer.Slice(0, length), style.FontSize, state.CaretPos, state.ScrollOffsetX, windowTextRect.Width);
            }
        }

        state.CaretPos = Math.Clamp(state.CaretPos, 0, length);
        States[widgetId] = state;

        uint bgColor = isFocused ? style.Surface : (hovered ? style.Hover : style.Surface);
        uint borderColor = isFocused ? style.Primary : style.Border;
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, bgColor);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, borderColor, style.BorderWidth);

        // Search icon (magnifying glass)
        float iconX = rect.X + padding;
        float iconY = rect.Y + (height - iconSize) * 0.5f;
        uint iconColor = style.TextSecondary;

        float glassRadius = iconSize * 0.3f;
        float glassCenterX = iconX + iconSize * 0.4f;
        float glassCenterY = iconY + iconSize * 0.4f;
        Im.DrawCircleStroke(glassCenterX, glassCenterY, glassRadius, iconColor, 1.5f);

        float handleStartX = glassCenterX + glassRadius * 0.7f;
        float handleStartY = glassCenterY + glassRadius * 0.7f;
        float handleEndX = iconX + iconSize - 2f;
        float handleEndY = iconY + iconSize - 2f;
        Im.DrawLine(handleStartX, handleStartY, handleEndX, handleEndY, 1.5f, iconColor);

        float textX = x + iconSize + padding;
        float textY = y + (height - style.FontSize) * 0.5f;

        Im.PushClipRect(windowTextRect);
        float textDrawXWindow = textX - state.ScrollOffsetX;
        float textDrawX = windowTextRect.X - state.ScrollOffsetX;

        if (isFocused && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
        {
            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);

            float selStartX = textDrawX + MeasureWidth(ctx, buffer.Slice(0, selStart), style.FontSize);
            float selEndX = textDrawX + MeasureWidth(ctx, buffer.Slice(0, selEnd), style.FontSize);
            Im.DrawRect(selStartX, windowTextRect.Y + 2f, selEndX - selStartX, windowTextRect.Height - 4f, ImStyle.WithAlpha(style.Primary, 100));
        }

        if (length > 0)
        {
            Im.Text(buffer.Slice(0, length), textDrawXWindow, textY, style.TextPrimary);
        }
        else if (!isFocused && !string.IsNullOrEmpty(placeholder))
        {
            Im.Text(placeholder.AsSpan(), textX, textY, style.TextSecondary);
        }

        if (isFocused && ctx.CaretVisible)
        {
            float caretOffsetX = MeasureWidth(ctx, buffer.Slice(0, state.CaretPos), style.FontSize);
            float caretX = textDrawX + caretOffsetX;
            Im.DrawRect(caretX, rect.Y + 4f, 1f, height - 8f, style.TextPrimary);
        }

        Im.PopClipRect();

        if (length > 0)
        {
            uint clearColor = clearHovered ? style.TextPrimary : style.TextSecondary;
            float xOffset = clearButtonSize * 0.25f;

            Im.DrawLine(
                windowClearRect.X + xOffset, windowClearRect.Y + xOffset,
                windowClearRect.Right - xOffset, windowClearRect.Bottom - xOffset,
                1.5f, clearColor);
            Im.DrawLine(
                windowClearRect.Right - xOffset, windowClearRect.Y + xOffset,
                windowClearRect.X + xOffset, windowClearRect.Bottom - xOffset,
                1.5f, clearColor);
        }

        return changed;
    }

    private static float MeasureWidth(ImContext ctx, ReadOnlySpan<char> text, float fontSize)
    {
        return ImTextMetrics.MeasureWidth(ctx.Font, text, fontSize);
    }

    private static float ClampHorizontalScroll(ImContext ctx, ReadOnlySpan<char> text, float fontSize, float scrollOffsetX, float viewWidth)
    {
        float contentWidth = viewWidth - 4f;
        if (contentWidth <= 0f)
        {
            return 0f;
        }

        float textWidth = MeasureWidth(ctx, text, fontSize);
        float maxScroll = Math.Max(0f, textWidth - contentWidth);
        return Math.Clamp(scrollOffsetX, 0f, maxScroll);
    }

    private static float ScrollToCaretX(ImContext ctx, ReadOnlySpan<char> text, float fontSize, int caretPos, float scrollOffsetX, float viewWidth)
    {
        float contentWidth = viewWidth - 4f;
        if (contentWidth <= 0f)
        {
            return 0f;
        }

        float caretX = MeasureWidth(ctx, text.Slice(0, caretPos), fontSize);
        float left = scrollOffsetX;
        float right = scrollOffsetX + contentWidth;

        float margin = Math.Min(20f, contentWidth * 0.25f);
        float targetLeft = left;
        if (caretX < left + margin)
        {
            targetLeft = caretX - margin;
        }
        else if (caretX > right - margin)
        {
            targetLeft = caretX - contentWidth + margin;
        }

        return ClampHorizontalScroll(ctx, text, fontSize, targetLeft, viewWidth);
    }

    private static bool ApplyBackspace(ref SearchBoxState state, Span<char> buffer, ref int length)
    {
        if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
        {
            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
            ImTextEdit.DeleteRange(buffer, ref length, selStart, selEnd);
            state.CaretPos = selStart;
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
            return true;
        }

        if (state.CaretPos <= 0 || length <= 0)
        {
            return false;
        }

        ImTextEdit.DeleteRange(buffer, ref length, state.CaretPos - 1, state.CaretPos);
        state.CaretPos--;
        return true;
    }

    private static bool ApplyDelete(ref SearchBoxState state, Span<char> buffer, ref int length)
    {
        if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
        {
            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
            ImTextEdit.DeleteRange(buffer, ref length, selStart, selEnd);
            state.CaretPos = selStart;
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
            return true;
        }

        if (state.CaretPos >= length)
        {
            return false;
        }

        ImTextEdit.DeleteRange(buffer, ref length, state.CaretPos, state.CaretPos + 1);
        return true;
    }

    private static float GetKeyRepeatRate(float heldTime)
    {
        const float initialRate = 7f;
        const float maxRate = 38f;
        const float accelTime = 1.25f;

        float t = heldTime / accelTime;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;

        return initialRate + (maxRate - initialRate) * t;
    }
}
