using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using Silk.NET.Input;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Numeric input widget with increment/decrement buttons and optional drag support.
/// </summary>
public static class ImNumberInput
{
    // State tracking (fixed-size arrays)
    private static readonly NumberInputState[] _states = new NumberInputState[64];
    private static readonly int[] _stateIds = new int[64];
    private static int _stateCount;

    private struct NumberInputState
    {
        public int CaretPos;
        public bool IsEditing;
        public bool IsDragging;
        public float DragStartValue;
        public float DragStartX;
        public float DragStartMouseY;
        public float DragAccumX;
        public byte CursorLocked;
    }

    /// <summary>Drag sensitivity - value change per pixel of mouse movement.</summary>
    public static float DragSensitivity = 0.1f;

    /// <summary>
    /// Draw a number input at explicit position.
    /// Returns true if value changed.
    /// </summary>
    public static bool DrawAt(string id, float x, float y, float width, ref float value,
        float min = float.MinValue, float max = float.MaxValue, float step = 1f, string format = "F2")
        => DrawAtInternal(Im.Context.GetId(id), x, y, width, ref value, min, max, step, format);

    private static bool DrawAtInternal(int widgetId, float x, float y, float width, ref float value,
        float min, float max, float step, string format)
    {
        float height = Im.Style.MinButtonHeight;
        float buttonWidth = height;

        var rect = new ImRect(x, y, width, height);

        var fullRect = new ImRect(rect.X, rect.Y, rect.Width, rect.Height);
        var decrementRect = new ImRect(rect.X, rect.Y, buttonWidth, height);
        var inputRect = new ImRect(rect.X + buttonWidth, rect.Y, rect.Width - buttonWidth * 2, height);
        var incrementRect = new ImRect(rect.X + rect.Width - buttonWidth, rect.Y, buttonWidth, height);

        // Get or create state
        int stateIdx = FindOrCreateState(widgetId);
        ref var state = ref _states[stateIdx];

        bool isFocused = Im.Context.FocusId == widgetId;
        bool hovered = inputRect.Contains(Im.MousePos);
        bool decrementHovered = decrementRect.Contains(Im.MousePos);
        bool incrementHovered = incrementRect.Contains(Im.MousePos);
        bool changed = false;

        // Handle decrement button
        if (decrementHovered && Im.MousePressed)
        {
            value = Math.Max(min, value - step);
            changed = true;
        }

        // Handle increment button
        if (incrementHovered && Im.MousePressed)
        {
            value = Math.Min(max, value + step);
            changed = true;
        }

        // Handle input field click
        if (hovered && Im.MousePressed)
        {
            Im.Context.FocusId = widgetId;
            state.IsEditing = true;
            Span<char> initBuf = stackalloc char[32];
            value.TryFormat(initBuf, out int initLen, format);
            state.CaretPos = initLen;
        }

        // Handle editing
        if (isFocused && state.IsEditing)
        {
            // Build current text from value
            Span<char> editBuffer = stackalloc char[32];
            value.TryFormat(editBuffer, out int length, format);

            bool textChanged = false;
            var input = Im.Context.Input;

            // Handle typed characters
            unsafe
            {
                for (int i = 0; i < input.InputCharCount; i++)
                {
                    char c = input.InputChars[i];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '.' || c == ',')
                    {
                        if (length < 31)
                        {
                            for (int j = length; j > state.CaretPos; j--)
                                editBuffer[j] = editBuffer[j - 1];
                            editBuffer[state.CaretPos] = c;
                            length++;
                            state.CaretPos++;
                            textChanged = true;
                        }
                    }
                }
            }

            // Backspace
            if (input.KeyBackspace && state.CaretPos > 0)
            {
                for (int i = state.CaretPos - 1; i < length - 1; i++)
                    editBuffer[i] = editBuffer[i + 1];
                length--;
                state.CaretPos--;
                textChanged = true;
            }

            // Delete
            if (input.KeyDelete && state.CaretPos < length)
            {
                for (int i = state.CaretPos; i < length - 1; i++)
                    editBuffer[i] = editBuffer[i + 1];
                length--;
                textChanged = true;
            }

            // Arrow keys
            if (input.KeyLeft && state.CaretPos > 0)
                state.CaretPos--;
            if (input.KeyRight && state.CaretPos < length)
                state.CaretPos++;

            // Up/Down for increment/decrement
            if (input.KeyUp)
            {
                value = Math.Min(max, value + step);
                changed = true;
            }
            if (input.KeyDown)
            {
                value = Math.Max(min, value - step);
                changed = true;
            }

            // Parse on text change
            if (textChanged)
            {
                if (float.TryParse(editBuffer.Slice(0, length), out float parsed))
                {
                    value = Math.Clamp(parsed, min, max);
                    changed = true;
                }
            }

            // Escape/Enter - end edit
            if (input.KeyEscape || input.KeyEnter)
            {
                state.IsEditing = false;
                Im.Context.FocusId = 0;
            }

            // Click outside
            if (Im.MousePressed && !fullRect.Contains(Im.MousePos))
            {
                state.IsEditing = false;
                Im.Context.FocusId = 0;
            }
        }

        // Clamp value
        value = Math.Clamp(value, min, max);
        state.CaretPos = Math.Clamp(state.CaretPos, 0, 31);

        // Draw decrement button
        uint decrementBg = decrementHovered ? Im.Style.Hover : Im.Style.Surface;
        Im.DrawRoundedRectPerCorner(decrementRect.X, decrementRect.Y, decrementRect.Width, decrementRect.Height,
            Im.Style.CornerRadius, 0, 0, Im.Style.CornerRadius, decrementBg);
        Im.DrawLine(decrementRect.X + decrementRect.Width - 1, decrementRect.Y,
            decrementRect.X + decrementRect.Width - 1, decrementRect.Y + decrementRect.Height, 1f, Im.Style.Border);
        // Draw minus
        float cy = decrementRect.Y + decrementRect.Height * 0.5f;
        Im.DrawLine(decrementRect.X + 6, cy, decrementRect.X + decrementRect.Width - 6, cy, 2f, Im.Style.TextPrimary);

        // Draw increment button
        uint incrementBg = incrementHovered ? Im.Style.Hover : Im.Style.Surface;
        Im.DrawRoundedRectPerCorner(incrementRect.X, incrementRect.Y, incrementRect.Width, incrementRect.Height,
            0, Im.Style.CornerRadius, Im.Style.CornerRadius, 0, incrementBg);
        Im.DrawLine(incrementRect.X, incrementRect.Y, incrementRect.X, incrementRect.Y + incrementRect.Height, 1f, Im.Style.Border);
        // Draw plus
        float cx = incrementRect.X + incrementRect.Width * 0.5f;
        cy = incrementRect.Y + incrementRect.Height * 0.5f;
        float halfSize = (incrementRect.Width - 12) * 0.5f;
        Im.DrawLine(cx - halfSize, cy, cx + halfSize, cy, 2f, Im.Style.TextPrimary);
        Im.DrawLine(cx, cy - halfSize, cx, cy + halfSize, 2f, Im.Style.TextPrimary);

        // Draw input field (use rect fill matching button height, with top/bottom borders)
        uint borderColor = isFocused ? Im.Style.Primary : (hovered ? Im.Style.Hover : Im.Style.Border);
        Im.DrawRect(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, Im.Style.Surface);
        // Top and bottom borders at exact button edge positions
        Im.DrawLine(inputRect.X, inputRect.Y + 0.5f, inputRect.X + inputRect.Width, inputRect.Y + 0.5f, 1f, borderColor);
        Im.DrawLine(inputRect.X, inputRect.Y + inputRect.Height - 0.5f, inputRect.X + inputRect.Width, inputRect.Y + inputRect.Height - 0.5f, 1f, borderColor);

        // Draw value text
        Span<char> displayBuffer = stackalloc char[32];
        value.TryFormat(displayBuffer, out int displayLen, format);
        var displaySpan = displayBuffer.Slice(0, displayLen);
        float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan, Im.Style.FontSize);
        float textX = inputRect.X + (inputRect.Width - textWidth) * 0.5f;
        float textY = inputRect.Y + (inputRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(displaySpan, textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        // Draw caret when editing
        if (isFocused && state.IsEditing)
        {
            float caretX = textX;
            if (state.CaretPos > 0 && state.CaretPos <= displayLen)
            {
                caretX += ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan.Slice(0, state.CaretPos), Im.Style.FontSize);
            }
            float caretY = inputRect.Y + 4;
            float caretHeight = inputRect.Height - 8;
            Im.DrawLine(caretX, caretY, caretX, caretY + caretHeight, 1f, Im.Style.Primary);
        }

        return changed;
    }

    /// <summary>
    /// Draw a number input with draggable label.
    /// </summary>
    public static bool DrawAt(string label, string id, float x, float y, float labelWidth, float inputWidth,
        ref float value, float min = float.MinValue, float max = float.MaxValue, float step = 1f, string format = "F2")
    {
        int widgetId = Im.Context.GetId(id);
        int labelId = Im.Context.GetId(id + "_label");
        float height = Im.Style.MinButtonHeight;

        var labelRect = new ImRect(x, y, labelWidth, height);
        bool labelHovered = labelRect.Contains(Im.MousePos);

        int labelStateIdx = FindOrCreateState(labelId);
        ref var labelState = ref _states[labelStateIdx];

        bool changed = false;

        // Handle label drag
        if (labelHovered && Im.MousePressed && !labelState.IsDragging)
        {
            labelState.IsDragging = true;
            labelState.DragStartValue = value;
            labelState.DragStartX = Im.MousePos.X;
            labelState.DragStartMouseY = Im.MousePos.Y;
            labelState.DragAccumX = 0f;
            labelState.CursorLocked = ImMouseCursorLock.Begin(labelId) ? (byte)1 : (byte)0;
        }

        if (labelState.IsDragging)
        {
            Im.SetCursor(StandardCursor.HResize);
            if (Im.MouseDown)
            {
                float sensitivity = DragSensitivity * step;
                labelState.DragAccumX += labelState.CursorLocked != 0 ? ImMouseCursorLock.ConsumeDelta(labelId).X : Im.Context.Input.MouseDelta.X;
                float newValue = labelState.DragStartValue + labelState.DragAccumX * sensitivity;
                newValue = Math.Clamp(newValue, min, max);
                if (Math.Abs(newValue - value) > 0.0001f)
                {
                    value = newValue;
                    changed = true;
                }
            }
            else
            {
                labelState.IsDragging = false;
                if (labelState.CursorLocked != 0)
                {
                    ImMouseCursorLock.End(labelId);
                    labelState.CursorLocked = 0;
                }
            }
        }
        else if (labelHovered)
        {
            // Show horizontal resize cursor to indicate drag-to-adjust
            Im.SetCursor(StandardCursor.HResize);
        }

        // Draw label
        float textY = labelRect.Y + (height - Im.Style.FontSize) * 0.5f;
        uint labelColor = (labelHovered || labelState.IsDragging) ? Im.Style.Primary : Im.Style.TextPrimary;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, labelColor);

        // Draw input
        changed |= DrawAtInternal(widgetId, x + labelWidth, y, inputWidth, ref value, min, max, step, format);
        return changed;
    }

    /// <summary>
    /// Draw an integer number input.
    /// </summary>
    public static bool DrawIntAt(string id, float x, float y, float width, ref int value,
        int min = int.MinValue, int max = int.MaxValue, int step = 1)
    {
        float floatVal = value;
        bool changed = DrawAt(id, x, y, width, ref floatVal, min, max, step, "F0");
        if (changed)
        {
            value = (int)Math.Round(floatVal);
        }
        return changed;
    }

    /// <summary>
    /// Draw an integer number input with draggable label.
    /// </summary>
    public static bool DrawIntAt(string label, string id, float x, float y, float labelWidth, float inputWidth,
        ref int value, int min = int.MinValue, int max = int.MaxValue, int step = 1)
    {
        float floatVal = value;
        bool changed = DrawAt(label, id, x, y, labelWidth, inputWidth, ref floatVal, min, max, step, "F0");
        if (changed)
        {
            value = (int)Math.Round(floatVal);
        }
        return changed;
    }

    private static int FindOrCreateState(int id)
    {
        for (int i = 0; i < _stateCount; i++)
        {
            if (_stateIds[i] == id) return i;
        }

        if (_stateCount >= 64) return 0;

        int idx = _stateCount++;
        _stateIds[idx] = id;
        _states[idx] = default;
        return idx;
    }
}
