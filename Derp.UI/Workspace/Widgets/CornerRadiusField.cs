using System;
using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using FontAwesome.Sharp;
using Silk.NET.Input;

namespace Derp.UI;

internal static class CornerRadiusField
{
    private static readonly string LinkedIcon = ((char)IconChar.Link).ToString();
    private static readonly string UnlinkedIcon = ((char)IconChar.LinkSlash).ToString();

    private static readonly int[] StateIds = new int[128];
    private static readonly byte[] StateExpanded = new byte[128];
    private static int _stateCount;

    public static bool DrawCornerRadius(
        string label,
        string widgetId,
        float labelWidth,
        float inputWidth,
        float minValue,
        float maxValue,
        float rowPaddingX,
        ref Vector4 value)
    {
        return DrawCornerRadius(label, widgetId, labelWidth, inputWidth, minValue, maxValue, rowPaddingX, ref value, mixedMask: 0u);
    }

    public static bool DrawCornerRadius(
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
        return DrawCornerRadius(label, widgetId, labelWidth, inputWidth, minValue, maxValue, rowPaddingX, ref value, mixedMask, out _);
    }

    public static bool DrawCornerRadius(
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
            bool toggleClicked = DrawCornerModeToggle(buttonRect, expanded);
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
            // Top row: TL / TR
            var rectA = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            float y = rectA.Y;
            float x = rectA.X + rowPaddingX;

            float labelTextY = y + (rectA.Height - Im.Style.FontSize) * 0.5f;
            Im.Text(label.AsSpan(), x, labelTextY, Im.Style.FontSize, Im.Style.TextPrimary);

            float inputX = x + labelWidth;
            float vectorWidth = editorWidth;

            Vector2 top = new Vector2(value.X, value.Y);
            changed |= ImVectorInput.DrawAtMixed(widgetId, inputX, y, vectorWidth, rightOverlayWidth: 0f, ref top, mixedMask: mixedMask & 3u, min: minValue, max: maxValue, format: "F2");
            isEditing |=
                ctx.IsActive(id * 31 + 0) || ctx.IsFocused(id * 31 + 0) ||
                ctx.IsActive(id * 31 + 1) || ctx.IsFocused(id * 31 + 1);

            float buttonX = inputX + editorWidth + spacing;
            var buttonRect = new ImRect(buttonX, y, iconSize, rectA.Height);
            bool toggleClicked = DrawCornerModeToggle(buttonRect, expanded);
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

            value.X = top.X;
            value.Y = top.Y;
        }

        {
            // Bottom row: BL / BR (stored as W / Z to match the common TL,TR,BR,BL ordering)
            var rectB = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            float y = rectB.Y;
            float x = rectB.X + rowPaddingX + labelWidth;

            Vector2 bottom = new Vector2(value.W, value.Z);
            uint bottomMixedMask = 0u;
            if ((mixedMask & 8u) != 0u) { bottomMixedMask |= 1u; }
            if ((mixedMask & 4u) != 0u) { bottomMixedMask |= 2u; }
            Im.Context.PushId(id);
            int bottomParentId = Im.Context.GetId("bottom");
            changed |= ImVectorInput.DrawAtMixed("bottom", x, y, editorWidth, rightOverlayWidth: 0f, ref bottom, bottomMixedMask, minValue, maxValue, "F2");
            isEditing |=
                ctx.IsActive(bottomParentId * 31 + 0) || ctx.IsFocused(bottomParentId * 31 + 0) ||
                ctx.IsActive(bottomParentId * 31 + 1) || ctx.IsFocused(bottomParentId * 31 + 1);
            Im.Context.PopId();

            value.W = bottom.X;
            value.Z = bottom.Y;
        }

        return changed;
    }

    private static bool DrawCornerModeToggle(ImRect rect, bool expanded)
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
