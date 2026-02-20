using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.Text;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

/// <summary>
/// Custom rich text editor widget. Handles text editing (caret, selection, keyboard, clipboard)
/// and renders text with per-span styling (colors, backgrounds, decorations).
/// Adapted from DerpLib.ImGui.Widgets.ImTextArea with rich text rendering replacing single-color text.
/// </summary>
internal static class ImRichTextArea
{
    private static readonly Dictionary<int, TextAreaState> States = new(16);
    private static RichTextLayout.VisualLine[] VisualLinesScratch = new RichTextLayout.VisualLine[512];
    private static RichTextLayout.StyledSegment[] StyledSegmentsScratch = new RichTextLayout.StyledSegment[256];
    private static int _lastPrunedFrame;
    private static readonly List<int> PruneKeysScratch = new(64);

    // Triple-click detection
    private static float _lastDoubleClickTime;
    private static float _lastDoubleClickMouseX;
    private static float _lastDoubleClickMouseY;

    // Rich text clipboard cache
    private static string? _richClipboardText;
    private static List<RichSpan>? _richClipboardSpans;

    /// <summary>True when Up arrow was pressed but caret was already on the first visual line.</summary>
    public static bool NavigatedPastStart { get; private set; }
    /// <summary>True when Down arrow was pressed but caret was already on the last visual line.</summary>
    public static bool NavigatedPastEnd { get; private set; }
    /// <summary>Set when Ctrl+B/I/U is pressed with a selection. Caller should issue ToggleSpan command.</summary>
    public static RichSpanStyle? PendingStyleToggle { get; private set; }

    private struct TextAreaState
    {
        public int CaretPos;
        public int SelectionStart;
        public int SelectionEnd;
        public int LastSeenFrame;
        public float BackspaceRepeatHeldTime;
        public float BackspaceRepeatTimer;
        public float DeleteRepeatHeldTime;
        public float DeleteRepeatTimer;
        public bool WasCopyDown;
        public bool WasPasteDown;
        public bool WasCutDown;
    }

    // =====================================================================
    //  Public API
    // =====================================================================

    public static bool DrawAt(
        string id,
        Span<char> buffer,
        ref int length,
        int maxLength,
        List<RichSpan> spans,
        float x, float y, float width, float height,
        bool singleLine,
        float fontSize,
        uint defaultTextColor,
        bool wordWrap = true)
    {
        NavigatedPastStart = false;
        NavigatedPastEnd = false;
        PendingStyleToggle = null;

        var ctx = Im.Context;
        var style = Im.Style;
        var input = ctx.Input;
        var mousePos = Im.MousePos;
        float lineHeight = fontSize * 1.4f;
        uint selectionColor = ImStyle.WithAlpha(style.Primary, 100);

        int widgetId = ctx.GetId(id);
        ctx.RegisterFocusable(widgetId);

        var rect = new ImRect(x, y, width, height);

        EnsureVisualLineCapacity(length + 1);

        if (!States.TryGetValue(widgetId, out var state))
            state = new TextAreaState { CaretPos = length, SelectionStart = -1, SelectionEnd = -1, LastSeenFrame = ctx.FrameCount };

        bool isFocused = ctx.IsFocused(widgetId);
        bool pointerCapturedByOverlay =
            !ctx.InOverlayScope &&
            ctx.OverlayCaptureMouse &&
            ctx.OverlayCaptureRect.Width > 0f &&
            ctx.OverlayCaptureRect.Height > 0f &&
            ctx.OverlayCaptureRect.Contains(input.MousePos);
        bool hovered = rect.Contains(mousePos);
        bool changed = false;

        if (hovered && !pointerCapturedByOverlay)
            ctx.SetHot(widgetId);

        // Build visual lines
        float wrapWidth = Math.Max(1f, width);
        int visualLineCount = RichTextLayout.BuildVisualLines(ctx.Font, buffer, length, fontSize, wordWrap, wrapWidth, 0f, VisualLinesScratch);

        bool layoutDirty = false;

        // Mouse click → caret placement / double-click word / triple-click select all
        if (hovered && input.MousePressed && !pointerCapturedByOverlay)
        {
            ctx.RequestFocus(widgetId);
            ctx.SetActive(widgetId);

            int lineIndex = GetVisualLineIndexFromMouseY(y, mousePos.Y, lineHeight, visualLineCount);
            var clickedLine = VisualLinesScratch[lineIndex];
            float relativeX = mousePos.X - x;
            int column = GetCaretPosFromRelativeX(ctx.Font,
                buffer.Slice(clickedLine.Start, clickedLine.Length), fontSize, relativeX);
            int clickPos = clickedLine.Start + column;

            // Triple-click: select all text in block
            float tdx = mousePos.X - _lastDoubleClickMouseX;
            float tdy = mousePos.Y - _lastDoubleClickMouseY;
            bool isTripleClick = _lastDoubleClickTime > 0
                && input.Time - _lastDoubleClickTime < 0.4f
                && tdx * tdx + tdy * tdy < 25f;

            if (isTripleClick && length > 0)
            {
                state.SelectionStart = 0;
                state.SelectionEnd = length;
                state.CaretPos = length;
                _lastDoubleClickTime = 0;
            }
            else if (input.IsDoubleClick && length > 0)
            {
                // Double-click: select the word at click position
                var (wordStart, wordEnd) = FindWordAtPos(buffer, length, clickPos);
                state.SelectionStart = wordStart;
                state.SelectionEnd = wordEnd;
                state.CaretPos = wordEnd;
                // Record for triple-click detection
                _lastDoubleClickTime = input.Time;
                _lastDoubleClickMouseX = mousePos.X;
                _lastDoubleClickMouseY = mousePos.Y;
            }
            else
            {
                state.CaretPos = clickPos;
                state.SelectionStart = state.CaretPos;
                state.SelectionEnd = state.CaretPos;
                _lastDoubleClickTime = 0;
            }
            ctx.ResetCaretBlink();
        }

        // Keyboard input when focused
        if (isFocused)
        {
            ctx.WantCaptureKeyboard = true;

            // Ctrl+C
            if (input.KeyCtrlC && !state.WasCopyDown)
                CopySelectionToClipboard(ref state, buffer, length, spans);

            // Ctrl+X
            if (input.KeyCtrlX && !state.WasCutDown)
            {
                if (CutSelectionToClipboard(ref state, buffer, ref length, spans))
                {
                    changed = true; layoutDirty = true;
                    ctx.ResetCaretBlink();
                }
            }

            // Ctrl+V
            if (input.KeyCtrlV && !state.WasPasteDown)
            {
                if (PasteFromClipboard(ref state, buffer, ref length, maxLength, spans, singleLine))
                {
                    changed = true; layoutDirty = true;
                    ctx.ResetCaretBlink();
                }
            }

            // Ctrl+B/I/U — signal style toggle to caller
            if (input.KeyCtrlB && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
                PendingStyleToggle = RichSpanStyle.Bold;
            if (input.KeyCtrlI && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
                PendingStyleToggle = RichSpanStyle.Italic;
            if (input.KeyCtrlU && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
                PendingStyleToggle = RichSpanStyle.Underline;

            // Shift+Enter — soft line break in single-line blocks
            if (singleLine && input.KeyEnter && input.KeyShift && length < maxLength)
            {
                if (DeleteSelectionIfAny(ref state, buffer, ref length, spans))
                    changed = true;

                BufferInsertChar(buffer, ref length, maxLength, state.CaretPos, '\n');
                AdjustSpansForInsert(spans, state.CaretPos, 1);
                state.CaretPos++;
                changed = true; layoutDirty = true;
                ctx.ResetCaretBlink();
            }

            // Character input
            unsafe
            {
                for (int i = 0; i < input.InputCharCount; i++)
                {
                    char c = input.InputChars[i];
                    if (c >= 32 && length < maxLength)
                    {
                        if (DeleteSelectionIfAny(ref state, buffer, ref length, spans))
                            changed = true;

                        BufferInsertChar(buffer, ref length, maxLength, state.CaretPos, c);
                        AdjustSpansForInsert(spans, state.CaretPos, 1);
                        state.CaretPos++;
                        changed = true; layoutDirty = true;
                        ctx.ResetCaretBlink();
                    }
                }
            }

            // Enter (multi-line only — CodeBlock)
            if (!singleLine && input.KeyEnter && length < maxLength)
            {
                if (DeleteSelectionIfAny(ref state, buffer, ref length, spans))
                    changed = true;

                BufferInsertChar(buffer, ref length, maxLength, state.CaretPos, '\n');
                AdjustSpansForInsert(spans, state.CaretPos, 1);
                state.CaretPos++;
                changed = true; layoutDirty = true;
                ctx.ResetCaretBlink();
            }

            // Backspace (Ctrl+Backspace = delete word)
            if (input.KeyBackspace)
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0.45f;
                if (input.KeyCtrl)
                {
                    if (ApplyBackspaceWord(ref state, buffer, ref length, spans))
                    {
                        changed = true; layoutDirty = true;
                        ctx.ResetCaretBlink();
                    }
                }
                else if (ApplyBackspace(ref state, buffer, ref length, spans))
                {
                    changed = true; layoutDirty = true;
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

                    if (!ApplyBackspace(ref state, buffer, ref length, spans))
                    {
                        state.BackspaceRepeatTimer = 0f;
                        break;
                    }

                    changed = true; layoutDirty = true;
                    ctx.ResetCaretBlink();
                }
            }
            else
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0f;
            }

            // Delete
            if (input.KeyDelete)
            {
                state.DeleteRepeatHeldTime = 0f;
                state.DeleteRepeatTimer = 0.45f;
                if (ApplyDelete(ref state, buffer, ref length, spans))
                {
                    changed = true; layoutDirty = true;
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

                    if (!ApplyDelete(ref state, buffer, ref length, spans))
                    {
                        state.DeleteRepeatTimer = 0f;
                        break;
                    }

                    changed = true; layoutDirty = true;
                    ctx.ResetCaretBlink();
                }
            }
            else
            {
                state.DeleteRepeatHeldTime = 0f;
                state.DeleteRepeatTimer = 0f;
            }

            // Arrow keys (Ctrl = word jump, Shift = extend selection)
            if (input.KeyLeft && state.CaretPos > 0)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = input.KeyCtrl
                    ? FindWordBoundaryLeft(buffer, length, state.CaretPos)
                    : state.CaretPos - 1;
                UpdateSelection(ref state, input.KeyShift, caretBeforeMove);
                ctx.ResetCaretBlink();
            }
            if (input.KeyRight && state.CaretPos < length)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = input.KeyCtrl
                    ? FindWordBoundaryRight(buffer, length, state.CaretPos)
                    : state.CaretPos + 1;
                UpdateSelection(ref state, input.KeyShift, caretBeforeMove);
                ctx.ResetCaretBlink();
            }

            // Home/End
            if (input.KeyHome)
            {
                int caretBeforeMove = state.CaretPos;
                int currentLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
                state.CaretPos = VisualLinesScratch[currentLineIndex].Start;
                UpdateSelection(ref state, input.KeyShift, caretBeforeMove);
                ctx.ResetCaretBlink();
            }
            if (input.KeyEnd)
            {
                int caretBeforeMove = state.CaretPos;
                int currentLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
                var currentLine = VisualLinesScratch[currentLineIndex];
                state.CaretPos = currentLine.Start + currentLine.Length;
                UpdateSelection(ref state, input.KeyShift, caretBeforeMove);
                ctx.ResetCaretBlink();
            }

            // Up/Down — signal cross-block navigation when at boundary
            if (input.KeyUp)
            {
                int currentLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
                if (currentLineIndex <= 0)
                {
                    NavigatedPastStart = true;
                }
                else
                {
                    int caretBeforeMove = state.CaretPos;
                    state.CaretPos = GetPosOnPreviousVisualLine(ctx.Font, buffer, length, fontSize,
                        VisualLinesScratch, visualLineCount, state.CaretPos);
                    UpdateSelection(ref state, input.KeyShift, caretBeforeMove);
                    ctx.ResetCaretBlink();
                }
            }
            if (input.KeyDown)
            {
                int currentLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
                if (currentLineIndex >= visualLineCount - 1)
                {
                    NavigatedPastEnd = true;
                }
                else
                {
                    int caretBeforeMove = state.CaretPos;
                    state.CaretPos = GetPosOnNextVisualLine(ctx.Font, buffer, length, fontSize,
                        VisualLinesScratch, visualLineCount, state.CaretPos);
                    UpdateSelection(ref state, input.KeyShift, caretBeforeMove);
                    ctx.ResetCaretBlink();
                }
            }

            // Ctrl+A
            if (input.KeyCtrlA)
            {
                state.SelectionStart = 0;
                state.SelectionEnd = length;
                state.CaretPos = length;
            }

            // Note: Escape and click-outside defocus are handled by DocumentRenderer,
            // not here. This widget only manages caret/selection within its text.

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

        // Rebuild visual lines if text changed
        if (layoutDirty)
        {
            EnsureVisualLineCapacity(length + 1);
            visualLineCount = RichTextLayout.BuildVisualLines(ctx.Font, buffer, length, fontSize, wordWrap, wrapWidth, 0f, VisualLinesScratch);
        }

        // Drag selection (skip on the click frame — already handled above)
        if (ctx.IsActive(widgetId) && !pointerCapturedByOverlay)
        {
            if (input.MouseDown && !input.MousePressed)
            {
                int lineIndex = GetVisualLineIndexFromMouseY(y, mousePos.Y, lineHeight, visualLineCount);
                var dragLine = VisualLinesScratch[lineIndex];
                float relativeX = mousePos.X - x;
                int column = GetCaretPosFromRelativeX(ctx.Font,
                    buffer.Slice(dragLine.Start, dragLine.Length), fontSize, relativeX);
                state.CaretPos = dragLine.Start + column;
                state.SelectionEnd = state.CaretPos;
            }
            else if (input.MouseReleased)
            {
                ctx.ClearActive();
            }
        }

        // =================================================================
        //  RENDERING
        // =================================================================

        for (int lineIndex = 0; lineIndex < visualLineCount; lineIndex++)
        {
            var line = VisualLinesScratch[lineIndex];
            float lineWindowY = y + lineIndex * lineHeight;

            // Selection highlight
            if (isFocused && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
            {
                int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
                int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
                int lineStart = line.Start;
                int lineEnd = line.Start + line.Length;
                int overlapStart = Math.Max(selStart, lineStart);
                int overlapEnd = Math.Min(selEnd, lineEnd);

                if (overlapEnd > overlapStart)
                {
                    float startXOff = overlapStart > lineStart
                        ? Im.MeasureTextWidth(buffer.Slice(lineStart, overlapStart - lineStart), fontSize)
                        : 0f;
                    float endXOff = Im.MeasureTextWidth(
                        buffer.Slice(lineStart, overlapEnd - lineStart), fontSize);

                    Im.DrawRect(x + startXOff, lineWindowY, endXOff - startXOff, lineHeight, selectionColor);
                }
            }

            // Text rendering (rich or plain)
            if (line.Length > 0)
            {
                int lineStart = line.Start;
                int lineEnd = lineStart + line.Length;

                // Check if any spans overlap this line
                bool hasSpans = false;
                for (int s = 0; s < spans.Count; s++)
                {
                    if (spans[s].Start < lineEnd && spans[s].End > lineStart)
                    {
                        hasSpans = true;
                        break;
                    }
                }

                if (!hasSpans)
                {
                    Im.Text(buffer.Slice(lineStart, line.Length), x, lineWindowY, fontSize, defaultTextColor);
                }
                else
                {
                    DrawRichLine(buffer, lineStart, line.Length, spans,
                        x, lineWindowY, fontSize, defaultTextColor, style);
                }
            }
        }

        // Caret
        if (isFocused && ctx.CaretVisible)
        {
            int caretLineIndex = FindVisualLineIndex(buffer, length, VisualLinesScratch, visualLineCount, state.CaretPos);
            var caretLine = VisualLinesScratch[caretLineIndex];
            int caretColumn = Math.Clamp(state.CaretPos - caretLine.Start, 0, caretLine.Length);

            float caretOffsetX = caretColumn > 0
                ? Im.MeasureTextWidth(buffer.Slice(caretLine.Start, caretColumn), fontSize)
                : 0f;

            float caretWindowX = x + caretOffsetX;
            float caretWindowY = y + caretLineIndex * lineHeight;
            Im.DrawRect(caretWindowX, caretWindowY + 2f, 1f, lineHeight - 4f, defaultTextColor);
        }

        state.LastSeenFrame = ctx.FrameCount;
        States[widgetId] = state;
        PruneStaleStates(ctx.FrameCount);
        return changed;
    }

    public static void SetState(int widgetId, int caretPos, int selectionStart = -1, int selectionEnd = -1)
    {
        if (!States.TryGetValue(widgetId, out var state))
            state = new TextAreaState();

        state.CaretPos = caretPos;
        state.SelectionStart = selectionStart;
        state.SelectionEnd = selectionEnd;
        state.LastSeenFrame = Im.Context.FrameCount;
        States[widgetId] = state;
    }

    public static bool TryGetState(int widgetId, out int caretPos, out int selectionStart, out int selectionEnd)
    {
        caretPos = 0;
        selectionStart = -1;
        selectionEnd = -1;

        if (!States.TryGetValue(widgetId, out TextAreaState state))
            return false;

        caretPos = state.CaretPos;
        selectionStart = state.SelectionStart;
        selectionEnd = state.SelectionEnd;
        return true;
    }

    /// <summary>Removes cached widget state for a given ID. Call when a block is deleted.</summary>
    public static void RemoveState(int widgetId) => States.Remove(widgetId);

    /// <summary>
    /// Resets the key repeat timers for a widget so that a held backspace/delete
    /// doesn't immediately fire on a newly-focused block after a merge.
    /// </summary>
    public static void ResetKeyRepeatState(int widgetId)
    {
        if (States.TryGetValue(widgetId, out var state))
        {
            state.BackspaceRepeatHeldTime = 0f;
            state.BackspaceRepeatTimer = 0.45f; // Initial delay before repeat starts
            state.DeleteRepeatHeldTime = 0f;
            state.DeleteRepeatTimer = 0.45f;
            States[widgetId] = state;
        }
    }

    /// <summary>
    /// Configures optional fonts for rich text style rendering.
    /// When style fonts are set, Bold/Italic spans use actual glyph variants.
    /// </summary>
    public static void SetStyleFonts(Font? boldFont, Font? italicFont, Font? boldItalicFont)
    {
        RichTextSegmentStyling.SetStyleFonts(boldFont, italicFont, boldItalicFont);
    }

    // =====================================================================
    //  Buffer editing (inlined from ImTextEdit)
    // =====================================================================

    private static void BufferInsertChar(Span<char> buffer, ref int length, int maxLength, int pos, char c)
    {
        if (length >= maxLength) return;
        if ((uint)pos > (uint)length) pos = length;
        for (int i = length; i > pos; i--)
            buffer[i] = buffer[i - 1];
        buffer[pos] = c;
        length++;
    }

    private static void BufferDeleteRange(Span<char> buffer, ref int length, int start, int end)
    {
        if (start < 0) start = 0;
        if (end > length) end = length;
        if (end <= start) return;
        int deleteCount = end - start;
        for (int i = start; i < length - deleteCount; i++)
            buffer[i] = buffer[i + deleteCount];
        length -= deleteCount;
    }

    private static int GetCaretPosFromRelativeX(Font? font, ReadOnlySpan<char> text, float fontSize, float relativeX)
    {
        if (relativeX <= 0f || text.Length == 0) return 0;

        if (font == null)
        {
            int estimate = (int)(relativeX / 7f);
            return Math.Clamp(estimate, 0, text.Length);
        }

        float scale = fontSize / font.BaseSizePixels;
        float cursorX = 0f;

        for (int i = 0; i < text.Length; i++)
        {
            float advanceX;
            if (font.TryGetGlyph(text[i], out var glyph))
                advanceX = glyph.AdvanceX * scale;
            else if (font.TryGetGlyph(' ', out var spaceGlyph))
                advanceX = spaceGlyph.AdvanceX * scale;
            else
                continue;

            float nextX = cursorX + advanceX;
            if (nextX >= relativeX)
                return (relativeX - cursorX) < (nextX - relativeX) ? i : i + 1;
            cursorX = nextX;
        }

        return text.Length;
    }

    // =====================================================================
    //  Span adjustment
    // =====================================================================

    private static void AdjustSpansForInsert(List<RichSpan> spans, int position, int insertedLength)
    {
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (span.Start >= position)
            {
                span.Start += insertedLength;
            }
            else if (span.End > position)
            {
                span.Length += insertedLength;
            }
            spans[i] = span;
        }
    }

    private static void AdjustSpansForDelete(List<RichSpan> spans, int position, int deletedLength)
    {
        int end = position + deletedLength;
        for (int i = spans.Count - 1; i >= 0; i--)
        {
            var span = spans[i];
            if (span.End <= position)
            {
                // Before deletion — keep as-is
            }
            else if (span.Start >= end)
            {
                // After deletion — shift left
                span.Start -= deletedLength;
                spans[i] = span;
            }
            else
            {
                // Overlaps with deletion
                int newStart = Math.Min(span.Start, position);
                int newEnd = Math.Max(span.End - deletedLength, position);
                int newLength = newEnd - newStart;
                if (newLength > 0)
                {
                    span.Start = newStart;
                    span.Length = newLength;
                    spans[i] = span;
                }
                else
                {
                    spans.RemoveAt(i);
                }
            }
        }
    }

    // =====================================================================
    //  Input helpers
    // =====================================================================

    private static void UpdateSelection(ref TextAreaState state, bool shiftHeld, int caretBeforeMove)
    {
        if (!shiftHeld)
        {
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
        }
        else
        {
            if (state.SelectionStart < 0)
                state.SelectionStart = caretBeforeMove;
            state.SelectionEnd = state.CaretPos;
        }
    }

    private static bool DeleteSelectionIfAny(ref TextAreaState state, Span<char> buffer, ref int length, List<RichSpan> spans)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd)
            return false;

        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
        selStart = Math.Clamp(selStart, 0, length);
        selEnd = Math.Clamp(selEnd, 0, length);

        if (selEnd <= selStart)
        {
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
            return false;
        }

        BufferDeleteRange(buffer, ref length, selStart, selEnd);
        AdjustSpansForDelete(spans, selStart, selEnd - selStart);
        state.CaretPos = selStart;
        state.SelectionStart = -1;
        state.SelectionEnd = -1;
        return true;
    }

    private static bool ApplyBackspace(ref TextAreaState state, Span<char> buffer, ref int length, List<RichSpan> spans)
    {
        if (DeleteSelectionIfAny(ref state, buffer, ref length, spans))
            return true;

        if (state.CaretPos <= 0 || length <= 0)
            return false;

        int deletePos = state.CaretPos - 1;
        BufferDeleteRange(buffer, ref length, deletePos, state.CaretPos);
        AdjustSpansForDelete(spans, deletePos, 1);
        state.CaretPos--;
        return true;
    }

    private static bool ApplyBackspaceWord(ref TextAreaState state, Span<char> buffer, ref int length, List<RichSpan> spans)
    {
        if (DeleteSelectionIfAny(ref state, buffer, ref length, spans))
            return true;

        if (state.CaretPos <= 0 || length <= 0)
            return false;

        int wordStart = FindWordBoundaryLeft(buffer, length, state.CaretPos);
        int deleteCount = state.CaretPos - wordStart;
        if (deleteCount <= 0) return false;

        BufferDeleteRange(buffer, ref length, wordStart, state.CaretPos);
        AdjustSpansForDelete(spans, wordStart, deleteCount);
        state.CaretPos = wordStart;
        return true;
    }

    private static bool ApplyDelete(ref TextAreaState state, Span<char> buffer, ref int length, List<RichSpan> spans)
    {
        if (DeleteSelectionIfAny(ref state, buffer, ref length, spans))
            return true;

        if (state.CaretPos >= length)
            return false;

        BufferDeleteRange(buffer, ref length, state.CaretPos, state.CaretPos + 1);
        AdjustSpansForDelete(spans, state.CaretPos, 1);
        return true;
    }

    private static void CopySelectionToClipboard(ref TextAreaState state, Span<char> buffer, int length,
        List<RichSpan>? spans = null)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd)
            return;

        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
        selStart = Math.Clamp(selStart, 0, length);
        selEnd = Math.Clamp(selEnd, 0, length);

        if (selEnd <= selStart) return;

        string selectedText = new string(buffer.Slice(selStart, selEnd - selStart));
        DerpLib.Derp.SetClipboardText(selectedText);

        // Cache spans for rich paste
        _richClipboardText = selectedText;
        _richClipboardSpans = null;
        if (spans != null)
        {
            var clipped = new List<RichSpan>();
            for (int i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                if (span.End <= selStart || span.Start >= selEnd) continue;
                int newStart = Math.Max(span.Start, selStart) - selStart;
                int newEnd = Math.Min(span.End, selEnd) - selStart;
                if (newEnd > newStart)
                    clipped.Add(new RichSpan { Start = newStart, Length = newEnd - newStart, Style = span.Style });
            }
            if (clipped.Count > 0)
                _richClipboardSpans = clipped;
        }
    }

    private static bool CutSelectionToClipboard(ref TextAreaState state, Span<char> buffer, ref int length, List<RichSpan> spans)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd)
            return false;

        // Use copy to also cache spans
        CopySelectionToClipboard(ref state, buffer, length, spans);

        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
        selStart = Math.Clamp(selStart, 0, length);
        selEnd = Math.Clamp(selEnd, 0, length);

        if (selEnd <= selStart) return false;

        BufferDeleteRange(buffer, ref length, selStart, selEnd);
        AdjustSpansForDelete(spans, selStart, selEnd - selStart);
        state.CaretPos = selStart;
        state.SelectionStart = -1;
        state.SelectionEnd = -1;
        return true;
    }

    private static bool PasteFromClipboard(ref TextAreaState state, Span<char> buffer, ref int length,
        int maxLength, List<RichSpan> spans, bool singleLine)
    {
        string? clipboard = DerpLib.Derp.GetClipboardText();
        if (string.IsNullOrEmpty(clipboard))
            return false;

        bool selectionDeleted = DeleteSelectionIfAny(ref state, buffer, ref length, spans);

        int insertPos = state.CaretPos;
        bool insertedAny = false;
        int insertedCount = 0;
        for (int i = 0; i < clipboard.Length; i++)
        {
            char c = clipboard[i];
            if (c == '\r') continue;
            if (singleLine && c == '\n') continue;
            if (length >= maxLength) break;

            BufferInsertChar(buffer, ref length, maxLength, state.CaretPos, c);
            AdjustSpansForInsert(spans, state.CaretPos, 1);
            state.CaretPos++;
            insertedCount++;
            insertedAny = true;
        }

        // Restore cached rich spans if clipboard matches
        if (insertedAny && _richClipboardSpans != null && _richClipboardText == clipboard)
        {
            for (int i = 0; i < _richClipboardSpans.Count; i++)
            {
                var cachedSpan = _richClipboardSpans[i];
                if (cachedSpan.Start + cachedSpan.Length <= insertedCount)
                {
                    spans.Add(new RichSpan
                    {
                        Start = insertPos + cachedSpan.Start,
                        Length = cachedSpan.Length,
                        Style = cachedSpan.Style,
                    });
                }
            }
        }

        return insertedAny || selectionDeleted;
    }

    // =====================================================================
    //  Visual line navigation helpers
    // =====================================================================

    private static int FindVisualLineIndex(ReadOnlySpan<char> buffer, int length,
        RichTextLayout.VisualLine[] lines, int lineCount, int caretPos)
    {
        if (lineCount <= 1) return 0;

        int pos = Math.Clamp(caretPos, 0, length);
        int lo = 0;
        int hi = lineCount - 1;

        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var line = lines[mid];
            int start = line.Start;
            int end = start + line.Length;

            if (pos < start) { hi = mid - 1; continue; }
            if (pos > end) { lo = mid + 1; continue; }
            if (pos < end) return mid;

            // pos == end
            if (end >= length || buffer[end] == '\n') return mid;
            lo = mid + 1;
        }

        return Math.Clamp(lo, 0, lineCount - 1);
    }

    private static int GetPosOnPreviousVisualLine(Font? font, ReadOnlySpan<char> buffer, int length,
        float fontSize, RichTextLayout.VisualLine[] lines, int lineCount, int caretPos)
    {
        if (lineCount <= 1) return caretPos;

        int currentLineIndex = FindVisualLineIndex(buffer, length, lines, lineCount, caretPos);
        if (currentLineIndex <= 0) return caretPos;

        var currentLine = lines[currentLineIndex];
        int currentColumn = Math.Clamp(caretPos - currentLine.Start, 0, currentLine.Length);
        float desiredX = currentColumn > 0
            ? Im.MeasureTextWidth(buffer.Slice(currentLine.Start, currentColumn), fontSize)
            : 0f;

        var previousLine = lines[currentLineIndex - 1];
        int newColumn = GetCaretPosFromRelativeX(font,
            buffer.Slice(previousLine.Start, previousLine.Length), fontSize, desiredX);
        return previousLine.Start + newColumn;
    }

    private static int GetPosOnNextVisualLine(Font? font, ReadOnlySpan<char> buffer, int length,
        float fontSize, RichTextLayout.VisualLine[] lines, int lineCount, int caretPos)
    {
        if (lineCount <= 1) return caretPos;

        int currentLineIndex = FindVisualLineIndex(buffer, length, lines, lineCount, caretPos);
        if (currentLineIndex >= lineCount - 1) return caretPos;

        var currentLine = lines[currentLineIndex];
        int currentColumn = Math.Clamp(caretPos - currentLine.Start, 0, currentLine.Length);
        float desiredX = currentColumn > 0
            ? Im.MeasureTextWidth(buffer.Slice(currentLine.Start, currentColumn), fontSize)
            : 0f;

        var nextLine = lines[currentLineIndex + 1];
        int newColumn = GetCaretPosFromRelativeX(font,
            buffer.Slice(nextLine.Start, nextLine.Length), fontSize, desiredX);
        return nextLine.Start + newColumn;
    }

    // =====================================================================
    //  Rich text rendering
    // =====================================================================

    private static void DrawRichLine(ReadOnlySpan<char> buffer, int lineStart, int lineLength,
        List<RichSpan> spans, float x, float y, float fontSize, uint defaultColor, ImStyle style)
    {
        EnsureStyledSegmentCapacity(spans.Count * 2 + 2);
        int segmentCount = RichTextLayout.BuildLineSegments(spans, lineStart, lineLength, StyledSegmentsScratch);
        float curX = x;
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var segment = StyledSegmentsScratch[segmentIndex];
            int segStart = segment.Start;
            int segEnd = segment.End;
            if (segEnd <= segStart)
            {
                continue;
            }

            RichSpanStyle segmentStyle = segment.Style;
            var segText = buffer.Slice(segStart, segEnd - segStart);
            Font? segmentFont = RichTextSegmentStyling.ResolveSegmentFont(segmentStyle, segText);
            float segWidth = RichTextSegmentStyling.MeasureSegmentWidth(segText, fontSize, segmentFont);

            if ((segmentStyle & RichSpanStyle.Code) != 0)
            {
                Im.DrawRoundedRect(curX - 2f, y - 1f, segWidth + 4f, fontSize + 2f, 2f, style.Surface);
            }
            else if ((segmentStyle & RichSpanStyle.Highlight) != 0)
            {
                Im.DrawRect(curX - 1f, y, segWidth + 2f, fontSize, 0x4000BBFF);
            }

            uint color = RichTextSegmentStyling.GetStyleColor(segmentStyle, defaultColor, style);
            RichTextSegmentStyling.DrawSegmentText(segText, curX, y, fontSize, color, segmentStyle, segmentFont);

            if ((segmentStyle & RichSpanStyle.Strikethrough) != 0)
            {
                float lineY = y + fontSize * 0.5f;
                Im.DrawLine(curX, lineY, curX + segWidth, lineY, 1f, color);
            }
            if ((segmentStyle & RichSpanStyle.Underline) != 0)
            {
                float lineY = y + fontSize + 1f;
                Im.DrawLine(curX, lineY, curX + segWidth, lineY, 1f, color);
            }

            curX += segWidth;
        }
    }

    // =====================================================================
    //  Word boundary helpers
    // =====================================================================

    private static int FindWordBoundaryLeft(ReadOnlySpan<char> buffer, int length, int pos)
    {
        if (pos <= 0) return 0;
        int i = pos - 1;

        // Skip whitespace/punctuation
        while (i > 0 && !char.IsLetterOrDigit(buffer[i]))
            i--;

        // Skip word chars
        while (i > 0 && char.IsLetterOrDigit(buffer[i - 1]))
            i--;

        return i;
    }

    private static int FindWordBoundaryRight(ReadOnlySpan<char> buffer, int length, int pos)
    {
        if (pos >= length) return length;
        int i = pos;

        // Skip word chars
        while (i < length && char.IsLetterOrDigit(buffer[i]))
            i++;

        // Skip whitespace/punctuation
        while (i < length && !char.IsLetterOrDigit(buffer[i]))
            i++;

        return i;
    }

    private static (int start, int end) FindWordAtPos(ReadOnlySpan<char> buffer, int length, int pos)
    {
        if (length == 0) return (0, 0);
        int p = Math.Clamp(pos, 0, length - 1);

        if (!char.IsLetterOrDigit(buffer[p]))
        {
            // On whitespace/punctuation — select just that char
            return (p, Math.Min(p + 1, length));
        }

        int start = p;
        while (start > 0 && char.IsLetterOrDigit(buffer[start - 1]))
            start--;

        int end = p;
        while (end < length && char.IsLetterOrDigit(buffer[end]))
            end++;

        return (start, end);
    }

    // =====================================================================
    //  Math helpers
    // =====================================================================

    private static int GetVisualLineIndexFromMouseY(float textAreaY, float mouseY,
        float lineHeight, int visualLineCount)
    {
        if (visualLineCount <= 1) return 0;

        float relativeY = mouseY - textAreaY;
        if (relativeY < 0f) return 0;

        int lineIndex = (int)(relativeY / lineHeight);
        return Math.Clamp(lineIndex, 0, visualLineCount - 1);
    }

    private static float GetKeyRepeatRate(float heldTime)
    {
        const float initialRate = 7f;
        const float maxRate = 38f;
        const float accelTime = 1.25f;

        float t = Math.Clamp(heldTime / accelTime, 0f, 1f);
        return initialRate + (maxRate - initialRate) * t;
    }

    private static void EnsureVisualLineCapacity(int requiredCapacity)
    {
        RichTextLayout.EnsureVisualLineCapacity(ref VisualLinesScratch, requiredCapacity);
    }

    private static void EnsureStyledSegmentCapacity(int requiredCapacity)
    {
        RichTextLayout.EnsureStyledSegmentCapacity(ref StyledSegmentsScratch, requiredCapacity);
    }

    private static void PruneStaleStates(int currentFrame)
    {
        if (currentFrame == _lastPrunedFrame || States.Count == 0)
        {
            return;
        }

        _lastPrunedFrame = currentFrame;

        const int StaleFrameThreshold = 600;
        PruneKeysScratch.Clear();

        foreach (var entry in States)
        {
            if (entry.Value.LastSeenFrame + StaleFrameThreshold < currentFrame)
            {
                PruneKeysScratch.Add(entry.Key);
            }
        }

        for (int i = 0; i < PruneKeysScratch.Count; i++)
        {
            States.Remove(PruneKeysScratch[i]);
        }
    }
}
