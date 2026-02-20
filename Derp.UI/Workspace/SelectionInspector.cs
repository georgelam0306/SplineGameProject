using System;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using DerpLib.Sdf;
using FontAwesome.Sharp;
using Property;
using Property.Runtime;
using Silk.NET.Input;

namespace Derp.UI;

internal static class SelectionInspector
{
    private static AnyComponentHandle[] _multiComponentScratch = new AnyComponentHandle[256];
    private const float KeyIconWidth = 18f;

    private static readonly string[] EventBindingWidgetIds = new string[]
    {
        "evt_hover_held",
        "evt_hover_enter",
        "evt_hover_exit",
        "evt_press",
        "evt_release",
        "evt_click",
        "evt_child_hover_held",
        "evt_child_hover_enter",
        "evt_child_hover_exit"
    };

    private static string[] _eventBoolVariableOptions = new string[96];
    private static ushort[] _eventBoolVariableOptionIds = new ushort[96];
    private static int _eventBoolVariableOptionCount;

    private static string[] _eventTriggerVariableOptions = new string[96];
    private static ushort[] _eventTriggerVariableOptionIds = new ushort[96];
    private static int _eventTriggerVariableOptionCount;

    private static readonly string AddConstraintButtonLabel = ((char)IconChar.Plus).ToString() + " Add Constraint";
    private static readonly string RemoveConstraintButtonLabel = ((char)IconChar.Trash).ToString();
    private static readonly string ConstraintTargetPickerIcon = ((char)IconChar.ChevronDown).ToString();
    private static readonly string[] ConstraintKindOptions =
    {
        "Match Size",
        "Match Position",
        "Scroll"
    };

    private static readonly string AddWarpButtonLabel = ((char)IconChar.Plus).ToString() + " Add Warp";
    private static readonly string RemoveWarpButtonLabel = ((char)IconChar.Trash).ToString();
    private static readonly string[] WarpTypeOptions =
    {
        "None",
        "Wave",
        "Twist",
        "Bulge",
        "Noise",
        "Lattice",
        "Repeat"
    };

    public static void DrawEntityInspector(UiWorkspace workspace, EntityId entity)
    {
        if (entity.IsNull)
        {
            InspectorCard.Begin("Selection");
            InspectorHint.Draw("Invalid selection");
            InspectorCard.End();
            return;
        }

        ReadOnlySpan<AnyComponentHandle> slots = workspace.World.GetComponentSlots(entity);
        ulong presentMask = workspace.World.GetComponentPresentMask(entity);
        if (slots.IsEmpty || presentMask == 0)
        {
            InspectorCard.Begin("Selection");
            InspectorHint.Draw("No components");
            InspectorCard.End();
            return;
        }

        UiNodeType selectedNodeType = workspace.World.GetNodeType(entity);

        bool isPolygonPath = false;
        if (workspace.World.TryGetComponent(entity, ShapeComponent.Api.PoolIdConst, out AnyComponentHandle shapeAny))
        {
            var shapeHandle = new ShapeComponentHandle(shapeAny.Index, shapeAny.Generation);
            var shapeView = ShapeComponent.Api.FromHandle(workspace.PropertyWorld, shapeHandle);
            if (shapeView.IsAlive && shapeView.Kind == ShapeKind.Polygon)
            {
                isPolygonPath = true;
            }
        }

        AnyComponentHandle computedTransformComponent = default;
        AnyComponentHandle computedSizeComponent = default;
        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (((presentMask >> slotIndex) & 1UL) == 0)
            {
                continue;
            }

            AnyComponentHandle component = slots[slotIndex];
            if (!component.IsValid)
            {
                continue;
            }

            if (component.Kind == ComputedTransformComponent.Api.PoolIdConst)
            {
                computedTransformComponent = component;
            }
            else if (component.Kind == ComputedSizeComponent.Api.PoolIdConst)
            {
                computedSizeComponent = component;
            }
        }

        bool didDrawComputedComponents = false;

        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (((presentMask >> slotIndex) & 1UL) == 0)
            {
                continue;
            }

            AnyComponentHandle component = slots[slotIndex];
            if (component.IsNull)
            {
                continue;
            }

            if (component.Kind == PaintComponent.Api.PoolIdConst &&
                workspace.World.TryGetComponent(entity, PaintComponent.Api.PoolIdConst, out _))
            {
                PaintStackPanel.Draw(workspace, entity);
                continue;
            }

            if (component.Kind == BlendComponent.Api.PoolIdConst)
            {
                string blendLabel = UiComponentKindNames.GetName(component.Kind);
                InspectorCard.Begin(blendLabel);
                var blendHandle = new BlendComponentHandle(component.Index, component.Generation);
                BlendControlsPanel.Draw(workspace, blendHandle);
                InspectorCard.End();
                continue;
            }

            if (component.Kind == PrefabVariablesComponent.Api.PoolIdConst ||
                component.Kind == PrefabInstanceComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (component.Kind == EventListenerComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (component.Kind == DraggableComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (component.Kind == ConstraintListComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (component.Kind == ModifierStackComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (selectedNodeType == UiNodeType.PrefabInstance &&
                component.Kind == PrefabCanvasComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (component.Kind == PrefabListDataComponent.Api.PoolIdConst ||
                component.Kind == ListGeneratedComponent.Api.PoolIdConst ||
                component.Kind == PrefabInstancePropertyOverridesComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (component.Kind == PrefabBindingsComponent.Api.PoolIdConst ||
                component.Kind == PrefabInstanceBindingCacheComponent.Api.PoolIdConst ||
                component.Kind == PrefabRevisionComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (component.Kind == ComputedTransformComponent.Api.PoolIdConst ||
                component.Kind == ComputedSizeComponent.Api.PoolIdConst)
            {
                continue;
            }

            string label = UiComponentKindNames.GetName(component.Kind);
            InspectorCard.Begin(label);
            if (isPolygonPath && component.Kind == PathComponent.Api.PoolIdConst)
            {
                bool isEditingPath = workspace.IsEditingPathForEntity(entity);
                string buttonLabel = isEditingPath ? "Finish Edit" : "Edit Path";
                if (InspectorButtonRow.Draw("polygon_path_edit", buttonLabel))
                {
                    workspace.TogglePathEditModeFromInspector(entity);
                }

                if (isEditingPath)
                {
                    InspectorHint.Draw("Alt+drag vertex: curve. Shift: break tangents. Enter: finish, Esc: cancel.");
                }
            }
            workspace.PropertyInspector.DrawComponentPropertiesContent(entity, label, component);
            InspectorCard.End();

            if (component.Kind == TransformComponent.Api.PoolIdConst && !didDrawComputedComponents)
            {
                if (!computedTransformComponent.IsNull)
                {
                    string computedLabel = UiComponentKindNames.GetName(computedTransformComponent.Kind);
                    InspectorCard.Begin(computedLabel);
                    workspace.PropertyInspector.DrawComponentPropertiesContent(entity, computedLabel, computedTransformComponent);
                    InspectorCard.End();
                }

                if (!computedSizeComponent.IsNull)
                {
                    string computedLabel = UiComponentKindNames.GetName(computedSizeComponent.Kind);
                    InspectorCard.Begin(computedLabel);
                    workspace.PropertyInspector.DrawComponentPropertiesContent(entity, computedLabel, computedSizeComponent);
                    InspectorCard.End();
                }

                didDrawComputedComponents = true;
            }
        }

        if (!didDrawComputedComponents && (!computedTransformComponent.IsNull || !computedSizeComponent.IsNull))
        {
            if (!computedTransformComponent.IsNull)
            {
                string computedLabel = UiComponentKindNames.GetName(computedTransformComponent.Kind);
                InspectorCard.Begin(computedLabel);
                workspace.PropertyInspector.DrawComponentPropertiesContent(entity, computedLabel, computedTransformComponent);
                InspectorCard.End();
            }

            if (!computedSizeComponent.IsNull)
            {
                string computedLabel = UiComponentKindNames.GetName(computedSizeComponent.Kind);
                InspectorCard.Begin(computedLabel);
                workspace.PropertyInspector.DrawComponentPropertiesContent(entity, computedLabel, computedSizeComponent);
                InspectorCard.End();
            }
        }

        if (selectedNodeType == UiNodeType.Shape || selectedNodeType == UiNodeType.BooleanGroup || selectedNodeType == UiNodeType.Text)
        {
            ImLayout.Space(12f);
            DrawEventsInspector(workspace, entity);
        }

        if (selectedNodeType == UiNodeType.Shape || selectedNodeType == UiNodeType.BooleanGroup || selectedNodeType == UiNodeType.Text || selectedNodeType == UiNodeType.PrefabInstance)
        {
            ImLayout.Space(12f);
            DrawConstraintsInspector(workspace, entity);
        }

        if (selectedNodeType == UiNodeType.Shape || selectedNodeType == UiNodeType.BooleanGroup || selectedNodeType == UiNodeType.Text || selectedNodeType == UiNodeType.Prefab || selectedNodeType == UiNodeType.PrefabInstance)
        {
            ImLayout.Space(12f);
            DrawModifiersInspector(workspace, entity);
        }

        if (selectedNodeType == UiNodeType.Shape || selectedNodeType == UiNodeType.BooleanGroup || selectedNodeType == UiNodeType.Prefab)
        {
            ImLayout.Space(12f);
            DrawDraggableInspector(workspace, entity);
        }
    }

    private static void DrawDraggableInspector(UiWorkspace workspace, EntityId entity)
    {
        EntityId owningPrefab = workspace.FindOwningPrefabEntity(entity);
        if (owningPrefab.IsNull)
        {
            return;
        }

        if (!workspace.World.TryGetComponent(entity, DraggableComponent.Api.PoolIdConst, out AnyComponentHandle dragAny) || !dragAny.IsValid)
        {
            return;
        }

        var dragHandle = new DraggableComponentHandle(dragAny.Index, dragAny.Generation);
        var drag = DraggableComponent.Api.FromHandle(workspace.PropertyWorld, dragHandle);
        if (!drag.IsAlive)
        {
            return;
        }

        InspectorCard.Begin("Runtime Drag");

        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Enabled".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        bool enabled = drag.Enabled;
        float x = rowRect.X + labelWidth;
        float y = rowRect.Y + (rowRect.Height - Im.Style.CheckboxSize) * 0.5f;
        bool changed = Im.Checkbox("drag_enabled", ref enabled, x, y);
        if (changed)
        {
            drag.Enabled = enabled;
            workspace.BumpPrefabRevision(owningPrefab);
        }

        InspectorHint.Draw("When enabled, Play mode input can take over from editor gizmos (e.g. scroll handles).");
        InspectorCard.End();
    }

    private static void DrawEventsInspector(UiWorkspace workspace, EntityId entity)
    {
        EntityId owningPrefab = workspace.FindOwningPrefabEntity(entity);
        if (owningPrefab.IsNull)
        {
            return;
        }

        EnsureEventVariableDropdownOptions(workspace, owningPrefab, PropertyKind.Bool, ref _eventBoolVariableOptions, ref _eventBoolVariableOptionIds, out _eventBoolVariableOptionCount);
        EnsureEventVariableDropdownOptions(workspace, owningPrefab, PropertyKind.Trigger, ref _eventTriggerVariableOptions, ref _eventTriggerVariableOptionIds, out _eventTriggerVariableOptionCount);

        bool hasListener = workspace.World.TryGetComponent(entity, EventListenerComponent.Api.PoolIdConst, out AnyComponentHandle listenerAny) && listenerAny.IsValid;
        EventListenerComponent.ViewProxy listener = default;
        if (hasListener)
        {
            var listenerHandle = new EventListenerComponentHandle(listenerAny.Index, listenerAny.Generation);
            listener = EventListenerComponent.Api.FromHandle(workspace.PropertyWorld, listenerHandle);
            hasListener = listener.IsAlive;
        }

        InspectorCard.Begin("Events");

        bool hover = hasListener && listener.HoverVarId != 0;
        bool trigger = hasListener &&
                       (listener.HoverEnterTriggerId != 0 || listener.HoverExitTriggerId != 0 ||
                        listener.PressTriggerId != 0 || listener.ReleaseTriggerId != 0 || listener.ClickTriggerId != 0);
        bool child = hasListener && (listener.ChildHoverVarId != 0 || listener.ChildHoverEnterTriggerId != 0 || listener.ChildHoverExitTriggerId != 0);

        if (!hasListener && _eventBoolVariableOptionCount <= 1 && _eventTriggerVariableOptionCount <= 1)
        {
            InspectorHint.Draw("Add a Bool or Trigger prefab variable to bind events.");
            InspectorCard.End();
            return;
        }

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 0,
            label: "Hover (Held)",
            options: _eventBoolVariableOptions,
            optionIds: _eventBoolVariableOptionIds,
            optionCount: _eventBoolVariableOptionCount,
            currentValue: hasListener ? listener.HoverVarId : 0);

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 1,
            label: "Hover Enter (Trigger)",
            options: _eventTriggerVariableOptions,
            optionIds: _eventTriggerVariableOptionIds,
            optionCount: _eventTriggerVariableOptionCount,
            currentValue: hasListener ? listener.HoverEnterTriggerId : 0);

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 2,
            label: "Hover Exit (Trigger)",
            options: _eventTriggerVariableOptions,
            optionIds: _eventTriggerVariableOptionIds,
            optionCount: _eventTriggerVariableOptionCount,
            currentValue: hasListener ? listener.HoverExitTriggerId : 0);

        ImLayout.Space(6f);

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 3,
            label: "Press (Trigger)",
            options: _eventTriggerVariableOptions,
            optionIds: _eventTriggerVariableOptionIds,
            optionCount: _eventTriggerVariableOptionCount,
            currentValue: hasListener ? listener.PressTriggerId : 0);

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 4,
            label: "Release (Trigger)",
            options: _eventTriggerVariableOptions,
            optionIds: _eventTriggerVariableOptionIds,
            optionCount: _eventTriggerVariableOptionCount,
            currentValue: hasListener ? listener.ReleaseTriggerId : 0);

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 5,
            label: "Click (Trigger)",
            options: _eventTriggerVariableOptions,
            optionIds: _eventTriggerVariableOptionIds,
            optionCount: _eventTriggerVariableOptionCount,
            currentValue: hasListener ? listener.ClickTriggerId : 0);

        ImLayout.Space(6f);

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 6,
            label: "Child Hover (Held)",
            options: _eventBoolVariableOptions,
            optionIds: _eventBoolVariableOptionIds,
            optionCount: _eventBoolVariableOptionCount,
            currentValue: hasListener ? listener.ChildHoverVarId : 0);

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 7,
            label: "Child Hover Enter (Trigger)",
            options: _eventTriggerVariableOptions,
            optionIds: _eventTriggerVariableOptionIds,
            optionCount: _eventTriggerVariableOptionCount,
            currentValue: hasListener ? listener.ChildHoverEnterTriggerId : 0);

        DrawEventBindingDropdown(
            workspace,
            entity,
            ref listenerAny,
            propertyIndex: 8,
            label: "Child Hover Exit (Trigger)",
            options: _eventTriggerVariableOptions,
            optionIds: _eventTriggerVariableOptionIds,
            optionCount: _eventTriggerVariableOptionCount,
            currentValue: hasListener ? listener.ChildHoverExitTriggerId : 0);

        if (!hover && !trigger && !child)
        {
            InspectorHint.Draw("Bind a prefab variable to enable runtime events.");
        }

        InspectorCard.End();
    }

    private static void DrawModifiersInspector(UiWorkspace workspace, EntityId entity)
    {
        EntityId owningPrefab = workspace.FindOwningPrefabEntity(entity);
        if (owningPrefab.IsNull)
        {
            return;
        }

        if (!workspace.World.TryGetComponent(entity, ModifierStackComponent.Api.PoolIdConst, out AnyComponentHandle modifiersAny) || !modifiersAny.IsValid)
        {
            return;
        }

        var modifiersHandle = new ModifierStackComponentHandle(modifiersAny.Index, modifiersAny.Generation);
        var modifiers = ModifierStackComponent.Api.FromHandle(workspace.PropertyWorld, modifiersHandle);
        if (!modifiers.IsAlive)
        {
            return;
        }

        InspectorCard.Begin("Modifiers");

        var addRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        addRect = InspectorRow.GetPaddedRect(addRect);
        if (Im.Button(AddWarpButtonLabel, addRect.X, addRect.Y, addRect.Width, addRect.Height))
        {
            _ = TryAddWarp(workspace, owningPrefab, modifiers);
        }

        ushort count = modifiers.Count;
        if (count == 0)
        {
            ImLayout.Space(6f);
            InspectorHint.Draw("Add warps to distort SDF rendering (applies to this layer and children).");
            InspectorCard.End();
            return;
        }

        if (count > ModifierStackComponent.MaxWarps)
        {
            count = ModifierStackComponent.MaxWarps;
        }

        var ctx = Im.Context;
        for (int warpIndex = 0; warpIndex < count; warpIndex++)
        {
            ctx.PushId(warpIndex);

            ImLayout.Space(8f);
            DrawWarpHeaderRow(workspace, owningPrefab, modifiers, warpIndex);
            DrawWarpEnabledRow(modifiers, warpIndex, owningPrefab, workspace);
            DrawWarpTypeRow(modifiers, warpIndex, owningPrefab, workspace);

            byte typeByte = modifiers.TypeValueReadOnlySpan()[warpIndex];
            if (typeByte > (byte)SdfWarpType.Repeat)
            {
                typeByte = 0;
            }

            var warpType = (SdfWarpType)typeByte;
            DrawWarpParams(workspace, modifiersAny, warpIndex, warpType);

            ctx.PopId();
        }

        InspectorCard.End();
    }

    private static void DrawWarpHeaderRow(UiWorkspace workspace, EntityId owningPrefab, ModifierStackComponent.ViewProxy modifiers, int warpIndex)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Warp".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float buttonSize = MathF.Min(Im.Style.MinButtonHeight, rowRect.Height);
        float removeX = rowRect.Right - buttonSize;
        if (Im.Button(RemoveWarpButtonLabel, removeX, rowRect.Y, buttonSize, rowRect.Height))
        {
            _ = TryRemoveWarpAt(workspace, owningPrefab, modifiers, warpIndex);
        }
    }

    private static void DrawWarpEnabledRow(ModifierStackComponent.ViewProxy modifiers, int warpIndex, EntityId owningPrefab, UiWorkspace workspace)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Enabled".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        Span<byte> enabledSpan = modifiers.EnabledValueSpan();
        bool value = enabledSpan[warpIndex] != 0;
        float x = rowRect.X + labelWidth;
        float y = rowRect.Y + (rowRect.Height - Im.Style.CheckboxSize) * 0.5f;

        bool changed = Im.Checkbox("warp_enabled", ref value, x, y);
        if (changed)
        {
            enabledSpan[warpIndex] = value ? (byte)1 : (byte)0;
            workspace.BumpPrefabRevision(owningPrefab);
        }
    }

    private static void DrawWarpTypeRow(ModifierStackComponent.ViewProxy modifiers, int warpIndex, EntityId owningPrefab, UiWorkspace workspace)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float inputWidth = MathF.Max(120f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Type".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        Span<byte> typeSpan = modifiers.TypeValueSpan();
        int selectedIndex = (int)Math.Clamp(typeSpan[warpIndex], (byte)0, (byte)(WarpTypeOptions.Length - 1));
        bool changed = Im.Dropdown("warp_type", WarpTypeOptions.AsSpan(), ref selectedIndex, rowRect.X + labelWidth, rowRect.Y, inputWidth);
        if (changed)
        {
            typeSpan[warpIndex] = (byte)selectedIndex;
            workspace.BumpPrefabRevision(owningPrefab);
        }
    }

    private static void DrawWarpParams(UiWorkspace workspace, AnyComponentHandle modifiersComponent, int warpIndex, SdfWarpType warpType)
    {
        if (warpType == SdfWarpType.None)
        {
            return;
        }

        if (warpType == SdfWarpType.Wave)
        {
            PropertySlot frequencySlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param1Value", warpIndex, PropertyKind.Float);
            PropertySlot amplitudeSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param2Value", warpIndex, PropertyKind.Float);
            PropertySlot phaseSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param3Value", warpIndex, PropertyKind.Float);

            float frequency = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, frequencySlot);
            float amplitude = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, amplitudeSlot);
            float phase = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, phaseSlot);

            _ = DrawFloatRowWithKey(workspace, "Frequency", "warp_p1", frequencySlot, ref frequency, minValue: 0f, maxValue: 500f, format: "F2");
            _ = DrawFloatRowWithKey(workspace, "Amplitude", "warp_p2", amplitudeSlot, ref amplitude, minValue: -500f, maxValue: 500f, format: "F2");
            _ = DrawFloatRowWithKey(workspace, "Phase", "warp_p3", phaseSlot, ref phase, minValue: -500f, maxValue: 500f, format: "F2");
            return;
        }

        if (warpType == SdfWarpType.Twist)
        {
            PropertySlot strengthSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param1Value", warpIndex, PropertyKind.Float);
            float strength = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, strengthSlot);
            _ = DrawFloatRowWithKey(workspace, "Strength", "warp_p1", strengthSlot, ref strength, minValue: -50f, maxValue: 50f, format: "F3");
            return;
        }

        if (warpType == SdfWarpType.Bulge)
        {
            PropertySlot strengthSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param1Value", warpIndex, PropertyKind.Float);
            PropertySlot radiusSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param2Value", warpIndex, PropertyKind.Float);

            float strength = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, strengthSlot);
            float radius = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, radiusSlot);

            _ = DrawFloatRowWithKey(workspace, "Strength", "warp_p1", strengthSlot, ref strength, minValue: -20f, maxValue: 20f, format: "F3");
            _ = DrawFloatRowWithKey(workspace, "Radius", "warp_p2", radiusSlot, ref radius, minValue: 0f, maxValue: 10000f, format: "F1");
            return;
        }

        if (warpType == SdfWarpType.Lattice)
        {
            PropertySlot latticeIndexSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param1Value", warpIndex, PropertyKind.Float);
            PropertySlot scaleXSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param2Value", warpIndex, PropertyKind.Float);
            PropertySlot scaleYSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param3Value", warpIndex, PropertyKind.Float);

            float latticeIndex = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, latticeIndexSlot);
            float scaleX = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, scaleXSlot);
            float scaleY = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, scaleYSlot);

            bool changed = DrawFloatRowWithKey(workspace, "Lattice Index", "warp_p1", latticeIndexSlot, ref latticeIndex, minValue: 0f, maxValue: 1024f, format: "F0");
            float rounded = MathF.Round(latticeIndex);
            if (changed && rounded != latticeIndex)
            {
                latticeIndex = rounded;
                int widgetId = Im.Context.GetId("warp_p1");
                workspace.Commands.SetPropertyValue(widgetId, isEditing: false, latticeIndexSlot, PropertyValue.FromFloat(latticeIndex));
            }

            _ = DrawFloatRowWithKey(workspace, "Scale X", "warp_p2", scaleXSlot, ref scaleX, minValue: 0f, maxValue: 10000f, format: "F1");
            _ = DrawFloatRowWithKey(workspace, "Scale Y", "warp_p3", scaleYSlot, ref scaleY, minValue: 0f, maxValue: 10000f, format: "F1");
            return;
        }

        if (warpType == SdfWarpType.Repeat)
        {
            PropertySlot periodXSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param1Value", warpIndex, PropertyKind.Float);
            PropertySlot periodYSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param2Value", warpIndex, PropertyKind.Float);
            PropertySlot offsetSlot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param3Value", warpIndex, PropertyKind.Float);

            float periodX = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, periodXSlot);
            float periodY = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, periodYSlot);
            float offset = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, offsetSlot);

            _ = DrawFloatRowWithKey(workspace, "Period X", "warp_p1", periodXSlot, ref periodX, minValue: 0.001f, maxValue: 10000f, format: "F1");
            _ = DrawFloatRowWithKey(workspace, "Period Y", "warp_p2", periodYSlot, ref periodY, minValue: 0f, maxValue: 10000f, format: "F1");
            _ = DrawFloatRowWithKey(workspace, "Offset", "warp_p3", offsetSlot, ref offset, minValue: -10000f, maxValue: 10000f, format: "F1");
            return;
        }

        PropertySlot param1Slot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param1Value", warpIndex, PropertyKind.Float);
        PropertySlot param2Slot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param2Value", warpIndex, PropertyKind.Float);
        PropertySlot param3Slot = ModifierStackComponentPropertySlot.ArrayElement(modifiersComponent, "Param3Value", warpIndex, PropertyKind.Float);

        float param1 = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, param1Slot);
        float param2 = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, param2Slot);
        float param3 = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, param3Slot);

        _ = DrawFloatRowWithKey(workspace, "Param1", "warp_p1", param1Slot, ref param1, minValue: -10000f, maxValue: 10000f, format: "F2");
        _ = DrawFloatRowWithKey(workspace, "Param2", "warp_p2", param2Slot, ref param2, minValue: -10000f, maxValue: 10000f, format: "F2");
        _ = DrawFloatRowWithKey(workspace, "Param3", "warp_p3", param3Slot, ref param3, minValue: -10000f, maxValue: 10000f, format: "F2");
    }

    private static bool DrawFloatRowWithKey(UiWorkspace workspace, string label, string id, PropertySlot slot, ref float value, float minValue, float maxValue, string format)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float inputWidth = Math.Max(120f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float contentWidth = Math.Max(1f, inputWidth - KeyIconWidth);
        float inputX = rowRect.X + labelWidth;

        bool changed = ImScalarInput.DrawAt(id, inputX, rowRect.Y, contentWidth, ref value, minValue, maxValue, format);
        int widgetId = Im.Context.GetId(id);
        bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
        if (changed)
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromFloat(value));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }

        if (workspace.Commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey))
        {
            var iconRect = new ImRect(inputX + contentWidth, rowRect.Y, KeyIconWidth, rowRect.Height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                workspace.Commands.ToggleKeyAtPlayhead(slot);
            }
        }

        return changed;
    }

    private static void DrawKeyIconDiamond(ImRect rect, bool filled, bool highlighted)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        uint color = highlighted ? Im.Style.Primary : Im.Style.TextSecondary;
        float size = MathF.Min(rect.Width, rect.Height) * 0.46f;
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;

        if (filled)
        {
            AnimationEditorHelpers.DrawFilledDiamond(cx, cy, size, color);
            return;
        }

        float half = size * 0.5f;
        Im.DrawLine(cx, cy - half, cx + half, cy, 1f, color);
        Im.DrawLine(cx + half, cy, cx, cy + half, 1f, color);
        Im.DrawLine(cx, cy + half, cx - half, cy, 1f, color);
        Im.DrawLine(cx - half, cy, cx, cy - half, 1f, color);
    }

    private static bool TryAddWarp(UiWorkspace workspace, EntityId owningPrefab, ModifierStackComponent.ViewProxy modifiers)
    {
        ushort count = modifiers.Count;
        if (count >= ModifierStackComponent.MaxWarps)
        {
            return false;
        }

        int idx = count;
        Span<byte> enabled = modifiers.EnabledValueSpan();
        Span<byte> typeValue = modifiers.TypeValueSpan();
        Span<float> p1 = modifiers.Param1ValueSpan();
        Span<float> p2 = modifiers.Param2ValueSpan();
        Span<float> p3 = modifiers.Param3ValueSpan();

        enabled[idx] = 1;
        typeValue[idx] = (byte)SdfWarpType.Wave;
        p1[idx] = 1f;
        p2[idx] = 8f;
        p3[idx] = 0f;

        modifiers.Count = (ushort)(count + 1);
        workspace.BumpPrefabRevision(owningPrefab);
        return true;
    }

    private static bool TryRemoveWarpAt(UiWorkspace workspace, EntityId owningPrefab, ModifierStackComponent.ViewProxy modifiers, int removeIndex)
    {
        ushort count = modifiers.Count;
        if (count == 0 || (uint)removeIndex >= (uint)count)
        {
            return false;
        }

        Span<byte> enabled = modifiers.EnabledValueSpan();
        Span<byte> typeValue = modifiers.TypeValueSpan();
        Span<float> p1 = modifiers.Param1ValueSpan();
        Span<float> p2 = modifiers.Param2ValueSpan();
        Span<float> p3 = modifiers.Param3ValueSpan();

        int last = count - 1;
        for (int i = removeIndex; i < last; i++)
        {
            enabled[i] = enabled[i + 1];
            typeValue[i] = typeValue[i + 1];
            p1[i] = p1[i + 1];
            p2[i] = p2[i + 1];
            p3[i] = p3[i + 1];
        }

        enabled[last] = 0;
        typeValue[last] = 0;
        p1[last] = 0f;
        p2[last] = 0f;
        p3[last] = 0f;

        modifiers.Count = (ushort)last;
        workspace.BumpPrefabRevision(owningPrefab);
        return true;
    }

    private static void DrawConstraintsInspector(UiWorkspace workspace, EntityId entity)
    {
        EntityId owningPrefab = workspace.FindOwningPrefabEntity(entity);
        if (owningPrefab.IsNull)
        {
            return;
        }

        if (!workspace.World.TryGetComponent(entity, ConstraintListComponent.Api.PoolIdConst, out AnyComponentHandle constraintsAny) || !constraintsAny.IsValid)
        {
            return;
        }

        var constraintsHandle = new ConstraintListComponentHandle(constraintsAny.Index, constraintsAny.Generation);
        var constraints = ConstraintListComponent.Api.FromHandle(workspace.PropertyWorld, constraintsHandle);
        if (!constraints.IsAlive)
        {
            return;
        }

        InspectorCard.Begin("Constraints");

        var addRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        addRect = InspectorRow.GetPaddedRect(addRect);
        if (Im.Button(AddConstraintButtonLabel, addRect.X, addRect.Y, addRect.Width, addRect.Height))
        {
            TryAddConstraint(workspace, owningPrefab, constraints);
        }

        ushort count = constraints.Count;
        if (count == 0)
        {
            ImLayout.Space(6f);
            InspectorHint.Draw("Drag the link icon from Hierarchy onto Target.");
            InspectorCard.End();
            return;
        }

        if (count > ConstraintListComponent.MaxConstraints)
        {
            count = ConstraintListComponent.MaxConstraints;
        }

        var ctx = Im.Context;
        for (int constraintIndex = 0; constraintIndex < count; constraintIndex++)
        {
            ctx.PushId(constraintIndex);

            ImLayout.Space(8f);
            DrawConstraintHeaderRow(workspace, owningPrefab, constraints, constraintIndex);

            DrawConstraintEnabledRow(constraints, constraintIndex, owningPrefab, workspace);
            DrawConstraintKindRow(workspace, entity, constraints, constraintIndex, owningPrefab);

            var kind = (ConstraintListComponent.ConstraintKind)constraints.KindValueReadOnlySpan()[constraintIndex];
            if (kind == ConstraintListComponent.ConstraintKind.Scroll)
            {
                DrawConstraintScrollOptionsRow(constraints, constraintIndex, owningPrefab, workspace);
            }

            DrawConstraintTargetRow(workspace, entity, owningPrefab, constraints, constraintIndex, kind);

            ctx.PopId();
        }

        InspectorCard.End();
    }

    private static void DrawConstraintHeaderRow(UiWorkspace workspace, EntityId owningPrefab, ConstraintListComponent.ViewProxy constraints, int constraintIndex)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Constraint".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float buttonSize = MathF.Min(Im.Style.MinButtonHeight, rowRect.Height);
        float removeX = rowRect.Right - buttonSize;
        if (Im.Button(RemoveConstraintButtonLabel, removeX, rowRect.Y, buttonSize, rowRect.Height))
        {
            TryRemoveConstraintAt(workspace, owningPrefab, constraints, constraintIndex);
        }
    }

    private static void DrawConstraintEnabledRow(ConstraintListComponent.ViewProxy constraints, int constraintIndex, EntityId owningPrefab, UiWorkspace workspace)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Enabled".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        Span<byte> enabledSpan = constraints.EnabledValueSpan();
        bool value = enabledSpan[constraintIndex] != 0;
        float x = rowRect.X + labelWidth;
        float y = rowRect.Y + (rowRect.Height - Im.Style.CheckboxSize) * 0.5f;

        bool changed = Im.Checkbox("constraint_enabled", ref value, x, y);
        if (changed)
        {
            enabledSpan[constraintIndex] = value ? (byte)1 : (byte)0;
            workspace.BumpPrefabRevision(owningPrefab);
        }
    }

    private static void DrawConstraintKindRow(UiWorkspace workspace, EntityId constrainedEntity, ConstraintListComponent.ViewProxy constraints, int constraintIndex, EntityId owningPrefab)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float inputWidth = MathF.Max(120f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Type".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        Span<byte> kindSpan = constraints.KindValueSpan();
        int selectedIndex = (int)Math.Clamp(kindSpan[constraintIndex], (byte)0, (byte)(ConstraintKindOptions.Length - 1));
        bool changed = Im.Dropdown("constraint_kind", ConstraintKindOptions.AsSpan(), ref selectedIndex, rowRect.X + labelWidth, rowRect.Y, inputWidth);
        if (changed)
        {
            kindSpan[constraintIndex] = (byte)selectedIndex;
            if ((ConstraintListComponent.ConstraintKind)selectedIndex == ConstraintListComponent.ConstraintKind.Scroll)
            {
                Span<byte> flagsSpan = constraints.FlagsValueSpan();
                flagsSpan[constraintIndex] = 1;

                if (workspace.World.TryGetComponent(constrainedEntity, DraggableComponent.Api.PoolIdConst, out AnyComponentHandle dragAny) && dragAny.IsValid)
                {
                    var dragHandle = new DraggableComponentHandle(dragAny.Index, dragAny.Generation);
                    var drag = DraggableComponent.Api.FromHandle(workspace.PropertyWorld, dragHandle);
                    if (drag.IsAlive)
                    {
                        drag.Enabled = true;
                    }
                }
            }
            workspace.BumpPrefabRevision(owningPrefab);
        }
    }

    private static void DrawConstraintScrollOptionsRow(ConstraintListComponent.ViewProxy constraints, int constraintIndex, EntityId owningPrefab, UiWorkspace workspace)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Resize Handle".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        Span<byte> flagsSpan = constraints.FlagsValueSpan();
        bool value = (flagsSpan[constraintIndex] & 1) != 0;

        float x = rowRect.X + labelWidth;
        float y = rowRect.Y + (rowRect.Height - Im.Style.CheckboxSize) * 0.5f;
        bool changed = Im.Checkbox("constraint_scroll_resize", ref value, x, y);
        if (changed)
        {
            flagsSpan[constraintIndex] = value ? (byte)1 : (byte)0;
            workspace.BumpPrefabRevision(owningPrefab);
        }

        ImLayout.Space(4f);
        InspectorHint.Draw("Handle = this layer. Track = parent. Object = scroll content. Viewport = Object's parent.");
    }

    private static void DrawConstraintTargetRow(
        UiWorkspace workspace,
        EntityId constrainedEntity,
        EntityId owningPrefab,
        ConstraintListComponent.ViewProxy constraints,
        int constraintIndex,
        ConstraintListComponent.ConstraintKind kind)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float inputWidth = MathF.Max(120f, rowRect.Width - labelWidth);

        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text((kind == ConstraintListComponent.ConstraintKind.Scroll ? "Object" : "Target").AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float x = rowRect.X + labelWidth;
        var inputRect = new ImRect(x, rowRect.Y, inputWidth, rowRect.Height);

        Span<uint> targets = constraints.TargetSourceStableIdSpan();
        uint currentTarget = targets[constraintIndex];

        bool hovered = inputRect.Contains(Im.MousePos);
        if (hovered)
        {
            Im.SetCursor(StandardCursor.Hand);
        }

        uint bg = ImStyle.WithAlpha(Im.Style.Surface, (byte)(hovered ? 220 : 180));
        uint border = hovered ? ImStyle.WithAlpha(Im.Style.Border, (byte)210) : ImStyle.WithAlpha(Im.Style.Border, (byte)140);

        if (EntityReferenceDragDrop.IsDragging && EntityReferenceDragDrop.ThresholdMet && hovered)
        {
            border = ImStyle.WithAlpha(Im.Style.Primary, (byte)220);
        }

        const float corner = 6f;
        Im.DrawRoundedRect(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, corner, bg);
        Im.DrawRoundedRectStroke(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, corner, border, 1.25f);

        float iconAreaWidth = 26f;
        float iconX = inputRect.Right - iconAreaWidth;
        Im.DrawLine(iconX, inputRect.Y + 4f, iconX, inputRect.Bottom - 4f, 1f, ImStyle.WithAlpha(Im.Style.Border, (byte)180));

        string valueText = GetConstraintTargetLabel(workspace, currentTarget);
        var textClipRect = new ImRect(inputRect.X, inputRect.Y, MathF.Max(0f, inputRect.Width - iconAreaWidth), inputRect.Height);
        Im.PushClipRect(textClipRect);
        float valueX = inputRect.X + 8f;
        float valueY = inputRect.Y + (inputRect.Height - Im.Style.FontSize) * 0.5f;
        uint valueColor = currentTarget == 0 ? Im.Style.TextDisabled : Im.Style.TextSecondary;
        Im.Text(valueText.AsSpan(), valueX, valueY, Im.Style.FontSize, valueColor);
        Im.PopClipRect();

        float iconSize = Im.Style.FontSize;
        float iconWidth = Im.MeasureTextWidth(ConstraintTargetPickerIcon.AsSpan(), iconSize);
        float iconTextX = iconX + (iconAreaWidth - iconWidth) * 0.5f;
        float iconTextY = inputRect.Y + (inputRect.Height - iconSize) * 0.5f;
        Im.Text(ConstraintTargetPickerIcon.AsSpan(), iconTextX, iconTextY, iconSize, Im.Style.TextSecondary);

        if (Im.MousePressed && inputRect.Contains(Im.MousePos))
        {
            ConstraintTargetPickerDialog.Open(workspace, constrainedEntity, owningPrefab, constraintIndex);
        }

        TryAcceptConstraintTargetDrop(workspace, constrainedEntity, owningPrefab, constraints, constraintIndex, inputRect);
    }

    private static void TryAcceptConstraintTargetDrop(
        UiWorkspace workspace,
        EntityId constrainedEntity,
        EntityId owningPrefab,
        ConstraintListComponent.ViewProxy constraints,
        int constraintIndex,
        ImRect inputRect)
    {
        if (!EntityReferenceDragDrop.HasActiveDrag || !EntityReferenceDragDrop.IsDragging)
        {
            return;
        }

        if (EntityReferenceDragDrop.ReleaseFrame != Im.Context.FrameCount)
        {
            return;
        }

        if (!inputRect.Contains(Im.MousePos))
        {
            return;
        }

        uint draggedStableId = EntityReferenceDragDrop.StableId;
        if (draggedStableId == 0)
        {
            return;
        }

        EntityId draggedEntity = workspace.World.GetEntityByStableId(draggedStableId);
        if (draggedEntity.IsNull)
        {
            return;
        }

        EntityId draggedOwningPrefab = workspace.FindOwningPrefabEntity(draggedEntity);
        if (draggedOwningPrefab.IsNull || draggedOwningPrefab.Value != owningPrefab.Value)
        {
            return;
        }

        if (!TryGetSourceStableIdForEntity(workspace, draggedEntity, out uint sourceStableId))
        {
            return;
        }

        if (sourceStableId == 0)
        {
            return;
        }

        if (workspace.World.GetStableId(constrainedEntity) == sourceStableId)
        {
            return;
        }

        Span<uint> targets = constraints.TargetSourceStableIdSpan();
        targets[constraintIndex] = sourceStableId;
        workspace.BumpPrefabRevision(owningPrefab);
        EntityReferenceDragDrop.Clear();
    }

    private static bool TryGetSourceStableIdForEntity(UiWorkspace workspace, EntityId entity, out uint sourceStableId)
    {
        sourceStableId = 0;
        if (entity.IsNull)
        {
            return false;
        }

        if (workspace.World.TryGetComponent(entity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) && expandedAny.IsValid)
        {
            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(workspace.PropertyWorld, expandedHandle);
            if (expanded.IsAlive && expanded.SourceNodeStableId != 0)
            {
                sourceStableId = expanded.SourceNodeStableId;
                return true;
            }
        }

        sourceStableId = workspace.World.GetStableId(entity);
        return sourceStableId != 0;
    }

    private static string GetConstraintTargetLabel(UiWorkspace workspace, uint targetStableId)
    {
        if (targetStableId == 0)
        {
            return "(None)";
        }

        if (workspace.TryGetLayerName(targetStableId, out string name))
        {
            return name;
        }

        EntityId entity = workspace.World.GetEntityByStableId(targetStableId);
        if (entity.IsNull)
        {
            return "(Missing " + targetStableId + ")";
        }

        UiNodeType type = workspace.World.GetNodeType(entity);
        if (type == UiNodeType.Shape &&
            workspace.World.TryGetComponent(entity, ShapeComponent.Api.PoolIdConst, out AnyComponentHandle shapeAny) &&
            shapeAny.IsValid)
        {
            var shapeHandle = new ShapeComponentHandle(shapeAny.Index, shapeAny.Generation);
            var shape = ShapeComponent.Api.FromHandle(workspace.PropertyWorld, shapeHandle);
            if (shape.IsAlive)
            {
                string kindLabel = shape.Kind switch
                {
                    ShapeKind.Rect => "Rectangle",
                    ShapeKind.Circle => "Ellipse",
                    ShapeKind.Polygon => "Polygon",
                    _ => "Shape"
                };
                return kindLabel + " (" + targetStableId + ")";
            }
        }

        string typeLabel = type switch
        {
            UiNodeType.Prefab => "Prefab",
            UiNodeType.PrefabInstance => "Prefab Instance",
            UiNodeType.BooleanGroup => "Group",
            UiNodeType.Text => "Text",
            UiNodeType.Shape => "Shape",
            _ => "Node"
        };
        return typeLabel + " (" + targetStableId + ")";
    }

    private static bool TryAddConstraint(UiWorkspace workspace, EntityId owningPrefab, ConstraintListComponent.ViewProxy constraints)
    {
        ushort count = constraints.Count;
        if (count >= ConstraintListComponent.MaxConstraints)
        {
            return false;
        }

        int idx = count;
        Span<byte> enabled = constraints.EnabledValueSpan();
        Span<byte> kind = constraints.KindValueSpan();
        Span<byte> flags = constraints.FlagsValueSpan();
        Span<uint> target = constraints.TargetSourceStableIdSpan();

        enabled[idx] = 1;
        kind[idx] = (byte)ConstraintListComponent.ConstraintKind.MatchTargetSize;
        flags[idx] = 0;
        target[idx] = 0;

        constraints.Count = (ushort)(count + 1);
        workspace.BumpPrefabRevision(owningPrefab);
        return true;
    }

    private static bool TryRemoveConstraintAt(UiWorkspace workspace, EntityId owningPrefab, ConstraintListComponent.ViewProxy constraints, int removeIndex)
    {
        ushort count = constraints.Count;
        if (count == 0 || (uint)removeIndex >= (uint)count)
        {
            return false;
        }

        Span<byte> enabled = constraints.EnabledValueSpan();
        Span<byte> kind = constraints.KindValueSpan();
        Span<byte> flags = constraints.FlagsValueSpan();
        Span<uint> target = constraints.TargetSourceStableIdSpan();

        int last = count - 1;
        for (int i = removeIndex; i < last; i++)
        {
            enabled[i] = enabled[i + 1];
            kind[i] = kind[i + 1];
            flags[i] = flags[i + 1];
            target[i] = target[i + 1];
        }

        enabled[last] = 0;
        kind[last] = 0;
        flags[last] = 0;
        target[last] = 0;

        constraints.Count = (ushort)last;
        workspace.BumpPrefabRevision(owningPrefab);
        return true;
    }

    private static void DrawEventBindingDropdown(
        UiWorkspace workspace,
        EntityId entity,
        ref AnyComponentHandle listenerAny,
        int propertyIndex,
        string label,
        string[] options,
        ushort[] optionIds,
        int optionCount,
        int currentValue)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = MathF.Min(170f, rowRect.Width * 0.45f);
        labelWidth = MathF.Min(labelWidth, MathF.Max(0f, rowRect.Width - 1f));
        float inputWidth = MathF.Max(1f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        ushort currentId = currentValue <= 0 ? (ushort)0 : currentValue >= ushort.MaxValue ? ushort.MaxValue : (ushort)currentValue;
        int selectedIndex = 0;
        for (int i = 0; i < optionCount; i++)
        {
            if (optionIds[i] == currentId)
            {
                selectedIndex = i;
                break;
            }
        }

        string widgetKey = (uint)propertyIndex < (uint)EventBindingWidgetIds.Length ? EventBindingWidgetIds[propertyIndex] : "evt_unknown";
        bool changed = Im.Dropdown(widgetKey, options.AsSpan(0, optionCount), ref selectedIndex, rowRect.X + labelWidth, rowRect.Y, inputWidth);
        if (!changed)
        {
            return;
        }

        ushort nextId = selectedIndex <= 0 ? (ushort)0 : optionIds[selectedIndex];
        if (nextId == currentId)
        {
            return;
        }

        if (!listenerAny.IsValid)
        {
            if (nextId == 0)
            {
                return;
            }

            if (!workspace.Commands.TryEnsureEventListenerComponent(entity, out listenerAny))
            {
                return;
            }
        }

        var listenerComponent = listenerAny;
        var slot = PropertyDispatcher.GetSlot(listenerComponent, (ushort)propertyIndex);
        int widgetId = Im.Context.GetId(widgetKey);
        workspace.Commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(nextId));
    }

    private static void EnsureEventVariableDropdownOptions(
        UiWorkspace workspace,
        EntityId owningPrefab,
        PropertyKind kind,
        ref string[] options,
        ref ushort[] optionIds,
        out int optionCount)
    {
        optionCount = 0;
        EnsureEventVariableDropdownCapacity(PrefabVariablesComponent.MaxVariables + 8, ref options, ref optionIds);

        options[optionCount] = "(None)";
        optionIds[optionCount] = 0;
        optionCount++;

        if (!workspace.World.TryGetComponent(owningPrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            return;
        }

        ushort count = vars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
        ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();
        for (int i = 0; i < count; i++)
        {
            if ((PropertyKind)kinds[i] != kind)
            {
                continue;
            }

            ushort id = ids[i];
            if (id == 0)
            {
                continue;
            }

            StringHandle nameHandle = names[i];
            string name = nameHandle.IsValid ? nameHandle.ToString() : string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                name = "Var " + id;
            }
            else
            {
                name = name + " (" + id + ")";
            }

            options[optionCount] = name;
            optionIds[optionCount] = id;
            optionCount++;

            if (optionCount >= options.Length)
            {
                break;
            }
        }
    }

    private static void EnsureEventVariableDropdownCapacity(int required, ref string[] options, ref ushort[] optionIds)
    {
        if (options.Length >= required && optionIds.Length >= required)
        {
            return;
        }

        int next = Math.Max(required, options.Length * 2);
        Array.Resize(ref options, next);
        Array.Resize(ref optionIds, next);
    }

    public static void DrawBooleanSelectionInspector(UiWorkspace workspace)
    {
        InspectorCard.Begin("Boolean");

        if (!workspace.TryGetBooleanCreateCandidate(out _, out _, out _))
        {
            InspectorHint.Draw("Select 2+ nodes in the same parent.");
            InspectorCard.End();
            return;
        }

        int opIndex = workspace._pendingBooleanOpIndex;
        if (BooleanControlsPanel.DrawCreateOperation(workspace, ref opIndex))
        {
            workspace._pendingBooleanOpIndex = opIndex;
        }

        if (workspace.TryGetAddToGroupCandidate(out int destinationGroupId))
        {
            if (InspectorButtonRow.Draw("add_to_group", "Add To Group"))
            {
                workspace.Commands.AddSelectionToGroup(destinationGroupId);
                InspectorCard.End();
                return;
            }
        }

        string buttonLabel = workspace.SelectionIncludesGroup() ? "Wrap In Boolean" : "Create Boolean";
        if (InspectorButtonRow.Draw("create_boolean", buttonLabel))
        {
            workspace.Commands.CreateBooleanGroupFromSelection(workspace._pendingBooleanOpIndex);
        }

        InspectorCard.End();
    }

    public static void DrawMaskSelectionInspector(UiWorkspace workspace)
    {
        InspectorCard.Begin("Mask");

        if (!workspace.TryGetMaskCreateCandidate(out _, out _, out _))
        {
            InspectorHint.Draw("Select 2+ nodes in the same parent. First selected node is the mask.");
            InspectorCard.End();
            return;
        }

        if (InspectorButtonRow.Draw("create_mask", "Create Mask"))
        {
            workspace.Commands.CreateMaskGroupFromSelection();
        }

        InspectorCard.End();
    }

    public static void DrawGroupSelectionInspector(UiWorkspace workspace)
    {
        InspectorCard.Begin("Group");

        if (!workspace.TryGetGroupCreateCandidate(out _))
        {
            InspectorHint.Draw("Select 2+ nodes in the same parent.");
            InspectorCard.End();
            return;
        }

        if (InspectorButtonRow.Draw("create_group", "Create Group"))
        {
            workspace.Commands.CreateGroupFromSelection();
        }

        InspectorCard.End();
    }

    public static void DrawMultiSelectionInspector(UiWorkspace workspace)
    {
        DrawGroupSelectionInspector(workspace);
        DrawBooleanSelectionInspector(workspace);
        DrawMaskSelectionInspector(workspace);
        DrawMultiSelectionSharedProperties(workspace);
    }

    private static void DrawMultiSelectionSharedProperties(UiWorkspace workspace)
    {
        int selectionCount = workspace._selectedEntities.Count;
        if (selectionCount < 2)
        {
            return;
        }

        EntityId entity0 = workspace._selectedEntities[0];
        if (entity0.IsNull)
        {
            return;
        }

        ulong commonMask = workspace.World.GetComponentPresentMask(entity0);
        for (int i = 1; i < selectionCount; i++)
        {
            EntityId entity = workspace._selectedEntities[i];
            if (entity.IsNull)
            {
                return;
            }

            commonMask &= workspace.World.GetComponentPresentMask(entity);
            if (commonMask == 0)
            {
                return;
            }
        }

        ReadOnlySpan<AnyComponentHandle> slots0 = workspace.World.GetComponentSlots(entity0);
        int slotCount = Math.Min(slots0.Length, 64);
        if (_multiComponentScratch.Length < selectionCount)
        {
            Array.Resize(ref _multiComponentScratch, Math.Max(selectionCount, _multiComponentScratch.Length * 2));
        }

        ImLayout.Space(12f);
        PropertyInspector.DrawInspectorSectionHeader("Shared Properties");

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            if (((commonMask >> slotIndex) & 1UL) == 0)
            {
                continue;
            }

            AnyComponentHandle component0 = slots0[slotIndex];
            if (component0.IsNull)
            {
                continue;
            }

            if (component0.Kind == PaintComponent.Api.PoolIdConst ||
                component0.Kind == BlendComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (PropertyDispatcher.GetPropertyCount(component0) <= 0)
            {
                continue;
            }

            _multiComponentScratch[0] = component0;
            bool allValid = true;
            for (int i = 1; i < selectionCount; i++)
            {
                EntityId entity = workspace._selectedEntities[i];
                ReadOnlySpan<AnyComponentHandle> slots = workspace.World.GetComponentSlots(entity);
                if (slotIndex >= slots.Length)
                {
                    allValid = false;
                    break;
                }

                AnyComponentHandle component = slots[slotIndex];
                if (component.IsNull || component.Kind != component0.Kind)
                {
                    allValid = false;
                    break;
                }

                _multiComponentScratch[i] = component;
            }

            if (!allValid)
            {
                continue;
            }

            string label = UiComponentKindNames.GetName(component0.Kind);
            InspectorCard.Begin(label);
            workspace.PropertyInspector.DrawMultiComponentPropertiesContent(label, _multiComponentScratch.AsSpan(0, selectionCount));
            InspectorCard.End();
        }
    }
}
