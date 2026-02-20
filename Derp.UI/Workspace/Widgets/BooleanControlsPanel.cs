using System;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class BooleanControlsPanel
{
    private static readonly StringHandle SmoothnessHandle = "Smoothness";

    public static bool DrawCreateOperation(UiWorkspace workspace, ref int opIndex)
    {
        return DrawDropdownRow("Operation", "boolean_create_op", UiWorkspace.BooleanOpOptions, ref opIndex);
    }

    public static void DrawGroup(UiWorkspace workspace, int groupId, BooleanGroupComponentHandle componentHandle, ref int opIndex)
    {
        if (DrawDropdownRow("Operation", "boolean_group_op", UiWorkspace.BooleanOpOptions, ref opIndex))
        {
            workspace.Commands.SetBooleanGroupOpIndex(groupId, opIndex);
        }

        if (componentHandle.IsNull)
        {
            InspectorHint.Draw("Missing boolean component");
            return;
        }

        AnyComponentHandle component = BooleanGroupComponentProperties.ToAnyHandle(componentHandle);
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            InspectorHint.Draw("Invalid boolean component");
            return;
        }

        PropertySlot smoothnessSlot = default;
        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out PropertyInfo info))
            {
                continue;
            }

            if (info.Name == SmoothnessHandle && info.Kind == PropertyKind.Float)
            {
                smoothnessSlot = PropertyDispatcher.GetSlot(component, propertyIndex);
                break;
            }
        }

        if (smoothnessSlot.Component.IsNull)
        {
            InspectorHint.Draw("Missing smoothness");
            return;
        }

        float smoothness = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, smoothnessSlot);
        bool changed = DrawFloatRow("Smoothness", "boolean_smoothness", ref smoothness, minValue: 0f, maxValue: 50f, format: "F2");

        int widgetId = Im.Context.GetId("boolean_smoothness");
        bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
        if (changed)
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing, smoothnessSlot, PropertyValue.FromFloat(smoothness));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }
    }

    private static bool DrawDropdownRow(string label, string id, string[] options, ref int selectedIndex)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 92f;
        float inputWidth = Math.Max(120f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        return Im.Dropdown(id, options, ref selectedIndex, rowRect.X + labelWidth, rowRect.Y, inputWidth);
    }

    private static bool DrawFloatRow(string label, string id, ref float value, float minValue, float maxValue, string format)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 92f;
        float inputWidth = Math.Max(120f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        return ImScalarInput.DrawAt(id, rowRect.X + labelWidth, rowRect.Y, inputWidth, ref value, minValue, maxValue, format);
    }
}
