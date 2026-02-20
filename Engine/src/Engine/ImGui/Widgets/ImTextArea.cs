using System;
using System.Collections.Generic;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.Text;

namespace DerpLib.ImGui.Widgets;

public static class ImTextArea
{
    [Flags]
    public enum ImTextAreaFlags
    {
        None = 0,
        NoBackground = 1 << 0,
        NoBorder = 1 << 1,
        NoRounding = 1 << 2,
        SingleLine = 1 << 3,
        NoText = 1 << 4,
        NoCaret = 1 << 5,
        NoSelection = 1 << 6,
    }

    private static readonly Dictionary<int, TextAreaState> States = new(16);
    private static readonly VisualLine[] VisualLinesScratch = new VisualLine[32768];

    private struct TextAreaState
    {
        public int CaretPos;
        public float ScrollY;
        public int SelectionStart;
        public int SelectionEnd;
        public float BackspaceRepeatHeldTime;
        public float BackspaceRepeatTimer;
        public float DeleteRepeatHeldTime;
        public float DeleteRepeatTimer;
        public bool WasCopyDown;
        public bool WasPasteDown;
        public bool WasCutDown;
        public VisualLine[]? VisualLines;
    }

    private struct VisualLine
    {
        public int Start;
        public int Length;
    }

    public static bool Draw(string id, Span<char> buffer, ref int length, int maxLength, float width, float height, bool wordWrap = true)
    {
        var rect = ImLayout.AllocateRect(width, height);
        return DrawAt(id, buffer, ref length, maxLength, rect.X, rect.Y, rect.Width, rect.Height, wordWrap);
    }

    public static bool Draw(string id, ImTextBuffer textBuffer, float width, float height, bool wordWrap = true)
    {
        var rect = ImLayout.AllocateRect(width, height);
        return DrawAt(id, textBuffer, rect.X, rect.Y, rect.Width, rect.Height, wordWrap);
    }

    public static float MeasureContentHeight(
        ReadOnlySpan<char> text,
        float width,
        bool wordWrap = true,
        float fontSizePx = -1f,
        float lineHeightPx = -1f,
        float letterSpacingPx = 0f,
        bool includeBorder = true)
    {
        return MeasureContentHeight(text, width, wordWrap, fontSizePx, lineHeightPx, letterSpacingPx, includeBorder, out _);
    }

    public static float MeasureContentHeight(
        ReadOnlySpan<char> text,
        float width,
        bool wordWrap,
        float fontSizePx,
        float lineHeightPx,
        float letterSpacingPx,
        bool includeBorder,
        out int visualLineCount)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        float fontSize = fontSizePx > 0f ? fontSizePx : style.FontSize;
        float lineHeight = lineHeightPx > 0f ? lineHeightPx : (fontSize + 4f);
        float borderInset = includeBorder ? 2f : 0f;
        float wrapWidth = Math.Max(0f, width - borderInset * 2f - style.Padding * 2f);
        visualLineCount = BuildVisualLines(ctx.Font, text, text.Length, fontSize, wordWrap, wrapWidth, letterSpacingPx, VisualLinesScratch);
        return visualLineCount * lineHeight + style.Padding * 2f + borderInset * 2f;
    }

    public static bool DrawAt(
        string id,
        Span<char> buffer,
        ref int length,
        int maxLength,
        float x,
        float y,
        float width,
        float height,
        bool wordWrap = true,
        float fontSizePx = -1f,
        ImTextAreaFlags flags = ImTextAreaFlags.None,
        float lineHeightPx = -1f,
        float letterSpacingPx = 0f,
        int alignX = 0,
        int alignY = 0,
        uint textColor = 0,
        uint selectionColor = 0,
        uint caretColor = 0)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        var input = ctx.Input;
        var mousePos = Im.MousePos;
        float fontSize = fontSizePx > 0f ? fontSizePx : style.FontSize;
        uint resolvedTextColor = textColor != 0 ? textColor : style.TextPrimary;
        uint resolvedCaretColor = caretColor != 0 ? caretColor : resolvedTextColor;
        uint resolvedSelectionColor = selectionColor != 0 ? selectionColor : ImStyle.WithAlpha(style.Primary, 100);

        int widgetId = ctx.GetId(id);
        ctx.RegisterFocusable(widgetId);

        var rect = new ImRect(x, y, width, height);

        if (!States.TryGetValue(widgetId, out var state))
        {
            state = new TextAreaState { CaretPos = length, ScrollY = 0f, SelectionStart = -1, SelectionEnd = -1 };
        }

        bool isFocused = ctx.IsFocused(widgetId);
        bool hovered = rect.Contains(mousePos);
        bool changed = false;

        if (hovered)
        {
            ctx.SetHot(widgetId);
        }

        float borderInset = (flags & ImTextAreaFlags.NoBorder) != 0 ? 0f : 2f;
        float viewHeight = height - borderInset * 2f;
        float lineHeight = lineHeightPx > 0f ? lineHeightPx : (fontSize + 4f);
        float contentHeight;
        bool showScrollbar;
        int visualLineCount = BuildVisualLinesForLayout(
            ctx.Font,
            buffer,
            length,
            fontSize,
            wordWrap,
            width,
            borderInset,
            style.Padding,
            style.ScrollbarWidth,
            viewHeight,
            lineHeight,
            letterSpacingPx,
            VisualLinesScratch,
            out contentHeight,
            out showScrollbar);

        float maxScroll = Math.Max(0f, contentHeight - viewHeight);

        bool shouldScrollToCaret = false;
        bool layoutDirty = false;

        float scrollbarWidth = showScrollbar ? style.ScrollbarWidth : 0f;
        var contentRect = new ImRect(
            x + borderInset,
            y + borderInset,
            width - borderInset * 2f - scrollbarWidth,
            height - borderInset * 2f);

        float yAlignOffset = 0f;
        if (alignY == 1 && contentHeight < viewHeight)
        {
            yAlignOffset = (viewHeight - contentHeight) * 0.5f;
        }
        else if (alignY == 2 && contentHeight < viewHeight)
        {
            yAlignOffset = viewHeight - contentHeight;
        }
        if (yAlignOffset < 0f)
        {
            yAlignOffset = 0f;
        }

        // Hit test / focus and caret placement
        if (hovered && input.MousePressed)
        {
            ctx.RequestFocus(widgetId);
            ctx.SetActive(widgetId);

            state.ScrollY = Math.Clamp(state.ScrollY, 0f, maxScroll);
            int lineIndex = GetVisualLineIndexFromMouseY(contentRect, style.Padding, yAlignOffset, state.ScrollY, mousePos.Y, lineHeight, visualLineCount);
            var clickedLine = VisualLinesScratch[lineIndex];

            float xAlignOffset = 0f;
            if (alignX != 0)
            {
                float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, buffer.Slice(clickedLine.Start, clickedLine.Length), fontSize, letterSpacingPx);
                xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
            }

            float textStartX = contentRect.X + style.Padding + xAlignOffset;
            float relativeX = mousePos.X - textStartX;
            int column = ImTextEdit.GetCaretPosFromRelativeX(ctx.Font, buffer.Slice(clickedLine.Start, clickedLine.Length), fontSize, letterSpacingPx, relativeX);
            state.CaretPos = clickedLine.Start + column;
            state.SelectionStart = state.CaretPos;
            state.SelectionEnd = state.CaretPos;
            ctx.ResetCaretBlink();
        }

        // Handle editing
        if (isFocused)
        {
            ctx.WantCaptureKeyboard = true;

            if (input.KeyCtrlC && !state.WasCopyDown)
            {
                CopySelectionToClipboard(ref state, buffer, length);
            }

            if (input.KeyCtrlX && !state.WasCutDown)
            {
                if (CutSelectionToClipboard(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
                    ctx.ResetCaretBlink();
                }
            }

            if (input.KeyCtrlV && !state.WasPasteDown)
            {
                if (PasteFromClipboard(ref state, buffer, ref length, maxLength))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
                    ctx.ResetCaretBlink();
                }
            }

            unsafe
            {
                for (int i = 0; i < input.InputCharCount; i++)
                {
                    char c = input.InputChars[i];
                    if (c >= 32 && length < maxLength)
                    {
                        if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
                        {
                            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
                            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
                            ImTextEdit.DeleteRange(buffer, ref length, selStart, selEnd);
                            state.CaretPos = selStart;
                            state.SelectionStart = -1;
                            state.SelectionEnd = -1;
                        }

                        ImTextEdit.InsertChar(buffer, ref length, maxLength, state.CaretPos, c);
                        state.CaretPos++;
                        changed = true;
                        shouldScrollToCaret = true;
                        layoutDirty = true;
                        ctx.ResetCaretBlink();
                    }
                }
            }

            if ((flags & ImTextAreaFlags.SingleLine) == 0 && input.KeyEnter && length < maxLength)
            {
                if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
                {
                    int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
                    int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
                    ImTextEdit.DeleteRange(buffer, ref length, selStart, selEnd);
                    state.CaretPos = selStart;
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }

                ImTextEdit.InsertChar(buffer, ref length, maxLength, state.CaretPos, '\n');
                state.CaretPos++;
                changed = true;
                shouldScrollToCaret = true;
                layoutDirty = true;
                ctx.ResetCaretBlink();
            }

            if (input.KeyBackspace)
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0.45f;
                if (ApplyBackspace(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
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
                    layoutDirty = true;
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
                    layoutDirty = true;
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
                    layoutDirty = true;
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
                int currentLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
                state.CaretPos = VisualLinesScratch[currentLineIndex].Start;
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
                int currentLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
                var currentLine = VisualLinesScratch[currentLineIndex];
                state.CaretPos = currentLine.Start + currentLine.Length;
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

            if (input.KeyUp)
                {
                    int caretBeforeMove = state.CaretPos;
                    state.CaretPos = GetPosOnPreviousVisualLine(ctx.Font, buffer, length, fontSize, letterSpacingPx, VisualLinesScratch, visualLineCount, state.CaretPos);
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
            if (input.KeyDown)
                {
                    int caretBeforeMove = state.CaretPos;
                    state.CaretPos = GetPosOnNextVisualLine(ctx.Font, buffer, length, fontSize, letterSpacingPx, VisualLinesScratch, visualLineCount, state.CaretPos);
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
                ctx.ClearFocus();
                ctx.ClearActive();
            }

            if (input.MousePressed && !hovered)
            {
                ctx.ClearFocus();
                ctx.ClearActive();
            }

            state.WasCopyDown = input.KeyCtrlC;
            state.WasPasteDown = input.KeyCtrlV;
            state.WasCutDown = input.KeyCtrlX;
        }
        else
        {
            state.WasCopyDown = false;
            state.WasPasteDown = false;
            state.WasCutDown = false;
        }

        state.CaretPos = Math.Clamp(state.CaretPos, 0, length);

        if (layoutDirty)
        {
            visualLineCount = BuildVisualLinesForLayout(
                ctx.Font,
                buffer,
                length,
                fontSize,
                wordWrap,
                width,
                borderInset,
                style.Padding,
                style.ScrollbarWidth,
                viewHeight,
                lineHeight,
                letterSpacingPx,
                VisualLinesScratch,
                out contentHeight,
                out showScrollbar);
            maxScroll = Math.Max(0f, contentHeight - viewHeight);
        }

        state.ScrollY = Math.Clamp(state.ScrollY, 0f, maxScroll);

        // Drag selection + optional auto-scroll while dragging
        if (ctx.IsActive(widgetId))
        {
            if (input.MouseDown)
            {
                float margin = Math.Min(24f, viewHeight * 0.25f);
                float topEdge = rect.Y + margin;
                float bottomEdge = rect.Bottom - margin;

                float mouseY = mousePos.Y;
                if (mouseY < topEdge)
                {
                    float overshoot = topEdge - mouseY;
                    state.ScrollY -= (240f + overshoot * 10f) * ctx.DeltaTime;
                }
                else if (mouseY > bottomEdge)
                {
                    float overshoot = mouseY - bottomEdge;
                    state.ScrollY += (240f + overshoot * 10f) * ctx.DeltaTime;
                }

                state.ScrollY = Math.Clamp(state.ScrollY, 0f, maxScroll);

                int lineIndex = GetVisualLineIndexFromMouseY(contentRect, style.Padding, yAlignOffset, state.ScrollY, mousePos.Y, lineHeight, visualLineCount);
                var dragLine = VisualLinesScratch[lineIndex];

                float xAlignOffset = 0f;
                if (alignX != 0 && dragLine.Length > 0)
                {
                    float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                    float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, buffer.Slice(dragLine.Start, dragLine.Length), fontSize, letterSpacingPx);
                    xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
                }

                float textStartX = contentRect.X + style.Padding + xAlignOffset;
                float relativeX = mousePos.X - textStartX;
                int column = ImTextEdit.GetCaretPosFromRelativeX(ctx.Font, buffer.Slice(dragLine.Start, dragLine.Length), fontSize, letterSpacingPx, relativeX);
                state.CaretPos = dragLine.Start + column;
                state.SelectionEnd = state.CaretPos;
            }
            else if (input.MouseReleased)
            {
                ctx.ClearActive();
            }
        }

        if (shouldScrollToCaret)
        {
            int caretLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
            float caretTop = style.Padding + caretLineIndex * lineHeight;
            float caretBottom = caretTop + lineHeight;

            float desiredScroll = state.ScrollY;
            float margin = 4f;
            if (caretTop < desiredScroll + margin)
            {
                desiredScroll = caretTop - margin;
            }
            else if (caretBottom > desiredScroll + viewHeight - margin)
            {
                desiredScroll = caretBottom - viewHeight + margin;
            }

            state.ScrollY = Math.Clamp(desiredScroll, 0f, maxScroll);
        }

        // Draw background
        float cornerRadius = (flags & ImTextAreaFlags.NoRounding) != 0 ? 0f : style.CornerRadius;
        uint bgColor = isFocused ? style.Surface : (hovered ? style.Hover : style.Surface);
        uint borderColor = isFocused ? style.Primary : style.Border;
        if ((flags & ImTextAreaFlags.NoBackground) == 0)
        {
            Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, cornerRadius, bgColor);
        }
        if ((flags & ImTextAreaFlags.NoBorder) == 0)
        {
            Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, cornerRadius, borderColor, style.BorderWidth);
        }

        // Content area with scrollbar
        showScrollbar = contentHeight > viewHeight;
        scrollbarWidth = showScrollbar ? style.ScrollbarWidth : 0f;

        contentRect = new ImRect(
            x + borderInset,
            y + borderInset,
            width - borderInset * 2f - scrollbarWidth,
            height - borderInset * 2f);
        var scrollbarRect = new ImRect(contentRect.Right, contentRect.Y, scrollbarWidth, contentRect.Height);

        float contentY = ImScrollView.Begin(contentRect, contentHeight, ref state.ScrollY, handleMouseWheel: true);

        float textX = contentRect.X + style.Padding;
        float textYBase = contentY + style.Padding + yAlignOffset;

        int firstVisibleLine = (int)Math.Floor((state.ScrollY - style.Padding) / lineHeight) - 1;
        if (firstVisibleLine < 0) firstVisibleLine = 0;
        int visibleLineCount = (int)Math.Ceiling((viewHeight + style.Padding * 2f) / lineHeight) + 2;
        int lastVisibleLineExclusive = firstVisibleLine + visibleLineCount;
        if (lastVisibleLineExclusive > visualLineCount) lastVisibleLineExclusive = visualLineCount;

        for (int lineIndex = firstVisibleLine; lineIndex < lastVisibleLineExclusive; lineIndex++)
        {
            var line = VisualLinesScratch[lineIndex];
            float lineWindowY = textYBase + lineIndex * lineHeight;

            if ((flags & ImTextAreaFlags.NoSelection) == 0 &&
                isFocused && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
            {
                int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
                int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
                int lineStart = line.Start;
                int lineEnd = line.Start + line.Length;
                int overlapStart = Math.Max(selStart, lineStart);
                int overlapEnd = Math.Min(selEnd, lineEnd);

                if (overlapEnd > overlapStart)
                {
                    float startXOffset = overlapStart > lineStart
                        ? ImTextMetrics.MeasureWidth(ctx.Font, buffer.Slice(lineStart, overlapStart - lineStart), fontSize, letterSpacingPx)
                        : 0f;
                    float endXOffset = overlapEnd > lineStart
                        ? ImTextMetrics.MeasureWidth(ctx.Font, buffer.Slice(lineStart, overlapEnd - lineStart), fontSize, letterSpacingPx)
                        : 0f;

                    float xAlignOffset = 0f;
                    if (alignX != 0)
                    {
                        float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                        float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, buffer.Slice(lineStart, line.Length), fontSize, letterSpacingPx);
                        xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
                    }

                    float highlightWindowX = textX + xAlignOffset + startXOffset;
                    float highlightWindowW = endXOffset - startXOffset;
                    Im.DrawRect(highlightWindowX, lineWindowY, highlightWindowW, lineHeight, resolvedSelectionColor);
                }
            }

            if ((flags & ImTextAreaFlags.NoText) == 0 && line.Length > 0)
            {
                float xAlignOffset = 0f;
                if (alignX != 0)
                {
                    float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                    float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, buffer.Slice(line.Start, line.Length), fontSize, letterSpacingPx);
                    xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
                }

                Im.Text(buffer.Slice(line.Start, line.Length), textX + xAlignOffset, lineWindowY, fontSize, resolvedTextColor);
            }
        }

        // Caret
        if ((flags & ImTextAreaFlags.NoCaret) == 0 && isFocused && ctx.CaretVisible)
        {
            int caretLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
            var caretLine = VisualLinesScratch[caretLineIndex];
            int caretColumn = state.CaretPos - caretLine.Start;
            if (caretColumn < 0) caretColumn = 0;
            if (caretColumn > caretLine.Length) caretColumn = caretLine.Length;

            float caretOffsetX = caretColumn > 0
                ? ImTextMetrics.MeasureWidth(ctx.Font, buffer.Slice(caretLine.Start, caretColumn), fontSize, letterSpacingPx)
                : 0f;

            float xAlignOffset = 0f;
            if (alignX != 0)
            {
                float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, buffer.Slice(caretLine.Start, caretLine.Length), fontSize, letterSpacingPx);
                xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
            }

            float caretWindowX = textX + xAlignOffset + caretOffsetX;
            float caretWindowY = textYBase + caretLineIndex * lineHeight;
            Im.DrawRect(caretWindowX, caretWindowY + 2f, 1f, lineHeight - 4f, resolvedCaretColor);
        }

        int scrollbarWidgetId = 0;
        if (showScrollbar)
        {
            ctx.PushId(widgetId);
            scrollbarWidgetId = ctx.GetId(0x54584153); // "TXAS"
            ctx.PopId();
        }

        ImScrollView.End(scrollbarWidgetId, scrollbarRect, contentRect.Height, contentHeight, ref state.ScrollY);

        States[widgetId] = state;
        return changed;
    }

    public static void ClearState(int widgetId)
    {
        States.Remove(widgetId);
    }

    /// <summary>
    /// Sets the caret position and selection for a widget. Creates state if it doesn't exist.
    /// </summary>
    public static void SetState(int widgetId, int caretPos, int selectionStart = -1, int selectionEnd = -1)
    {
        if (!States.TryGetValue(widgetId, out var state))
        {
            state = new TextAreaState { ScrollY = 0f };
        }
        state.CaretPos = caretPos;
        state.SelectionStart = selectionStart;
        state.SelectionEnd = selectionEnd;
        States[widgetId] = state;
    }

    public static bool TryGetState(int widgetId, out int caretPos, out int selectionStart, out int selectionEnd)
    {
        caretPos = 0;
        selectionStart = -1;
        selectionEnd = -1;

        if (!States.TryGetValue(widgetId, out TextAreaState state))
        {
            return false;
        }

        caretPos = state.CaretPos;
        selectionStart = state.SelectionStart;
        selectionEnd = state.SelectionEnd;
        return true;
    }

    public static bool DrawAt(
        string id,
        ImTextBuffer textBuffer,
        float x,
        float y,
        float width,
        float height,
        bool wordWrap = true,
        float fontSizePx = -1f,
        ImTextAreaFlags flags = ImTextAreaFlags.None,
        float lineHeightPx = -1f,
        float letterSpacingPx = 0f,
        int alignX = 0,
        int alignY = 0,
        uint textColor = 0,
        uint selectionColor = 0,
        uint caretColor = 0)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        var input = ctx.Input;
        var mousePos = Im.MousePos;
        uint resolvedTextColor = textColor != 0 ? textColor : style.TextPrimary;
        uint resolvedCaretColor = caretColor != 0 ? caretColor : resolvedTextColor;
        uint resolvedSelectionColor = selectionColor != 0 ? selectionColor : ImStyle.WithAlpha(style.Primary, 100);

        int widgetId = ctx.GetId(id);
        ctx.RegisterFocusable(widgetId);
        float fontSize = fontSizePx > 0f ? fontSizePx : style.FontSize;

        var rect = new ImRect(x, y, width, height);

        int length = textBuffer.Length;

        if (!States.TryGetValue(widgetId, out var state))
        {
            state = new TextAreaState { CaretPos = length, ScrollY = 0f, SelectionStart = -1, SelectionEnd = -1 };
        }

        bool isFocused = ctx.IsFocused(widgetId);
        bool hovered = rect.Contains(mousePos);
        bool changed = false;

        if (hovered)
        {
            ctx.SetHot(widgetId);
        }

        float borderInset = (flags & ImTextAreaFlags.NoBorder) != 0 ? 0f : 2f;
        float viewHeight = height - borderInset * 2f;
        float lineHeight = lineHeightPx > 0f ? lineHeightPx : (fontSize + 4f);
        float contentHeight;
        bool showScrollbar;

        VisualLine[] visualLines = state.VisualLines ?? VisualLinesScratch;
        int visualLineCount = BuildVisualLinesForLayoutWithGrowth(
            ctx.Font,
            textBuffer.AsSpan(),
            length,
            fontSize,
            wordWrap,
            width,
            borderInset,
            style.Padding,
            style.ScrollbarWidth,
            viewHeight,
            lineHeight,
            letterSpacingPx,
            ref state,
            ref visualLines,
            out contentHeight,
            out showScrollbar);

        float maxScroll = Math.Max(0f, contentHeight - viewHeight);

        bool shouldScrollToCaret = false;
        bool layoutDirty = false;

        float scrollbarWidth = showScrollbar ? style.ScrollbarWidth : 0f;
        var contentRect = new ImRect(
            x + borderInset,
            y + borderInset,
            width - borderInset * 2f - scrollbarWidth,
            height - borderInset * 2f);

        float yAlignOffset = 0f;
        if (alignY == 1 && contentHeight < viewHeight)
        {
            yAlignOffset = (viewHeight - contentHeight) * 0.5f;
        }
        else if (alignY == 2 && contentHeight < viewHeight)
        {
            yAlignOffset = viewHeight - contentHeight;
        }
        if (yAlignOffset < 0f)
        {
            yAlignOffset = 0f;
        }

        // Hit test / focus and caret placement
        if (hovered && input.MousePressed)
        {
            ctx.RequestFocus(widgetId);
            ctx.SetActive(widgetId);

            state.ScrollY = Math.Clamp(state.ScrollY, 0f, maxScroll);
            int lineIndex = GetVisualLineIndexFromMouseY(contentRect, style.Padding, yAlignOffset, state.ScrollY, mousePos.Y, lineHeight, visualLineCount);
            var clickedLine = visualLines[lineIndex];

            float xAlignOffset = 0f;
            if (alignX != 0)
            {
                float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, textBuffer.AsSpan().Slice(clickedLine.Start, clickedLine.Length), fontSize, letterSpacingPx);
                xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
            }

            float textStartX = contentRect.X + style.Padding + xAlignOffset;
            float relativeX = mousePos.X - textStartX;
            ReadOnlySpan<char> clickedText = textBuffer.AsSpan().Slice(clickedLine.Start, clickedLine.Length);
            int column = ImTextEdit.GetCaretPosFromRelativeX(ctx.Font, clickedText, fontSize, letterSpacingPx, relativeX);
            state.CaretPos = clickedLine.Start + column;
            state.SelectionStart = state.CaretPos;
            state.SelectionEnd = state.CaretPos;
            ctx.ResetCaretBlink();
        }

        // Handle editing
        if (isFocused)
        {
            ctx.WantCaptureKeyboard = true;

            if (input.KeyCtrlC && !state.WasCopyDown)
            {
                CopySelectionToClipboard(ref state, textBuffer.AsSpan(), length);
            }

            if (input.KeyCtrlX && !state.WasCutDown)
            {
                if (CutSelectionToClipboard(ref state, textBuffer))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
                    length = textBuffer.Length;
                    ctx.ResetCaretBlink();
                }
            }

            if (input.KeyCtrlV && !state.WasPasteDown)
            {
                if (PasteFromClipboard(ref state, textBuffer))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
                    length = textBuffer.Length;
                    ctx.ResetCaretBlink();
                }
            }

            unsafe
            {
                for (int i = 0; i < input.InputCharCount; i++)
                {
                    char c = input.InputChars[i];
                    if (c >= 32)
                    {
                        if (DeleteSelectionIfAny(ref state, textBuffer))
                        {
                            length = textBuffer.Length;
                        }

                        textBuffer.InsertChar(state.CaretPos, c);
                        state.CaretPos++;
                        changed = true;
                        shouldScrollToCaret = true;
                        layoutDirty = true;
                        length = textBuffer.Length;
                        ctx.ResetCaretBlink();
                    }
                }
            }

            if ((flags & ImTextAreaFlags.SingleLine) == 0 && input.KeyEnter)
            {
                if (DeleteSelectionIfAny(ref state, textBuffer))
                {
                    length = textBuffer.Length;
                }

                textBuffer.InsertChar(state.CaretPos, '\n');
                state.CaretPos++;
                changed = true;
                shouldScrollToCaret = true;
                layoutDirty = true;
                length = textBuffer.Length;
                ctx.ResetCaretBlink();
            }

            if (input.KeyBackspace)
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0.45f;
                if (ApplyBackspace(ref state, textBuffer))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
                    length = textBuffer.Length;
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

                    if (!ApplyBackspace(ref state, textBuffer))
                    {
                        state.BackspaceRepeatTimer = 0f;
                        break;
                    }

                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
                    length = textBuffer.Length;
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
                if (ApplyDelete(ref state, textBuffer))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
                    length = textBuffer.Length;
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

                    if (!ApplyDelete(ref state, textBuffer))
                    {
                        state.DeleteRepeatTimer = 0f;
                        break;
                    }

                    changed = true;
                    shouldScrollToCaret = true;
                    layoutDirty = true;
                    length = textBuffer.Length;
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
                int currentLineIndex = FindVisualLineIndex(textBuffer.AsSpan(), length, visualLines, visualLineCount, state.CaretPos);
                state.CaretPos = visualLines[currentLineIndex].Start;
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
                int currentLineIndex = FindVisualLineIndex(textBuffer.AsSpan(), length, visualLines, visualLineCount, state.CaretPos);
                var currentLine = visualLines[currentLineIndex];
                state.CaretPos = currentLine.Start + currentLine.Length;
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

            if (input.KeyUp)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = GetPosOnPreviousVisualLine(ctx.Font, textBuffer.AsSpan(), length, fontSize, letterSpacingPx, visualLines, visualLineCount, state.CaretPos);
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
            if (input.KeyDown)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = GetPosOnNextVisualLine(ctx.Font, textBuffer.AsSpan(), length, fontSize, letterSpacingPx, visualLines, visualLineCount, state.CaretPos);
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
                ctx.ClearFocus();
                ctx.ClearActive();
            }

            if (input.MousePressed && !hovered)
            {
                ctx.ClearFocus();
                ctx.ClearActive();
            }

            state.WasCopyDown = input.KeyCtrlC;
            state.WasPasteDown = input.KeyCtrlV;
            state.WasCutDown = input.KeyCtrlX;
        }
        else
        {
            state.WasCopyDown = false;
            state.WasPasteDown = false;
            state.WasCutDown = false;
        }

        state.CaretPos = Math.Clamp(state.CaretPos, 0, length);
        ClampSelection(ref state, length);

        if (layoutDirty)
        {
            length = textBuffer.Length;
            visualLineCount = BuildVisualLinesForLayoutWithGrowth(
                ctx.Font,
                textBuffer.AsSpan(),
                length,
                fontSize,
                wordWrap,
                width,
                borderInset,
                style.Padding,
                style.ScrollbarWidth,
                viewHeight,
                lineHeight,
                letterSpacingPx,
                ref state,
                ref visualLines,
                out contentHeight,
                out showScrollbar);
            maxScroll = Math.Max(0f, contentHeight - viewHeight);
        }

        state.ScrollY = Math.Clamp(state.ScrollY, 0f, maxScroll);

        ReadOnlySpan<char> text = textBuffer.AsSpan();

        // Drag selection + optional auto-scroll while dragging
        if (ctx.IsActive(widgetId))
        {
            if (input.MouseDown)
            {
                float margin = Math.Min(24f, viewHeight * 0.25f);
                float topEdge = rect.Y + margin;
                float bottomEdge = rect.Bottom - margin;

                float mouseY = mousePos.Y;
                if (mouseY < topEdge)
                {
                    float overshoot = topEdge - mouseY;
                    state.ScrollY -= (240f + overshoot * 10f) * ctx.DeltaTime;
                }
                else if (mouseY > bottomEdge)
                {
                    float overshoot = mouseY - bottomEdge;
                    state.ScrollY += (240f + overshoot * 10f) * ctx.DeltaTime;
                }

                state.ScrollY = Math.Clamp(state.ScrollY, 0f, maxScroll);

                int lineIndex = GetVisualLineIndexFromMouseY(contentRect, style.Padding, yAlignOffset, state.ScrollY, mousePos.Y, lineHeight, visualLineCount);
                var dragLine = visualLines[lineIndex];

                float xAlignOffset = 0f;
                if (alignX != 0 && dragLine.Length > 0)
                {
                    float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                    float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, text.Slice(dragLine.Start, dragLine.Length), fontSize, letterSpacingPx);
                    xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
                }

                float textStartX = contentRect.X + style.Padding + xAlignOffset;
                float relativeX = mousePos.X - textStartX;
                int column = ImTextEdit.GetCaretPosFromRelativeX(ctx.Font, text.Slice(dragLine.Start, dragLine.Length), fontSize, letterSpacingPx, relativeX);
                state.CaretPos = dragLine.Start + column;
                state.SelectionEnd = state.CaretPos;
            }
            else if (input.MouseReleased)
            {
                ctx.ClearActive();
            }
        }

        if (shouldScrollToCaret)
        {
            int caretLineIndex = FindVisualLineIndex(text, length, visualLines, visualLineCount, state.CaretPos);
            float caretTop = style.Padding + caretLineIndex * lineHeight;
            float caretBottom = caretTop + lineHeight;

            float desiredScroll = state.ScrollY;
            float margin = 4f;
            if (caretTop < desiredScroll + margin)
            {
                desiredScroll = caretTop - margin;
            }
            else if (caretBottom > desiredScroll + viewHeight - margin)
            {
                desiredScroll = caretBottom - viewHeight + margin;
            }

            state.ScrollY = Math.Clamp(desiredScroll, 0f, maxScroll);
        }

        // Draw background
        float cornerRadius = (flags & ImTextAreaFlags.NoRounding) != 0 ? 0f : style.CornerRadius;
        uint bgColor = isFocused ? style.Surface : (hovered ? style.Hover : style.Surface);
        uint borderColor = isFocused ? style.Primary : style.Border;
        if ((flags & ImTextAreaFlags.NoBackground) == 0)
        {
            Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, cornerRadius, bgColor);
        }
        if ((flags & ImTextAreaFlags.NoBorder) == 0)
        {
            Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, cornerRadius, borderColor, style.BorderWidth);
        }

        // Content area with scrollbar
        showScrollbar = contentHeight > viewHeight;
        scrollbarWidth = showScrollbar ? style.ScrollbarWidth : 0f;

        contentRect = new ImRect(
            x + borderInset,
            y + borderInset,
            width - borderInset * 2f - scrollbarWidth,
            height - borderInset * 2f);
        var scrollbarRect = new ImRect(contentRect.Right, contentRect.Y, scrollbarWidth, contentRect.Height);

        float contentY = ImScrollView.Begin(contentRect, contentHeight, ref state.ScrollY, handleMouseWheel: true);

        float textX = contentRect.X + style.Padding;
        float textYBase = contentY + style.Padding + yAlignOffset;

        int firstVisibleLine = (int)Math.Floor((state.ScrollY - style.Padding) / lineHeight) - 1;
        if (firstVisibleLine < 0) firstVisibleLine = 0;
        int visibleLineCount = (int)Math.Ceiling((viewHeight + style.Padding * 2f) / lineHeight) + 2;
        int lastVisibleLineExclusive = firstVisibleLine + visibleLineCount;
        if (lastVisibleLineExclusive > visualLineCount) lastVisibleLineExclusive = visualLineCount;

        for (int lineIndex = firstVisibleLine; lineIndex < lastVisibleLineExclusive; lineIndex++)
        {
            var line = visualLines[lineIndex];
            float lineWindowY = textYBase + lineIndex * lineHeight;

            if ((flags & ImTextAreaFlags.NoSelection) == 0 &&
                isFocused && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
            {
                int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
                int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
                int lineStart = line.Start;
                int lineEnd = line.Start + line.Length;
                int overlapStart = Math.Max(selStart, lineStart);
                int overlapEnd = Math.Min(selEnd, lineEnd);

                if (overlapEnd > overlapStart)
                {
                    float startXOffset = overlapStart > lineStart
                        ? ImTextMetrics.MeasureWidth(ctx.Font, text.Slice(lineStart, overlapStart - lineStart), fontSize, letterSpacingPx)
                        : 0f;
                    float endXOffset = overlapEnd > lineStart
                        ? ImTextMetrics.MeasureWidth(ctx.Font, text.Slice(lineStart, overlapEnd - lineStart), fontSize, letterSpacingPx)
                        : 0f;
	
                    float xAlignOffset = 0f;
                    if (alignX != 0)
                    {
                        float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                        float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, text.Slice(lineStart, line.Length), fontSize, letterSpacingPx);
                        xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
                    }

                    float highlightWindowX = textX + xAlignOffset + startXOffset;
                    float highlightWindowW = endXOffset - startXOffset;
                    Im.DrawRect(highlightWindowX, lineWindowY, highlightWindowW, lineHeight, resolvedSelectionColor);
                }
            }

            if ((flags & ImTextAreaFlags.NoText) == 0 && line.Length > 0)
            {
                float xAlignOffset = 0f;
                if (alignX != 0)
                {
                    float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                    float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, text.Slice(line.Start, line.Length), fontSize, letterSpacingPx);
                    xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
                }

                Im.Text(text.Slice(line.Start, line.Length), textX + xAlignOffset, lineWindowY, fontSize, resolvedTextColor);
            }
        }

        // Caret
        if ((flags & ImTextAreaFlags.NoCaret) == 0 && isFocused && ctx.CaretVisible)
        {
            int caretLineIndex = FindVisualLineIndex(text, length, visualLines, visualLineCount, state.CaretPos);
            var caretLine = visualLines[caretLineIndex];
            int caretColumn = state.CaretPos - caretLine.Start;
            if (caretColumn < 0) caretColumn = 0;
            if (caretColumn > caretLine.Length) caretColumn = caretLine.Length;

            float caretOffsetX = caretColumn > 0
                ? ImTextMetrics.MeasureWidth(ctx.Font, text.Slice(caretLine.Start, caretColumn), fontSize, letterSpacingPx)
                : 0f;

            float xAlignOffset = 0f;
            if (alignX != 0)
            {
                float innerWidth = Math.Max(0f, contentRect.Width - style.Padding * 2f);
                float lineWidth = ImTextMetrics.MeasureWidth(ctx.Font, text.Slice(caretLine.Start, caretLine.Length), fontSize, letterSpacingPx);
                xAlignOffset = GetXAlignOffset(alignX, innerWidth, lineWidth);
            }

            float caretWindowX = textX + xAlignOffset + caretOffsetX;
            float caretWindowY = textYBase + caretLineIndex * lineHeight;
            Im.DrawRect(caretWindowX, caretWindowY + 2f, 1f, lineHeight - 4f, resolvedCaretColor);
        }

        int scrollbarWidgetId = 0;
        if (showScrollbar)
        {
            ctx.PushId(widgetId);
            scrollbarWidgetId = ctx.GetId(0x54584153); // "TXAS"
            ctx.PopId();
        }

        ImScrollView.End(scrollbarWidgetId, scrollbarRect, contentRect.Height, contentHeight, ref state.ScrollY);

        state.VisualLines = visualLines == VisualLinesScratch ? state.VisualLines : visualLines;
        States[widgetId] = state;
        return changed;
    }

    private static bool ApplyBackspace(ref TextAreaState state, Span<char> buffer, ref int length)
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

    private static bool ApplyBackspace(ref TextAreaState state, ImTextBuffer textBuffer)
    {
        if (DeleteSelectionIfAny(ref state, textBuffer))
        {
            return true;
        }

        if (state.CaretPos <= 0 || textBuffer.Length <= 0)
        {
            return false;
        }

        textBuffer.DeleteRange(state.CaretPos - 1, state.CaretPos);
        state.CaretPos--;
        return true;
    }

    private static bool ApplyDelete(ref TextAreaState state, Span<char> buffer, ref int length)
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

    private static bool ApplyDelete(ref TextAreaState state, ImTextBuffer textBuffer)
    {
        if (DeleteSelectionIfAny(ref state, textBuffer))
        {
            return true;
        }

        if (state.CaretPos >= textBuffer.Length)
        {
            return false;
        }

        textBuffer.DeleteRange(state.CaretPos, state.CaretPos + 1);
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

    private static int GetVisualLineIndexFromMouseY(ImRect viewportRect, float padding, float yAlignOffset, float scrollY, float mouseY, float lineHeight, int visualLineCount)
    {
        if (visualLineCount <= 1)
        {
            return 0;
        }

        float relativeY = (mouseY - (viewportRect.Y + padding + yAlignOffset)) + scrollY;
        if (relativeY < 0f)
        {
            return 0;
        }

        int lineIndex = (int)(relativeY / lineHeight);
        if (lineIndex < 0) lineIndex = 0;
        if (lineIndex >= visualLineCount) lineIndex = visualLineCount - 1;
        return lineIndex;
    }

    private static float GetXAlignOffset(int alignX, float innerWidth, float lineWidth)
    {
        if (innerWidth <= 0f)
        {
            return 0f;
        }

        float remaining = innerWidth - lineWidth;
        if (remaining <= 0f)
        {
            return 0f;
        }

        if (alignX == 1)
        {
            return remaining * 0.5f;
        }
        if (alignX == 2)
        {
            return remaining;
        }

        return 0f;
    }

    private static int FindVisualLineIndex(ReadOnlySpan<char> buffer, int length, VisualLine[] lines, int lineCount, int caretPos)
    {
        if (lineCount <= 1)
        {
            return 0;
        }

        int pos = Math.Clamp(caretPos, 0, length);
        int lo = 0;
        int hi = lineCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var line = lines[mid];
            int start = line.Start;
            int end = start + line.Length;

            if (pos < start)
            {
                hi = mid - 1;
                continue;
            }

            if (pos > end)
            {
                lo = mid + 1;
                continue;
            }

            if (pos < end)
            {
                return mid;
            }

            // pos == end
            if (end >= length || buffer[end] == '\n')
            {
                return mid;
            }

            lo = mid + 1;
        }

        return Math.Clamp(lo, 0, lineCount - 1);
    }

    private static int GetPosOnPreviousVisualLine(Font? font, ReadOnlySpan<char> buffer, int length, float fontSize, float letterSpacingPx, VisualLine[] lines, int lineCount, int caretPos)
    {
        if (lineCount <= 1)
        {
            return caretPos;
        }

        int currentLineIndex = FindVisualLineIndex(buffer, length, lines, lineCount, caretPos);
        if (currentLineIndex <= 0)
        {
            return caretPos;
        }

        var currentLine = lines[currentLineIndex];
        int currentColumn = Math.Clamp(caretPos - currentLine.Start, 0, currentLine.Length);
        float desiredX = currentColumn > 0 ? ImTextMetrics.MeasureWidth(font, buffer.Slice(currentLine.Start, currentColumn), fontSize, letterSpacingPx) : 0f;

        var previousLine = lines[currentLineIndex - 1];
        int newColumn = ImTextEdit.GetCaretPosFromRelativeX(font, buffer.Slice(previousLine.Start, previousLine.Length), fontSize, letterSpacingPx, desiredX);
        return previousLine.Start + newColumn;
    }

    private static int GetPosOnNextVisualLine(Font? font, ReadOnlySpan<char> buffer, int length, float fontSize, float letterSpacingPx, VisualLine[] lines, int lineCount, int caretPos)
    {
        if (lineCount <= 1)
        {
            return caretPos;
        }

        int currentLineIndex = FindVisualLineIndex(buffer, length, lines, lineCount, caretPos);
        if (currentLineIndex >= lineCount - 1)
        {
            return caretPos;
        }

        var currentLine = lines[currentLineIndex];
        int currentColumn = Math.Clamp(caretPos - currentLine.Start, 0, currentLine.Length);
        float desiredX = currentColumn > 0 ? ImTextMetrics.MeasureWidth(font, buffer.Slice(currentLine.Start, currentColumn), fontSize, letterSpacingPx) : 0f;

        var nextLine = lines[currentLineIndex + 1];
        int newColumn = ImTextEdit.GetCaretPosFromRelativeX(font, buffer.Slice(nextLine.Start, nextLine.Length), fontSize, letterSpacingPx, desiredX);
        return nextLine.Start + newColumn;
    }

    private static float GetAdvanceX(Font? font, float scale, float fallbackAdvanceX, char c)
    {
        if (c == '\t')
        {
            return fallbackAdvanceX * 4f;
        }

        if (font == null)
        {
            return fallbackAdvanceX;
        }

        if (font.TryGetGlyph(c, out var glyph))
        {
            return glyph.AdvanceX * scale;
        }

        if (font.TryGetGlyph(' ', out var spaceGlyph))
        {
            return spaceGlyph.AdvanceX * scale;
        }

        return 0f;
    }

    private static int BuildVisualLinesForLayout(
        Font? font,
        ReadOnlySpan<char> buffer,
        int length,
        float fontSize,
        bool wordWrap,
        float width,
        float borderInset,
        float padding,
        float scrollbarWidth,
        float viewHeight,
        float lineHeight,
        float letterSpacingPx,
        VisualLine[] output,
        out float contentHeight,
        out bool showScrollbar)
    {
        float wrapWidthNoScrollbar = Math.Max(0f, width - borderInset * 2f - padding * 2f);
        int lineCount = BuildVisualLines(font, buffer, length, fontSize, wordWrap, wrapWidthNoScrollbar, letterSpacingPx, output);
        contentHeight = lineCount * lineHeight + padding * 2f;
        showScrollbar = contentHeight > viewHeight;

        if (wordWrap && showScrollbar)
        {
            float wrapWidthWithScrollbar = Math.Max(0f, width - borderInset * 2f - scrollbarWidth - padding * 2f);
            lineCount = BuildVisualLines(font, buffer, length, fontSize, wordWrap, wrapWidthWithScrollbar, letterSpacingPx, output);
            contentHeight = lineCount * lineHeight + padding * 2f;
            showScrollbar = contentHeight > viewHeight;
        }

        return lineCount;
    }

    private static int BuildVisualLines(
        Font? font,
        ReadOnlySpan<char> buffer,
        int length,
        float fontSize,
        bool wordWrap,
        float wrapWidth,
        float letterSpacingPx,
        VisualLine[] output)
    {
        int count = 0;
        int lineStart = 0;

        float fallbackAdvanceX = font != null && font.TryGetGlyph(' ', out var spaceGlyph)
            ? spaceGlyph.AdvanceX * (fontSize / font.BaseSizePixels)
            : 7f;

        float scale = font != null ? (fontSize / font.BaseSizePixels) : 1f;
        float currentWidth = 0f;
        int lastBreakIndex = -1;

        float effectiveWrapWidth = wordWrap ? Math.Max(1f, wrapWidth) : float.PositiveInfinity;

        for (int i = 0; i < length; i++)
        {
            char c = buffer[i];
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                if (count >= output.Length)
                {
                    return output.Length;
                }

                output[count++] = new VisualLine { Start = lineStart, Length = i - lineStart };
                lineStart = i + 1;
                currentWidth = 0f;
                lastBreakIndex = -1;
                continue;
            }

            float advanceX = GetAdvanceX(font, scale, fallbackAdvanceX, c) + letterSpacingPx;

            if (wordWrap && currentWidth + advanceX > effectiveWrapWidth && i > lineStart)
            {
                int wrapEnd = i;
                if (lastBreakIndex >= lineStart)
                {
                    wrapEnd = (lastBreakIndex == i) ? i : (lastBreakIndex + 1);
                }

                if (wrapEnd <= lineStart)
                {
                    wrapEnd = i;
                }

                if (count >= output.Length)
                {
                    return output.Length;
                }

                output[count++] = new VisualLine { Start = lineStart, Length = wrapEnd - lineStart };
                lineStart = wrapEnd;
                currentWidth = 0f;
                lastBreakIndex = -1;
                i = lineStart - 1;
                continue;
            }

            currentWidth += advanceX;

            if (c == ' ' || c == '\t')
            {
                lastBreakIndex = i;
            }
        }

        if (count < output.Length)
        {
            output[count++] = new VisualLine { Start = lineStart, Length = length - lineStart };
        }

        if (count == 0)
        {
            output[0] = new VisualLine { Start = 0, Length = 0 };
            count = 1;
        }

        return count;
    }

    private static int BuildVisualLinesForLayoutWithGrowth(
        Font? font,
        ReadOnlySpan<char> buffer,
        int length,
        float fontSize,
        bool wordWrap,
        float width,
        float borderInset,
        float padding,
        float scrollbarWidth,
        float viewHeight,
        float lineHeight,
        float letterSpacingPx,
        ref TextAreaState state,
        ref VisualLine[] visualLines,
        out float contentHeight,
        out bool showScrollbar)
    {
        const int maxVisualLines = 262144;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            int lineCount = BuildVisualLinesForLayout(
                font,
                buffer,
                length,
                fontSize,
                wordWrap,
                width,
                borderInset,
                padding,
                scrollbarWidth,
                viewHeight,
                lineHeight,
                letterSpacingPx,
                visualLines,
                out contentHeight,
                out showScrollbar);

            if (lineCount < visualLines.Length)
            {
                state.VisualLines = visualLines == VisualLinesScratch ? state.VisualLines : visualLines;
                return lineCount;
            }

            var last = visualLines[visualLines.Length - 1];
            int lastEnd = last.Start + last.Length;
            if (lastEnd >= length)
            {
                state.VisualLines = visualLines == VisualLinesScratch ? state.VisualLines : visualLines;
                return lineCount;
            }

            int newCapacity = visualLines.Length < 1024 ? 1024 : visualLines.Length * 2;
            if (newCapacity > maxVisualLines)
            {
                state.VisualLines = visualLines == VisualLinesScratch ? state.VisualLines : visualLines;
                return lineCount;
            }

            visualLines = new VisualLine[newCapacity];
        }

        int fallbackCount = BuildVisualLinesForLayout(
            font,
            buffer,
            length,
            fontSize,
            wordWrap,
            width,
            borderInset,
            padding,
            scrollbarWidth,
            viewHeight,
            lineHeight,
            letterSpacingPx,
            visualLines,
            out contentHeight,
            out showScrollbar);
        state.VisualLines = visualLines == VisualLinesScratch ? state.VisualLines : visualLines;
        return fallbackCount;
    }

    private static void ClampSelection(ref TextAreaState state, int length)
    {
        if (state.SelectionStart >= 0)
        {
            state.SelectionStart = Math.Clamp(state.SelectionStart, 0, length);
        }

        if (state.SelectionEnd >= 0)
        {
            state.SelectionEnd = Math.Clamp(state.SelectionEnd, 0, length);
        }
    }

    private static bool DeleteSelectionIfAny(ref TextAreaState state, ImTextBuffer textBuffer)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd)
        {
            return false;
        }

        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
        selStart = Math.Clamp(selStart, 0, textBuffer.Length);
        selEnd = Math.Clamp(selEnd, 0, textBuffer.Length);

        if (selEnd <= selStart)
        {
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
            state.CaretPos = Math.Clamp(state.CaretPos, 0, textBuffer.Length);
            return false;
        }

        textBuffer.DeleteRange(selStart, selEnd);
        state.CaretPos = selStart;
        state.SelectionStart = -1;
        state.SelectionEnd = -1;
        return true;
    }

    private static void CopySelectionToClipboard(ref TextAreaState state, Span<char> buffer, int length)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd)
        {
            return;
        }

        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
        selStart = Math.Clamp(selStart, 0, length);
        selEnd = Math.Clamp(selEnd, 0, length);

        if (selEnd <= selStart)
        {
            return;
        }

        string selectedText = new string(buffer.Slice(selStart, selEnd - selStart));
        DerpLib.Derp.SetClipboardText(selectedText);
    }

    private static void CopySelectionToClipboard(ref TextAreaState state, ReadOnlySpan<char> buffer, int length)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd)
        {
            return;
        }

        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
        selStart = Math.Clamp(selStart, 0, length);
        selEnd = Math.Clamp(selEnd, 0, length);

        if (selEnd <= selStart)
        {
            return;
        }

        string selectedText = new string(buffer.Slice(selStart, selEnd - selStart));
        DerpLib.Derp.SetClipboardText(selectedText);
    }

    private static bool CutSelectionToClipboard(ref TextAreaState state, Span<char> buffer, ref int length)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd)
        {
            return false;
        }

        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
        selStart = Math.Clamp(selStart, 0, length);
        selEnd = Math.Clamp(selEnd, 0, length);

        if (selEnd <= selStart)
        {
            return false;
        }

        string selectedText = new string(buffer.Slice(selStart, selEnd - selStart));
        DerpLib.Derp.SetClipboardText(selectedText);

        ImTextEdit.DeleteRange(buffer, ref length, selStart, selEnd);
        state.CaretPos = selStart;
        state.SelectionStart = -1;
        state.SelectionEnd = -1;
        return true;
    }

    private static bool CutSelectionToClipboard(ref TextAreaState state, ImTextBuffer textBuffer)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd)
        {
            return false;
        }

        int length = textBuffer.Length;
        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
        selStart = Math.Clamp(selStart, 0, length);
        selEnd = Math.Clamp(selEnd, 0, length);

        if (selEnd <= selStart)
        {
            return false;
        }

        string selectedText = new string(textBuffer.AsSpan().Slice(selStart, selEnd - selStart));
        DerpLib.Derp.SetClipboardText(selectedText);

        textBuffer.DeleteRange(selStart, selEnd);
        state.CaretPos = selStart;
        state.SelectionStart = -1;
        state.SelectionEnd = -1;
        return true;
    }

    private static bool PasteFromClipboard(ref TextAreaState state, Span<char> buffer, ref int length, int maxLength)
    {
        string? clipboard = DerpLib.Derp.GetClipboardText();
        if (string.IsNullOrEmpty(clipboard))
        {
            return false;
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

        ReadOnlySpan<char> text = clipboard.AsSpan();
        bool insertedAny = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                continue;
            }

            if (length >= maxLength)
            {
                break;
            }

            ImTextEdit.InsertChar(buffer, ref length, maxLength, state.CaretPos, c);
            state.CaretPos++;
            insertedAny = true;
        }

        return insertedAny;
    }

    private static bool PasteFromClipboard(ref TextAreaState state, ImTextBuffer textBuffer)
    {
        string? clipboard = DerpLib.Derp.GetClipboardText();
        if (string.IsNullOrEmpty(clipboard))
        {
            return false;
        }

        if (DeleteSelectionIfAny(ref state, textBuffer))
        {
            state.CaretPos = Math.Clamp(state.CaretPos, 0, textBuffer.Length);
        }

        ReadOnlySpan<char> text = clipboard.AsSpan();
        int insertedCount = 0;

        int segmentStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\r')
            {
                continue;
            }

            int segmentLength = i - segmentStart;
            if (segmentLength > 0)
            {
                textBuffer.InsertText(state.CaretPos, text.Slice(segmentStart, segmentLength));
                state.CaretPos += segmentLength;
                insertedCount += segmentLength;
            }

            segmentStart = i + 1;
        }

        int tailLength = text.Length - segmentStart;
        if (tailLength > 0)
        {
            textBuffer.InsertText(state.CaretPos, text.Slice(segmentStart, tailLength));
            state.CaretPos += tailLength;
            insertedCount += tailLength;
        }

        return insertedCount > 0;
    }
}
