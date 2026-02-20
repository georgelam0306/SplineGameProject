using System;
using System.Numerics;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using FontAwesome.Sharp;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class BlendControlsPanel
{
    private static readonly StringHandle VisibleHandle = "Visible";
    private static readonly StringHandle OpacityHandle = "Opacity";
    private static readonly StringHandle BlendModeHandle = "Blend Mode";

    private static readonly string VisibleIcon = ((char)IconChar.Eye).ToString();
    private static readonly string HiddenIcon = ((char)IconChar.EyeSlash).ToString();

    private static readonly string[] BlendModeOptions = new string[]
    {
        "Normal",
        "Darken",
        "Multiply",
        "Color Burn",
        "Lighten",
        "Screen",
        "Color Dodge",
        "Overlay",
        "Soft Light",
        "Hard Light",
        "Difference",
        "Exclusion",
        "Hue",
        "Saturation",
        "Color",
        "Luminosity"
    };

    public static void Draw(UiWorkspace workspace, BlendComponentHandle blendHandle)
    {
        if (blendHandle.IsNull)
        {
            InspectorHint.Draw("No blend component");
            return;
        }

        AnyComponentHandle component = BlendComponentProperties.ToAnyHandle(blendHandle);
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            InspectorHint.Draw("Invalid blend component");
            return;
        }

        PropertySlot visibleSlot = default;
        PropertySlot opacitySlot = default;
        PropertySlot blendModeSlot = default;

        for (int i = 0; i < propertyCount; i++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, i, out PropertyInfo info))
            {
                continue;
            }

            if (info.Name == VisibleHandle)
            {
                visibleSlot = PropertyDispatcher.GetSlot(component, i);
            }
            else if (info.Name == OpacityHandle)
            {
                opacitySlot = PropertyDispatcher.GetSlot(component, i);
            }
            else if (info.Name == BlendModeHandle)
            {
                blendModeSlot = PropertyDispatcher.GetSlot(component, i);
            }
        }

        if (visibleSlot.Component.IsNull || opacitySlot.Component.IsNull || blendModeSlot.Component.IsNull)
        {
            InspectorHint.Draw("Invalid blend component");
            return;
        }

        bool isVisible = PropertyDispatcher.ReadBool(workspace.PropertyWorld, visibleSlot);
        float opacity = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, opacitySlot);
        int blendMode = PropertyDispatcher.ReadInt(workspace.PropertyWorld, blendModeSlot);
        if (blendMode < 0)
        {
            blendMode = 0;
        }
        else if (blendMode >= BlendModeOptions.Length)
        {
            blendMode = BlendModeOptions.Length - 1;
        }

        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = new ImRect(rowRect.X + 8f, rowRect.Y, Math.Max(1f, rowRect.Width - 16f), rowRect.Height);

        float spacing = Im.Style.Spacing;
        float eyeSize = rowRect.Height;
        float opacityWidth = 72f;

        float x = rowRect.X;
        float y = rowRect.Y;

        float eyeX = rowRect.Right - eyeSize;
        var eyeRect = new ImRect(eyeX, y, eyeSize, rowRect.Height);

        float opacityX = eyeRect.X - spacing - opacityWidth;
        var opacityRect = new ImRect(opacityX, y, opacityWidth, rowRect.Height);

        float dropdownWidth = Math.Max(140f, opacityRect.X - spacing - x);
        var dropdownRect = new ImRect(x, y, dropdownWidth, rowRect.Height);

        int dropdownWidgetId = Im.Context.GetId("blend_mode");
        if (Im.Dropdown("blend_mode", BlendModeOptions, ref blendMode, dropdownRect.X, dropdownRect.Y, dropdownRect.Width))
        {
            workspace.Commands.SetPropertyValue(dropdownWidgetId, isEditing: false, blendModeSlot, PropertyValue.FromInt(blendMode));
        }
        workspace.Commands.NotifyPropertyWidgetState(dropdownWidgetId, isEditing: false);

        float opacityPercent = Math.Clamp(opacity, 0f, 1f) * 100f;
        bool opacityChanged = ImScalarInput.DrawAt("blend_opacity", opacityRect.X, opacityRect.Y, opacityRect.Width, ref opacityPercent, 0f, 100f, "F0");
        int opacityWidgetId = Im.Context.GetId("blend_opacity");
        bool isEditingOpacity = Im.Context.IsActive(opacityWidgetId) || Im.Context.IsFocused(opacityWidgetId);
        if (opacityChanged)
        {
            float newOpacity = Math.Clamp(opacityPercent / 100f, 0f, 1f);
            workspace.Commands.SetPropertyValue(opacityWidgetId, isEditingOpacity, opacitySlot, PropertyValue.FromFloat(newOpacity));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(opacityWidgetId, isEditingOpacity);
        }

        float percentTextY = opacityRect.Y + (opacityRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("%".AsSpan(), opacityRect.Right - Im.Style.Padding - 6f, percentTextY, Im.Style.FontSize, Im.Style.TextSecondary);

        bool clickedEye = DrawEyeButton("blend_visible", eyeRect, isVisible);
        if (clickedEye)
        {
            isVisible = !isVisible;
            int visibleWidgetId = Im.Context.GetId("blend_visible");
            workspace.Commands.SetPropertyValue(visibleWidgetId, isEditing: false, visibleSlot, PropertyValue.FromBool(isVisible));
            workspace.Commands.NotifyPropertyWidgetState(visibleWidgetId, isEditing: false);
        }
        else
        {
            int visibleWidgetId = Im.Context.GetId("blend_visible");
            workspace.Commands.NotifyPropertyWidgetState(visibleWidgetId, isEditing: false);
        }
    }

    private static bool DrawEyeButton(string id, ImRect rect, bool isOn)
    {
        var ctx = Im.Context;
        int widgetId = ctx.GetId(id);
        bool hovered = rect.Contains(Im.MousePos);

        if (hovered)
        {
            ctx.SetHot(widgetId);
        }

        if (ctx.IsHot(widgetId) && Im.MousePressed)
        {
            ctx.SetActive(widgetId);
        }

        bool clicked = false;
        if (ctx.IsActive(widgetId) && ctx.Input.MouseReleased)
        {
            if (hovered)
            {
                clicked = true;
            }
            ctx.ClearActive();
        }

        uint background = hovered ? ImStyle.Lerp(Im.Style.Surface, 0xFF000000, 0.20f) : Im.Style.Surface;
        uint border = hovered ? Im.Style.Primary : Im.Style.Border;
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, background);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

        uint color = Im.Style.TextSecondary;
        DrawEyeIcon(rect, isOn, color);
        return clicked;
    }

    private static void DrawEyeIcon(ImRect rect, bool open, uint color)
    {
        DrawCenteredIcon(rect, open ? VisibleIcon : HiddenIcon, color);
    }

    private static void DrawCenteredIcon(ImRect rect, string icon, uint color)
    {
        float fontSize = MathF.Min(Im.Style.FontSize, MathF.Min(rect.Width, rect.Height));
        float iconWidth = Im.MeasureTextWidth(icon.AsSpan(), fontSize);
        float x = rect.X + (rect.Width - iconWidth) * 0.5f;
        float y = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(icon.AsSpan(), x, y, fontSize, color);
    }
}
