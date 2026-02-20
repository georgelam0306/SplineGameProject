using System;
using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using Silk.NET.Input;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Vector input widgets for Vector2, Vector3, and Vector4.
/// Supports Unity-style label dragging to adjust component values.
/// </summary>
public static class ImVectorInput
{
    private static ReadOnlySpan<char> MixedPlaceholder => "—".AsSpan();

    // State tracking
    private static readonly VectorInputState[] _states = new VectorInputState[64];
    private static readonly int[] _stateIds = new int[64];
    private static int _stateCount;

    private struct VectorInputState
    {
        public int CaretPos;
        public int SelStart;
        public int SelEnd;
        public byte HasSelection;
        public byte WasFocusedPrev;
        public byte BlankMode;
        public byte Pressed;
        public byte PressedIsInput;
        public bool IsDragging;
        public float DragStartValue;
        public float DragStartX;
        public float DragStartMouseY;
        public float DragAccumX;
        public byte CursorLocked;
    }

    /// <summary>Drag sensitivity - value change per pixel of mouse movement.</summary>
    public static float DragSensitivity = 0.01f;

    /// <summary>
    /// Draw a Vector2 input at explicit position.
    /// </summary>
    public static bool DrawAt(string id, float x, float y, float width, ref Vector2 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAt(id, x, y, width, rightOverlayWidth: 0f, ref value, min, max, format, disabled: false);
    }

    public static bool DrawAt(string id, float x, float y, float width, float rightOverlayWidth, ref Vector2 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAt(id, x, y, width, rightOverlayWidth, ref value, min, max, format, disabled: false);
    }

    public static bool DrawAt(string id, float x, float y, float width, float rightOverlayWidth, ref Vector2 value,
        float min, float max, string format, bool disabled)
    {
        var rect = new ImRect(x, y, width, Im.Style.MinButtonHeight);

        int parentId = Im.Context.GetId(id);
        float componentWidth = (rect.Width - Im.Style.Spacing) / 2;
        bool changed = false;

        // X component
        changed |= DrawComponent(parentId * 31 + 0, "X", rect.X, rect.Y, componentWidth, rightOverlayWidth, ref value.X, min, max, format, mixed: false, disabled);

        // Y component
        changed |= DrawComponent(parentId * 31 + 1, "Y", rect.X + componentWidth + Im.Style.Spacing, rect.Y, componentWidth, rightOverlayWidth, ref value.Y, min, max, format, mixed: false, disabled);

        return changed;
    }

    public static bool DrawAtMixed(string id, float x, float y, float width, float rightOverlayWidth, ref Vector2 value, uint mixedMask,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAtMixed(id, x, y, width, rightOverlayWidth, ref value, mixedMask, min, max, format, disabled: false);
    }

    public static bool DrawAtMixed(string id, float x, float y, float width, float rightOverlayWidth, ref Vector2 value, uint mixedMask,
        float min, float max, string format, bool disabled)
    {
        var rect = new ImRect(x, y, width, Im.Style.MinButtonHeight);

        int parentId = Im.Context.GetId(id);
        float componentWidth = (rect.Width - Im.Style.Spacing) / 2;
        bool changed = false;

        changed |= DrawComponent(parentId * 31 + 0, "X", rect.X, rect.Y, componentWidth, rightOverlayWidth, ref value.X, min, max, format, mixed: (mixedMask & 1u) != 0u, disabled);
        changed |= DrawComponent(parentId * 31 + 1, "Y", rect.X + componentWidth + Im.Style.Spacing, rect.Y, componentWidth, rightOverlayWidth, ref value.Y, min, max, format, mixed: (mixedMask & 2u) != 0u, disabled);

        return changed;
    }

    /// <summary>
    /// Draw a Vector3 input at explicit position.
    /// </summary>
    public static bool DrawAt(string id, float x, float y, float width, ref Vector3 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAt(id, x, y, width, rightOverlayWidth: 0f, ref value, min, max, format, disabled: false);
    }

    public static bool DrawAt(string id, float x, float y, float width, float rightOverlayWidth, ref Vector3 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAt(id, x, y, width, rightOverlayWidth, ref value, min, max, format, disabled: false);
    }

    public static bool DrawAt(string id, float x, float y, float width, float rightOverlayWidth, ref Vector3 value,
        float min, float max, string format, bool disabled)
    {
        var rect = new ImRect(x, y, width, Im.Style.MinButtonHeight);

        int parentId = Im.Context.GetId(id);
        float componentWidth = (rect.Width - Im.Style.Spacing * 2) / 3;
        bool changed = false;

        // X component (red-ish)
        changed |= DrawComponent(parentId * 31 + 0, "X", rect.X, rect.Y, componentWidth, rightOverlayWidth, ref value.X, min, max, format, mixed: false, disabled);

        // Y component (green-ish)
        changed |= DrawComponent(parentId * 31 + 1, "Y", rect.X + componentWidth + Im.Style.Spacing, rect.Y, componentWidth, rightOverlayWidth, ref value.Y, min, max, format, mixed: false, disabled);

        // Z component (blue-ish)
        changed |= DrawComponent(parentId * 31 + 2, "Z", rect.X + (componentWidth + Im.Style.Spacing) * 2, rect.Y, componentWidth, rightOverlayWidth, ref value.Z, min, max, format, mixed: false, disabled);

        return changed;
    }

    public static bool DrawAtMixed(string id, float x, float y, float width, float rightOverlayWidth, ref Vector3 value, uint mixedMask,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAtMixed(id, x, y, width, rightOverlayWidth, ref value, mixedMask, min, max, format, disabled: false);
    }

    public static bool DrawAtMixed(string id, float x, float y, float width, float rightOverlayWidth, ref Vector3 value, uint mixedMask,
        float min, float max, string format, bool disabled)
    {
        var rect = new ImRect(x, y, width, Im.Style.MinButtonHeight);

        int parentId = Im.Context.GetId(id);
        float componentWidth = (rect.Width - Im.Style.Spacing * 2) / 3;
        bool changed = false;

        changed |= DrawComponent(parentId * 31 + 0, "X", rect.X, rect.Y, componentWidth, rightOverlayWidth, ref value.X, min, max, format, mixed: (mixedMask & 1u) != 0u, disabled);
        changed |= DrawComponent(parentId * 31 + 1, "Y", rect.X + componentWidth + Im.Style.Spacing, rect.Y, componentWidth, rightOverlayWidth, ref value.Y, min, max, format, mixed: (mixedMask & 2u) != 0u, disabled);
        changed |= DrawComponent(parentId * 31 + 2, "Z", rect.X + (componentWidth + Im.Style.Spacing) * 2, rect.Y, componentWidth, rightOverlayWidth, ref value.Z, min, max, format, mixed: (mixedMask & 4u) != 0u, disabled);

        return changed;
    }

    /// <summary>
    /// Draw a Vector4 input at explicit position.
    /// </summary>
    public static bool DrawAt(string id, float x, float y, float width, ref Vector4 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAt(id, x, y, width, rightOverlayWidth: 0f, ref value, min, max, format, disabled: false);
    }

    public static bool DrawAt(string id, float x, float y, float width, float rightOverlayWidth, ref Vector4 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAt(id, x, y, width, rightOverlayWidth, ref value, min, max, format, disabled: false);
    }

    public static bool DrawAt(string id, float x, float y, float width, float rightOverlayWidth, ref Vector4 value,
        float min, float max, string format, bool disabled)
    {
        var rect = new ImRect(x, y, width, Im.Style.MinButtonHeight);

        int parentId = Im.Context.GetId(id);
        float componentWidth = (rect.Width - Im.Style.Spacing * 3) / 4;
        bool changed = false;

        changed |= DrawComponent(parentId * 31 + 0, "X", rect.X, rect.Y, componentWidth, rightOverlayWidth, ref value.X, min, max, format, mixed: false, disabled);
        changed |= DrawComponent(parentId * 31 + 1, "Y", rect.X + componentWidth + Im.Style.Spacing, rect.Y, componentWidth, rightOverlayWidth, ref value.Y, min, max, format, mixed: false, disabled);
        changed |= DrawComponent(parentId * 31 + 2, "Z", rect.X + (componentWidth + Im.Style.Spacing) * 2, rect.Y, componentWidth, rightOverlayWidth, ref value.Z, min, max, format, mixed: false, disabled);
        changed |= DrawComponent(parentId * 31 + 3, "W", rect.X + (componentWidth + Im.Style.Spacing) * 3, rect.Y, componentWidth, rightOverlayWidth, ref value.W, min, max, format, mixed: false, disabled);

        return changed;
    }

    public static bool DrawAtMixed(string id, float x, float y, float width, float rightOverlayWidth, ref Vector4 value, uint mixedMask,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        return DrawAtMixed(id, x, y, width, rightOverlayWidth, ref value, mixedMask, min, max, format, disabled: false);
    }

    public static bool DrawAtMixed(string id, float x, float y, float width, float rightOverlayWidth, ref Vector4 value, uint mixedMask,
        float min, float max, string format, bool disabled)
    {
        var rect = new ImRect(x, y, width, Im.Style.MinButtonHeight);

        int parentId = Im.Context.GetId(id);
        float componentWidth = (rect.Width - Im.Style.Spacing * 3) / 4;
        bool changed = false;

        changed |= DrawComponent(parentId * 31 + 0, "X", rect.X, rect.Y, componentWidth, rightOverlayWidth, ref value.X, min, max, format, mixed: (mixedMask & 1u) != 0u, disabled);
        changed |= DrawComponent(parentId * 31 + 1, "Y", rect.X + componentWidth + Im.Style.Spacing, rect.Y, componentWidth, rightOverlayWidth, ref value.Y, min, max, format, mixed: (mixedMask & 2u) != 0u, disabled);
        changed |= DrawComponent(parentId * 31 + 2, "Z", rect.X + (componentWidth + Im.Style.Spacing) * 2, rect.Y, componentWidth, rightOverlayWidth, ref value.Z, min, max, format, mixed: (mixedMask & 4u) != 0u, disabled);
        changed |= DrawComponent(parentId * 31 + 3, "W", rect.X + (componentWidth + Im.Style.Spacing) * 3, rect.Y, componentWidth, rightOverlayWidth, ref value.W, min, max, format, mixed: (mixedMask & 8u) != 0u, disabled);

        return changed;
    }

    /// <summary>
    /// Draw a Vector2 input with label.
    /// </summary>
    public static bool DrawAt(string label, string id, float x, float y, float labelWidth, float inputWidth, ref Vector2 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        // DrawAt already uses local coords, so offset by labelWidth
        return DrawAt(id, x + labelWidth, y, inputWidth, ref value, min, max, format);
    }

    public static bool DrawAt(string label, string id, float x, float y, float labelWidth, float inputWidth, float rightOverlayWidth, ref Vector2 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        return DrawAt(id, x + labelWidth, y, inputWidth, rightOverlayWidth, ref value, min, max, format);
    }

    public static bool DrawAtMixed(string label, string id, float x, float y, float labelWidth, float inputWidth, float rightOverlayWidth, ref Vector2 value, uint mixedMask,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        return DrawAtMixed(id, x + labelWidth, y, inputWidth, rightOverlayWidth, ref value, mixedMask, min, max, format);
    }

    /// <summary>
    /// Draw a Vector3 input with label.
    /// </summary>
    public static bool DrawAt(string label, string id, float x, float y, float labelWidth, float inputWidth, ref Vector3 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        // DrawAt already uses local coords, so offset by labelWidth
        return DrawAt(id, x + labelWidth, y, inputWidth, ref value, min, max, format);
    }

    public static bool DrawAt(string label, string id, float x, float y, float labelWidth, float inputWidth, float rightOverlayWidth, ref Vector3 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        return DrawAt(id, x + labelWidth, y, inputWidth, rightOverlayWidth, ref value, min, max, format);
    }

    public static bool DrawAtMixed(string label, string id, float x, float y, float labelWidth, float inputWidth, float rightOverlayWidth, ref Vector3 value, uint mixedMask,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        return DrawAtMixed(id, x + labelWidth, y, inputWidth, rightOverlayWidth, ref value, mixedMask, min, max, format);
    }

    /// <summary>
    /// Draw a Vector4 input with label.
    /// </summary>
    public static bool DrawAt(string label, string id, float x, float y, float labelWidth, float inputWidth, ref Vector4 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        // DrawAt already uses local coords, so offset by labelWidth
        return DrawAt(id, x + labelWidth, y, inputWidth, ref value, min, max, format);
    }

    public static bool DrawAt(string label, string id, float x, float y, float labelWidth, float inputWidth, float rightOverlayWidth, ref Vector4 value,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        return DrawAt(id, x + labelWidth, y, inputWidth, rightOverlayWidth, ref value, min, max, format);
    }

    public static bool DrawAtMixed(string label, string id, float x, float y, float labelWidth, float inputWidth, float rightOverlayWidth, ref Vector4 value, uint mixedMask,
        float min = float.MinValue, float max = float.MaxValue, string format = "F2")
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        return DrawAtMixed(id, x + labelWidth, y, inputWidth, rightOverlayWidth, ref value, mixedMask, min, max, format);
    }

    private static bool DrawComponent(int widgetId, string label, float x, float y, float width, float rightOverlayWidth, ref float value,
        float min, float max, string format)
    {
        return DrawComponent(widgetId, label, x, y, width, rightOverlayWidth, ref value, min, max, format, mixed: false, disabled: false);
    }

    private static bool DrawComponent(int widgetId, string label, float x, float y, float width, float rightOverlayWidth, ref float value,
        float min, float max, string format, bool mixed)
    {
        return DrawComponent(widgetId, label, x, y, width, rightOverlayWidth, ref value, min, max, format, mixed, disabled: false);
    }

    private static bool DrawComponent(int widgetId, string label, float x, float y, float width, float rightOverlayWidth, ref float value,
        float min, float max, string format, bool mixed, bool disabled)
    {
        var ctx = Im.Context;
        float height = Im.Style.MinButtonHeight;
        float labelWidth = 18f;

        var fullRect = new ImRect(x, y, width, height);
        var labelRect = new ImRect(x, y, labelWidth, height);
        var inputRect = new ImRect(x + labelWidth, y, width - labelWidth, height);
        float overlay = MathF.Max(0f, rightOverlayWidth);
        float interactiveWidth = MathF.Max(0f, inputRect.Width - overlay);
        var interactiveInputRect = new ImRect(inputRect.X, y, interactiveWidth, height);

        int stateIdx = FindOrCreateState(widgetId);
        ref var state = ref _states[stateIdx];

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
            state.PressedIsInput = 0;
            state.IsDragging = false;
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

        bool isFocused = !disabled && ctx.IsFocused(widgetId);
        bool labelHovered = labelRect.Contains(Im.MousePos);
        bool hovered = fullRect.Contains(Im.MousePos);
        bool inputHovered = interactiveInputRect.Contains(Im.MousePos);
        bool changed = false;

        if (hovered && !disabled)
        {
            ctx.SetHot(widgetId);
        }

        // Handle press (drag or focus-on-release)
        if (!disabled && !isFocused && (labelHovered || inputHovered))
        {
            Im.SetCursor(StandardCursor.HResize);
        }

        if (!mixed && !disabled)
        {
            if ((labelHovered || inputHovered) && Im.MousePressed && !state.IsDragging)
            {
                ctx.SetActive(widgetId);
                state.Pressed = 1;
                state.PressedIsInput = inputHovered ? (byte)1 : (byte)0;
                state.DragStartValue = value;
                state.DragStartX = Im.MousePos.X;
                state.DragStartMouseY = Im.MousePos.Y;
            }

            if (state.Pressed != 0)
            {
                ctx.SetActive(widgetId);
                if (Im.MouseDown)
                {
                    float deltaX = Im.MousePos.X - state.DragStartX;
                    if (MathF.Abs(deltaX) >= 2f)
                    {
                        state.Pressed = 0;
                        state.IsDragging = true;
                        state.DragAccumX = 0f;
                        state.CursorLocked = ImMouseCursorLock.Begin(widgetId) ? (byte)1 : (byte)0;
                    }
                }
                else
                {
                    byte pressedIsInput = state.PressedIsInput;
                    state.Pressed = 0;
                    state.PressedIsInput = 0;

                    if (pressedIsInput != 0)
                    {
                        ctx.FocusId = widgetId;
                        Span<char> initBuf = stackalloc char[32];
                        value.TryFormat(initBuf, out int initLen, format);
                        state.CaretPos = initLen;
                        state.SelStart = 0;
                        state.SelEnd = initLen;
                        state.HasSelection = 1;
                        state.BlankMode = 0;
                    }
                }
            }

            if (state.IsDragging)
            {
                ctx.SetActive(widgetId);
                if (Im.MouseDown)
                {
                    state.DragAccumX += state.CursorLocked != 0 ? ImMouseCursorLock.ConsumeDelta(widgetId).X : ctx.Input.MouseDelta.X;
                    float newValue = state.DragStartValue + state.DragAccumX * DragSensitivity;
                    newValue = Math.Clamp(newValue, min, max);
                    if (Math.Abs(newValue - value) > 0.0001f)
                    {
                        value = newValue;
                        changed = true;
                    }
                }
                else
                {
                    state.IsDragging = false;
                    if (state.CursorLocked != 0)
                    {
                        ImMouseCursorLock.End(widgetId);
                        state.CursorLocked = 0;
                    }
                    if (ctx.IsActive(widgetId) && !isFocused)
                    {
                        ctx.ClearActive();
                    }
                }
            }
        }
        else if (!disabled)
        {
            state.Pressed = 0;
            state.PressedIsInput = 0;
            state.IsDragging = false;
            if (state.CursorLocked != 0)
            {
                ImMouseCursorLock.End(widgetId);
                state.CursorLocked = 0;
            }

            if (inputHovered && Im.MousePressed)
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

        // Handle input field click while focused (caret placement)
        if (inputHovered && Im.MousePressed && isFocused && !state.IsDragging)
        {
            ctx.SetActive(widgetId);
            ctx.FocusId = widgetId;
            state.HasSelection = 0;
        }

        // Handle editing
        if (isFocused)
        {
            ctx.SetActive(widgetId);
            Span<char> editBuffer = stackalloc char[32];
            int length = 0;
            if (state.BlankMode == 0)
            {
                value.TryFormat(editBuffer, out length, format);
            }

            if (state.WasFocusedPrev == 0)
            {
                if (mixed)
                {
                    state.BlankMode = 1;
                    length = 0;
                    state.CaretPos = 0;
                    state.SelStart = 0;
                    state.SelEnd = 0;
                    state.HasSelection = 0;
                }
                else
                {
                    state.BlankMode = 0;
                    state.CaretPos = length;
                    state.SelStart = 0;
                    state.SelEnd = length;
                    state.HasSelection = 1;
                }
            }

            bool textChanged = false;
            var input = ctx.Input;

            // Handle typed characters
            unsafe
            {
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
                        editBuffer[i] = editBuffer[i + 1];
                    length--;
                    state.CaretPos--;
                    textChanged = true;
                }
            }

            // Arrow keys
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

            // Parse value
            if (textChanged)
            {
                if (float.TryParse(editBuffer.Slice(0, length), out float parsed))
                {
                    value = Math.Clamp(parsed, min, max);
                    changed = true;
                }
            }

            // Escape/Enter
            if (input.KeyEscape || input.KeyEnter)
            {
                ctx.FocusId = 0;
                if (ctx.IsActive(widgetId))
                {
                    ctx.ClearActive();
                }
                state.HasSelection = 0;
            }

            // Click outside
            if (Im.MousePressed && !fullRect.Contains(Im.MousePos))
            {
                ctx.FocusId = 0;
                if (ctx.IsActive(widgetId))
                {
                    ctx.ClearActive();
                }
                state.HasSelection = 0;
            }
        }

        // Clamp
        value = Math.Clamp(value, min, max);
        state.CaretPos = Math.Clamp(state.CaretPos, 0, 31);

        uint background = Im.Style.Surface;
        if (hovered && !isFocused && !disabled)
        {
            background = ImStyle.Lerp(Im.Style.Surface, 0xFF000000, 0.16f);
        }
        uint borderColor = disabled ? Im.Style.Border : (isFocused ? Im.Style.Primary : Im.Style.Border);
        Im.DrawRoundedRect(fullRect.X, fullRect.Y, fullRect.Width, fullRect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(fullRect.X, fullRect.Y, fullRect.Width, fullRect.Height, Im.Style.CornerRadius, borderColor, Im.Style.BorderWidth);
        Im.DrawLine(inputRect.X, fullRect.Y + 1f, inputRect.X, fullRect.Bottom - 1f, 1f, ImStyle.WithAlphaF(Im.Style.Border, 0.8f));

        float labelTextWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, label.AsSpan(), Im.Style.FontSize);
        float labelTextX = labelRect.X + (labelRect.Width - labelTextWidth) * 0.5f;
        float labelTextY = labelRect.Y + (labelRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelTextX, labelTextY, Im.Style.FontSize, Im.Style.TextSecondary);

        // Draw value text
        if (!isFocused && mixed)
        {
            ReadOnlySpan<char> placeholder = MixedPlaceholder;
            float placeholderWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, placeholder, Im.Style.FontSize);
            float placeholderX = interactiveInputRect.X + MathF.Max(Im.Style.Padding, (interactiveInputRect.Width - placeholderWidth) * 0.5f);
            float placeholderY = inputRect.Y + (inputRect.Height - Im.Style.FontSize) * 0.5f;
            Im.Text(placeholder, placeholderX, placeholderY, Im.Style.FontSize, Im.Style.TextSecondary);

            if (!disabled && ctx.IsActive(widgetId) && !Im.MouseDown && !state.IsDragging && !isFocused)
            {
                ctx.ClearActive();
            }

            state.WasFocusedPrev = (!disabled && ctx.IsFocused(widgetId)) ? (byte)1 : (byte)0;
            return changed;
        }

        Span<char> displayBuffer = stackalloc char[32];
        int displayLen = 0;
        if (!(isFocused && state.BlankMode != 0))
        {
            value.TryFormat(displayBuffer, out displayLen, format);
        }

        var displaySpan = displayBuffer.Slice(0, displayLen);

        float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan, Im.Style.FontSize);
        float textX = interactiveInputRect.X + MathF.Max(Im.Style.Padding, (interactiveInputRect.Width - textWidth) * 0.5f);
        float textY = inputRect.Y + (inputRect.Height - Im.Style.FontSize) * 0.5f;

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
                selX0 += ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan.Slice(0, selStart), Im.Style.FontSize);
            }
            if (selEnd > 0)
            {
                selX1 += ImTextMetrics.MeasureWidth(Im.Context.Font, displaySpan.Slice(0, selEnd), Im.Style.FontSize);
            }

            float selTop = inputRect.Y + 3f;
            float selHeight = inputRect.Height - 6f;
            uint selColor = ImStyle.WithAlphaF(Im.Style.Primary, 0.25f);
            Im.DrawRect(selX0, selTop, selX1 - selX0, selHeight, selColor);
        }

        uint valueTextColor = disabled ? Im.Style.TextSecondary : Im.Style.TextPrimary;
        Im.Text(displaySpan, textX, textY, Im.Style.FontSize, valueTextColor);

        // Draw caret
        if (isFocused)
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

        if (!disabled && ctx.IsActive(widgetId) && !Im.MouseDown && !state.IsDragging && !isFocused)
        {
            ctx.ClearActive();
        }

        state.WasFocusedPrev = (!disabled && ctx.IsFocused(widgetId)) ? (byte)1 : (byte)0;
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

    private static void DeleteSelection(Span<char> buffer, ref int length, ref VectorInputState state)
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
