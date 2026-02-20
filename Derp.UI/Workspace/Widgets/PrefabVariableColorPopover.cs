using System;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Widgets;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal sealed class PrefabVariableColorPopover
{
    private const float InspectorPopoverGap = 6f;
    private const float PopoverWidth = 240f;

    private bool _isOpen;
    private bool _editInstance;
    private EntityId _ownerEntity;
    private ushort _variableId;
    private ImRect _anchorRectViewport;
    private ImRect _inspectorContentRectViewport;
    private int _openedFrame;
    private DerpLib.ImGui.Viewport.ImViewport? _viewport;
    private bool _mouseDownStartedInside;

    public void SetInspectorContentRectViewport(ImRect rectViewport)
    {
        if (!_isOpen)
        {
            _inspectorContentRectViewport = rectViewport;
        }
    }

    public bool IsOpenForViewport(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        return _isOpen && _viewport != null && _viewport == viewport;
    }

    public void Open(EntityId ownerEntity, ushort variableId, bool editInstance, ImRect anchorRectViewport)
    {
        _isOpen = true;
        _editInstance = editInstance;
        _ownerEntity = ownerEntity;
        _variableId = variableId;
        _anchorRectViewport = anchorRectViewport;
        _openedFrame = Im.Context.FrameCount;
        _viewport = Im.CurrentViewport;
        _mouseDownStartedInside = false;
    }

    public void Close()
    {
        _isOpen = false;
        _editInstance = false;
        _ownerEntity = EntityId.Null;
        _variableId = 0;
        _viewport = null;
        _mouseDownStartedInside = false;
    }

    public bool CapturesMouse(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        if (!_isOpen || _viewport == null || _viewport != viewport)
        {
            _mouseDownStartedInside = false;
            return false;
        }

        if (!TryGetPopoverRect(viewport, out ImRect popupRect))
        {
            _mouseDownStartedInside = false;
            return false;
        }

        bool hovered = popupRect.Contains(Im.MousePosViewport);
        if (hovered && viewport.Input.MousePressed)
        {
            _mouseDownStartedInside = true;
        }

        if (!viewport.Input.MouseDown)
        {
            _mouseDownStartedInside = false;
        }

        return hovered;
    }

    public void RenderPopover(UiWorkspace workspace)
    {
        if (!_isOpen || _viewport == null)
        {
            return;
        }

        var viewport = Im.CurrentViewport;
        if (viewport == null || viewport != _viewport)
        {
            Close();
            return;
        }

        if (!TryGetPopoverRect(viewport, out ImRect popupRect))
        {
            Close();
            return;
        }

        if (ImPopover.ShouldClose(
                openedFrame: _openedFrame,
                closeOnEscape: true,
                closeOnOutsideButtons: _mouseDownStartedInside ? ImPopoverCloseButtons.None : ImPopoverCloseButtons.Left,
                consumeCloseClick: false,
                requireNoMouseOwner: false,
                useViewportMouseCoordinates: true,
                insideRect: popupRect))
        {
            Close();
            return;
        }

        if (_ownerEntity.IsNull || _variableId == 0)
        {
            Close();
            return;
        }

        uint argb;
        PropertyValue current;
        if (!TryReadCurrentValue(workspace, out current))
        {
            Close();
            return;
        }

        argb = UiColor32.ToArgb(current.Color32);

        var previousLayer = viewport.CurrentLayer;
        var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
        int previousSortKey = drawList.GetSortKey();
        var previousClipRect = drawList.GetClipRect();

        Im.SetDrawLayer(ImDrawLayer.Overlay);
        drawList.SetSortKey(Math.Max(previousSortKey, int.MaxValue - 2_048));
        drawList.ClearClipRect();

        var savedTranslation = Im.CurrentTranslation;
        bool pushedCancelTransform = false;
        if (savedTranslation.X != 0f || savedTranslation.Y != 0f)
        {
            Im.PushTransform(-savedTranslation);
            pushedCancelTransform = true;
        }

        using var popoverOverlayScope = ImPopover.PushOverlayScope(popupRect);

        Im.DrawRoundedRect(popupRect.X + 3f, popupRect.Y + 3f, popupRect.Width, popupRect.Height, Im.Style.CornerRadius, Im.Style.ShadowColor);
        Im.DrawRoundedRect(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, Im.Style.CornerRadius, Im.Style.Background);
        Im.DrawRoundedRectStroke(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

        Im.PushClipRectOverride(new ImRect(0f, 0f, viewport.Size.X, viewport.Size.Y));

        float x = popupRect.X + Im.Style.Padding;
        float y = popupRect.Y + Im.Style.Padding;
        float width = popupRect.Width - Im.Style.Padding * 2f;

        bool popoverHovered = popupRect.Contains(Im.MousePosViewport);
        bool isEditing = popoverHovered && viewport.Input.MouseDown;

        Im.Context.PushId(unchecked((int)_variableId));
        if (ImColorPicker.DrawAt("var_color_picker", x, y, width, ref argb, showAlpha: true))
        {
            var value = PropertyValue.FromColor32(UiColor32.FromArgb(argb));
            int widgetId = Im.Context.GetId("var_color_picker");
            if (_editInstance)
            {
                workspace.Commands.SetPrefabInstanceVariableValue(widgetId, isEditing, _ownerEntity, _variableId, PropertyKind.Color32, value);
            }
            else
            {
                workspace.Commands.SetPrefabVariableDefault(widgetId, isEditing, _ownerEntity, _variableId, PropertyKind.Color32, value);
            }
        }
        else
        {
            int widgetId = Im.Context.GetId("var_color_picker");
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }
        Im.Context.PopId();

        Im.PopClipRect();

        if (pushedCancelTransform)
        {
            Im.PopTransform();
        }

        drawList.SetClipRect(previousClipRect);
        drawList.SetSortKey(previousSortKey);
        Im.SetDrawLayer(previousLayer);
    }

    private bool TryGetPopoverRect(DerpLib.ImGui.Viewport.ImViewport viewport, out ImRect popupRect)
    {
        popupRect = default;
        if (!_isOpen)
        {
            return false;
        }

        float padding = Im.Style.Padding;
        float width = Math.Clamp(PopoverWidth, 200f, Math.Max(200f, viewport.Size.X - InspectorPopoverGap * 2f));
        float contentWidth = MathF.Max(1f, width - padding * 2f);
        float height = padding * 2f + ImColorPickerHeight(contentWidth);

        width = Math.Min(width, viewport.Size.X - InspectorPopoverGap * 2f);
        height = Math.Min(height, viewport.Size.Y - InspectorPopoverGap * 2f);

        popupRect = PlaceLeftPopover(viewport, _anchorRectViewport, width, height);
        return true;
    }

    private ImRect PlaceLeftPopover(DerpLib.ImGui.Viewport.ImViewport viewport, ImRect anchorViewport, float width, float height)
    {
        float x = anchorViewport.X - width - InspectorPopoverGap;
        float y = anchorViewport.Y;

        if (_inspectorContentRectViewport.Width > 0f && _inspectorContentRectViewport.Height > 0f)
        {
            x = _inspectorContentRectViewport.X - InspectorPopoverGap - width;
        }

        if (x < InspectorPopoverGap)
        {
            x = InspectorPopoverGap;
        }
        if (x + width > viewport.Size.X - InspectorPopoverGap)
        {
            x = viewport.Size.X - width - InspectorPopoverGap;
        }
        if (y + height > viewport.Size.Y - InspectorPopoverGap)
        {
            y = viewport.Size.Y - height - InspectorPopoverGap;
        }
        if (y < InspectorPopoverGap)
        {
            y = InspectorPopoverGap;
        }

        return new ImRect(x, y, width, height);
    }

    private static float ImColorPickerHeight(float width)
    {
        // Mirror ImColorPicker layout constants:
        // svSize = width - HueBarWidth - Spacing
        // height = svSize + Spacing + AlphaBarHeight + Spacing + PreviewHeight
        const float hueBarWidth = 20f;
        const float alphaBarHeight = 16f;
        const float previewHeight = 24f;
        const float spacing = 8f;

        float svSize = MathF.Max(1f, width - hueBarWidth - spacing);
        return svSize + spacing + alphaBarHeight + spacing + previewHeight;
    }

    private bool TryReadCurrentValue(UiWorkspace workspace, out PropertyValue value)
    {
        value = default;
        if (_ownerEntity.IsNull || _variableId == 0)
        {
            return false;
        }

        if (!_editInstance)
        {
            if (!workspace.World.TryGetComponent(_ownerEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
            {
                return false;
            }

            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
            if (!vars.IsAlive || vars.VariableCount == 0)
            {
                return false;
            }

            ushort count = vars.VariableCount;
            if (count > PrefabVariablesComponent.MaxVariables)
            {
                count = PrefabVariablesComponent.MaxVariables;
            }

            ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
            ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();
            for (int i = 0; i < count; i++)
            {
                if (ids[i] == _variableId)
                {
                    value = defaults[i];
                    return true;
                }
            }

            return false;
        }

        if (!workspace.World.TryGetComponent(_ownerEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return false;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive)
        {
            return false;
        }

        uint sourcePrefabStableId = instance.SourcePrefabStableId;
        EntityId sourcePrefab = sourcePrefabStableId != 0 ? workspace.World.GetEntityByStableId(sourcePrefabStableId) : EntityId.Null;
        PrefabVariablesComponent.ViewProxy sourceVars = default;
        if (!sourcePrefab.IsNull && workspace.World.TryGetComponent(sourcePrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle sourceVarsAny) && sourceVarsAny.IsValid)
        {
            var srcHandle = new PrefabVariablesComponentHandle(sourceVarsAny.Index, sourceVarsAny.Generation);
            sourceVars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, srcHandle);
        }

        ReadOnlySpan<ushort> instanceVarId = instance.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> instanceValue = instance.ValueReadOnlySpan();
        ushort instanceCount = instance.ValueCount;
        if (instanceCount > PrefabInstanceComponent.MaxVariables)
        {
            instanceCount = PrefabInstanceComponent.MaxVariables;
        }

        ulong overrideMask = instance.OverrideMask;

        for (int i = 0; i < instanceCount; i++)
        {
            if (instanceVarId[i] != _variableId)
            {
                continue;
            }

            bool overridden = (overrideMask & (1UL << i)) != 0;
            if (overridden)
            {
                value = instanceValue[i];
                return true;
            }
            break;
        }

        if (sourceVars.IsAlive && sourceVars.VariableCount > 0)
        {
            ushort count = sourceVars.VariableCount;
            if (count > PrefabVariablesComponent.MaxVariables)
            {
                count = (ushort)PrefabVariablesComponent.MaxVariables;
            }

            ReadOnlySpan<ushort> ids = sourceVars.VariableIdReadOnlySpan();
            ReadOnlySpan<PropertyValue> defaults = sourceVars.DefaultValueReadOnlySpan();
            for (int i = 0; i < count; i++)
            {
                if (ids[i] == _variableId)
                {
                    value = defaults[i];
                    return true;
                }
            }
        }

        return false;
    }
}
