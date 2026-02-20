using System;
using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using FontAwesome.Sharp;
using Silk.NET.Input;

namespace Derp.UI;

internal static class InsetsField
{
    private static readonly string LinkedIcon = ((char)IconChar.Link).ToString();
    private static readonly string UnlinkedIcon = ((char)IconChar.LinkSlash).ToString();

    private static readonly int[] StateIds = new int[128];
    private static readonly byte[] StateExpanded = new byte[128];
    private static int _stateCount;

    public static bool DrawInsets(
        string label,
        string widgetId,
        float labelWidth,
        float inputWidth,
        float minValue,
        float maxValue,
        float rowPaddingX,
        ref Vector4 value)
    {
        return DrawInsets(label, widgetId, labelWidth, inputWidth, minValue, maxValue, rowPaddingX, ref value, mixedMask: 0u);
    }

    public static bool DrawInsets(
        string label,
        string widgetId,
        float labelWidth,
        float inputWidth,
        float minValue,
        float maxValue,
        float rowPaddingX,
        ref Vector4 value,
        uint mixedMask)
    {
        return DrawInsets(label, widgetId, labelWidth, inputWidth, minValue, maxValue, rowPaddingX, ref value, mixedMask, out _);
    }

    public static bool DrawInsets(
        string label,
        string widgetId,
        float labelWidth,
        float inputWidth,
        float minValue,
        float maxValue,
        float rowPaddingX,
        ref Vector4 value,
        uint mixedMask,
        out bool isEditing)
    {
        var ctx = Im.Context;
        int id = Im.Context.GetId(widgetId);
        int stateIndex = FindOrCreateState(id);
        bool expanded = StateExpanded[stateIndex] != 0;
        isEditing = ctx.IsActive(id) || ctx.IsFocused(id);

        const float iconSize = 28f;
        float spacing = Im.Style.Spacing;
        float editorWidth = Math.Max(1f, inputWidth - iconSize - spacing);

        bool changed = false;

        if (!expanded)
        {
            float uniform = value.X;
            var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            float y = rect.Y;
            float x = rect.X + rowPaddingX;

            float labelTextY = y + (rect.Height - Im.Style.FontSize) * 0.5f;
            Im.Text(label.AsSpan(), x, labelTextY, Im.Style.FontSize, Im.Style.TextPrimary);

            float inputX = x + labelWidth;
            changed |= ImScalarInput.DrawAt(widgetId, inputX, y, editorWidth, rightOverlayWidth: 0f, ref uniform, minValue, maxValue, "F2", mixed: mixedMask != 0u);

            float buttonX = inputX + editorWidth + spacing;
            var buttonRect = new ImRect(buttonX, y, iconSize, rect.Height);
            bool toggleClicked = DrawLinkToggle(buttonRect, expanded);
            if (toggleClicked)
            {
                expanded = true;
                StateExpanded[stateIndex] = 1;
            }

            if (changed)
            {
                value.X = uniform;
                value.Y = uniform;
                value.Z = uniform;
                value.W = uniform;
            }

            return changed;
        }

        {
            // Top row: Left / Top
            var rectA = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            float y = rectA.Y;
            float x = rectA.X + rowPaddingX;

            float labelTextY = y + (rectA.Height - Im.Style.FontSize) * 0.5f;
            Im.Text(label.AsSpan(), x, labelTextY, Im.Style.FontSize, Im.Style.TextPrimary);

            float inputX = x + labelWidth;
            float rowWidth = editorWidth;
            float componentWidth = (rowWidth - spacing) * 0.5f;
            const float smallLabelWidth = 14f;

            float left = value.X;
            float top = value.Y;
            bool leftMixed = (mixedMask & 1u) != 0u;
            bool topMixed = (mixedMask & 2u) != 0u;

            float componentTextY = y + (rectA.Height - Im.Style.FontSize) * 0.5f;
            Im.Text("L".AsSpan(), inputX, componentTextY, Im.Style.FontSize, Im.Style.TextSecondary);
            Im.Context.PushId(id);
            int leftId = Im.Context.GetId("left");
            changed |= ImScalarInput.DrawAt("left", inputX + smallLabelWidth, y, componentWidth - smallLabelWidth, rightOverlayWidth: 0f, ref left, minValue, maxValue, "F2", mixed: leftMixed);
            isEditing |= ctx.IsActive(leftId) || ctx.IsFocused(leftId);
            Im.Context.PopId();

            float x1 = inputX + componentWidth + spacing;
            Im.Text("T".AsSpan(), x1, componentTextY, Im.Style.FontSize, Im.Style.TextSecondary);
            Im.Context.PushId(id);
            int topId = Im.Context.GetId("top");
            changed |= ImScalarInput.DrawAt("top", x1 + smallLabelWidth, y, componentWidth - smallLabelWidth, rightOverlayWidth: 0f, ref top, minValue, maxValue, "F2", mixed: topMixed);
            isEditing |= ctx.IsActive(topId) || ctx.IsFocused(topId);
            Im.Context.PopId();

            float buttonX = inputX + editorWidth + spacing;
            var buttonRect = new ImRect(buttonX, y, iconSize, rectA.Height);
            bool toggleClicked = DrawLinkToggle(buttonRect, expanded);
            if (toggleClicked)
            {
                expanded = false;
                StateExpanded[stateIndex] = 0;

                float uniform = value.X;
                value.X = uniform;
                value.Y = uniform;
                value.Z = uniform;
                value.W = uniform;
            }

            value.X = left;
            value.Y = top;
        }

        {
            // Bottom row: Right / Bottom
            var rectB = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            float y = rectB.Y;
            float x = rectB.X + rowPaddingX + labelWidth;

            float rowWidth = editorWidth;
            float componentWidth = (rowWidth - spacing) * 0.5f;
            const float smallLabelWidth = 14f;

            float right = value.Z;
            float bottom = value.W;
            bool rightMixed = (mixedMask & 4u) != 0u;
            bool bottomMixed = (mixedMask & 8u) != 0u;

            float componentTextY = y + (rectB.Height - Im.Style.FontSize) * 0.5f;
            Im.Text("R".AsSpan(), x, componentTextY, Im.Style.FontSize, Im.Style.TextSecondary);
            Im.Context.PushId(id);
            int rightId = Im.Context.GetId("right");
            changed |= ImScalarInput.DrawAt("right", x + smallLabelWidth, y, componentWidth - smallLabelWidth, rightOverlayWidth: 0f, ref right, minValue, maxValue, "F2", mixed: rightMixed);
            isEditing |= ctx.IsActive(rightId) || ctx.IsFocused(rightId);
            Im.Context.PopId();

            float x1 = x + componentWidth + spacing;
            Im.Text("B".AsSpan(), x1, componentTextY, Im.Style.FontSize, Im.Style.TextSecondary);
            Im.Context.PushId(id);
            int bottomId = Im.Context.GetId("bottom");
            changed |= ImScalarInput.DrawAt("bottom", x1 + smallLabelWidth, y, componentWidth - smallLabelWidth, rightOverlayWidth: 0f, ref bottom, minValue, maxValue, "F2", mixed: bottomMixed);
            isEditing |= ctx.IsActive(bottomId) || ctx.IsFocused(bottomId);
            Im.Context.PopId();

            value.Z = right;
            value.W = bottom;
        }

        return changed;
    }

    private static bool DrawLinkToggle(ImRect rect, bool expanded)
    {
        bool hovered = rect.Contains(Im.MousePos);
        if (hovered)
        {
            Im.SetCursor(StandardCursor.Hand);
        }

        uint background = Im.Style.Surface;
        if (hovered)
        {
            background = ImStyle.Lerp(Im.Style.Surface, 0xFF000000, 0.16f);
        }
        uint border = hovered ? Im.Style.Primary : Im.Style.Border;
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

        uint iconColor = Im.Style.TextSecondary;
        string icon = expanded ? UnlinkedIcon : LinkedIcon;
        float fontSize = Im.Style.FontSize;
        float iconWidth = Im.MeasureTextWidth(icon.AsSpan(), fontSize);
        float iconX = rect.X + (rect.Width - iconWidth) * 0.5f;
        float iconY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(icon.AsSpan(), iconX, iconY, fontSize, iconColor);

        return hovered && Im.MousePressed;
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
        StateExpanded[index] = 0;
        return index;
    }
}
