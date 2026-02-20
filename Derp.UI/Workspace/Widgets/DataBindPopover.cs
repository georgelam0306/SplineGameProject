using System;
using System.Numerics;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Widgets;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal sealed class DataBindPopover
{
    private const float InspectorPopoverGap = 6f;
    private const float PopoverWidth = 320f;
    private const float RowHeight = 26f;
    private const float HeaderHeight = 32f;

    // 0 = default (Variable -> Property)
    // 1 = bind to source (Property -> Variable)
    private const byte DirectionBindToSource = 1;

    private bool _isOpen;
    private EntityId _owningPrefab;
    private uint _targetStableId;
    private ushort _targetComponentKind;
    private ulong _propertyId;
    private ushort _propertyIndexHint;
    private PropertyKind _propertyKind;
    private ImRect _anchorRectViewport;
    private ImRect _inspectorContentRectViewport;
    private int _openedFrame;
    private DerpLib.ImGui.Viewport.ImViewport? _viewport;
    private bool _mouseDownStartedInside;
    private string[] _variableDropdownOptions = Array.Empty<string>();
    private ushort[] _variableDropdownOptionIds = Array.Empty<ushort>();
    private int _variableDropdownOptionCount;
    private bool _hasDirectionOverride;
    private byte _directionOverride;

    public void Open(
        EntityId owningPrefab,
        uint targetStableId,
        ushort targetComponentKind,
        in PropertySlot slot,
        ImRect anchorRectViewport)
    {
        _isOpen = true;
        _owningPrefab = owningPrefab;
        _targetStableId = targetStableId;
        _targetComponentKind = targetComponentKind;
        _propertyId = slot.PropertyId;
        _propertyIndexHint = slot.PropertyIndex;
        _propertyKind = slot.Kind;
        _anchorRectViewport = anchorRectViewport;
        _openedFrame = Im.Context.FrameCount;
        _viewport = Im.CurrentViewport;
        _mouseDownStartedInside = false;
        _variableDropdownOptionCount = 0;
        _hasDirectionOverride = false;
        _directionOverride = 0;
    }

    public void Close()
    {
        _isOpen = false;
        _owningPrefab = EntityId.Null;
        _targetStableId = 0;
        _targetComponentKind = 0;
        _propertyId = 0;
        _propertyIndexHint = 0;
        _propertyKind = default;
        _viewport = null;
        _mouseDownStartedInside = false;
        _variableDropdownOptionCount = 0;
        _hasDirectionOverride = false;
        _directionOverride = 0;
    }

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
        if (viewport == null)
        {
            Close();
            return;
        }

        if (_viewport != viewport)
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

        var previousLayer = viewport.CurrentLayer;
        var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
        int previousSortKey = drawList.GetSortKey();
        var previousClipRect = drawList.GetClipRect();

        Im.SetDrawLayer(ImDrawLayer.Overlay);
        drawList.SetSortKey(Math.Max(previousSortKey, int.MaxValue - 16_384));
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
        float contentWidth = popupRect.Width - Im.Style.Padding * 2f;

        DrawHeader(x, y, contentWidth);
        y += HeaderHeight;

        DrawSectionLabel("Property", x, y);
        y += RowHeight;

        DrawPropertyName(workspace, x, y, contentWidth);
        y += RowHeight + 6f;

        DrawSectionLabel("Variable", x, y);
        y += RowHeight;

        DrawVariableDropdown(workspace, x, y, contentWidth);
        y += RowHeight + 6f;

        DrawSectionLabel("Direction", x, y);
        y += RowHeight;

        DrawDirectionOptions(workspace, x, y, contentWidth);

        Im.PopClipRect();

        if (pushedCancelTransform)
        {
            Im.PopTransform();
        }

        drawList.SetClipRect(previousClipRect);
        drawList.SetSortKey(previousSortKey);
        Im.SetDrawLayer(previousLayer);
    }

    private static void DrawHeader(float x, float y, float width)
    {
        var headerRect = new ImRect(x, y, width, HeaderHeight);
        Im.Text("Data Bind".AsSpan(), headerRect.X, headerRect.Y + (HeaderHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextPrimary);
        Im.DrawLine(headerRect.X, headerRect.Bottom, headerRect.Right, headerRect.Bottom, 1f, Im.Style.Border);
    }

    private void DrawPropertyName(UiWorkspace workspace, float x, float y, float width)
    {
        string label = "Property";

        EntityId target = workspace.World.GetEntityByStableId(_targetStableId);
        if (!target.IsNull && workspace.World.TryGetComponent(target, _targetComponentKind, out AnyComponentHandle componentAny) && componentAny.IsValid)
        {
            if (TryResolveSlot(componentAny, _propertyIndexHint, _propertyId, _propertyKind, out PropertySlot resolved) &&
                PropertyDispatcher.TryGetInfo(resolved.Component, resolved.PropertyIndex, out PropertyInfo info) &&
                info.Name.IsValid)
            {
                label = info.Name.ToString();
            }
        }

        Im.Text(label.AsSpan(), x, y + (RowHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextSecondary);
    }

    private void DrawVariableDropdown(UiWorkspace workspace, float x, float y, float width)
    {
        ushort boundVariableId = GetCurrentBindingDirection(workspace, out byte boundDirection);
        byte desiredDirection = GetDesiredDirection(boundVariableId, boundDirection);

        EnsureVariableDropdownOptions(workspace);

        if (_hasDirectionOverride && desiredDirection != boundDirection)
        {
            boundVariableId = 0;
        }

        int selectedIndex = -1;
        for (int i = 0; i < _variableDropdownOptionCount; i++)
        {
            if (_variableDropdownOptionIds[i] == boundVariableId)
            {
                selectedIndex = i;
                break;
            }
        }

        Im.Context.PushId(unchecked((int)_propertyId));
        bool changed = Im.Dropdown("db_variable", _variableDropdownOptions.AsSpan(0, _variableDropdownOptionCount), ref selectedIndex, x, y, width);
        Im.Context.PopId();
        if (!changed)
        {
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= _variableDropdownOptionCount)
        {
            return;
        }

        ushort nextVariableId = _variableDropdownOptionIds[selectedIndex];
        if (nextVariableId == 0 || nextVariableId == boundVariableId)
        {
            return;
        }

        workspace.Commands.SetPrefabPropertyBinding(
            _owningPrefab,
            _targetStableId,
            _targetComponentKind,
            new PropertySlot(default, _propertyIndexHint, _propertyId, _propertyKind),
            variableId: nextVariableId,
            direction: desiredDirection);

        _hasDirectionOverride = false;
    }

    private void DrawDirectionOptions(UiWorkspace workspace, float x, float y, float width)
    {
        ushort boundVariableId = GetCurrentBindingDirection(workspace, out byte boundDirection);
        byte desiredDirection = GetDesiredDirection(boundVariableId, boundDirection);

        DrawDirectionRow(workspace, x, y, width, "Bind to source", DirectionBindToSource, boundVariableId, boundDirection, ref desiredDirection, enabled: true);
    }

    private void DrawDirectionRow(UiWorkspace workspace, float x, float y, float width, string label, byte value, ushort boundVariableId, byte boundDirection, ref byte current, bool enabled)
    {
        var rowRect = new ImRect(x, y, width, RowHeight);
        bool hovered = rowRect.Contains(Im.MousePosViewport);
        if (hovered)
        {
            Im.DrawRoundedRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.CornerRadius, Im.Style.Hover);
        }

        bool isChecked = current == value;
        float checkboxY = rowRect.Y + (RowHeight - Im.Style.CheckboxSize) * 0.5f;
        float checkboxX = rowRect.X + Im.Style.Padding;

        uint textColor = enabled ? Im.Style.TextPrimary : Im.Style.TextDisabled;

        if (!enabled)
        {
            Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.Background);
        }

        bool clicked = false;
        if (enabled)
        {
            Im.Context.PushId(value);
            clicked = Im.Checkbox("dir", ref isChecked, checkboxX, checkboxY);
            Im.Context.PopId();
        }
        else
        {
            bool dummy = isChecked;
            Im.Context.PushId(unchecked((int)value + 1024));
            Im.Checkbox("dir", ref dummy, checkboxX, checkboxY);
            Im.Context.PopId();
        }

        Im.Text(label.AsSpan(), checkboxX + Im.Style.CheckboxSize + Im.Style.Spacing, rowRect.Y + (RowHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, textColor);

        if (!clicked)
        {
            return;
        }

        byte nextDirection = isChecked ? value : (byte)0;
        if (current == nextDirection)
        {
            return;
        }

        current = nextDirection;
        _hasDirectionOverride = true;
        _directionOverride = current;

        if (boundVariableId != 0)
        {
            // Update the direction of the existing property binding immediately.
            workspace.Commands.SetPrefabPropertyBinding(
                _owningPrefab,
                _targetStableId,
                _targetComponentKind,
                new PropertySlot(default, _propertyIndexHint, _propertyId, _propertyKind),
                variableId: boundVariableId,
                direction: current);

            _hasDirectionOverride = false;
        }
    }

    private ushort GetCurrentBindingDirection(UiWorkspace workspace, out byte direction)
    {
        direction = 0;

        if (_owningPrefab.IsNull || !workspace.World.TryGetComponent(_owningPrefab, PrefabBindingsComponent.Api.PoolIdConst, out AnyComponentHandle bindingsAny) || !bindingsAny.IsValid)
        {
            return 0;
        }

        var bindingsHandle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
        var bindings = PrefabBindingsComponent.Api.FromHandle(workspace.PropertyWorld, bindingsHandle);
        if (!bindings.IsAlive || bindings.BindingCount == 0)
        {
            return 0;
        }

        ushort bindingCount = bindings.BindingCount;
        if (bindingCount > PrefabBindingsComponent.MaxBindings)
        {
            bindingCount = PrefabBindingsComponent.MaxBindings;
        }

        ReadOnlySpan<uint> targetNode = bindings.TargetSourceNodeStableIdReadOnlySpan();
        ReadOnlySpan<ushort> targetComponent = bindings.TargetComponentKindReadOnlySpan();
        ReadOnlySpan<ulong> propertyId = bindings.PropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> varId = bindings.VariableIdReadOnlySpan();
        ReadOnlySpan<byte> directions = bindings.DirectionReadOnlySpan();

        for (int i = 0; i < bindingCount; i++)
        {
            if (targetNode[i] == _targetStableId && targetComponent[i] == _targetComponentKind && propertyId[i] == _propertyId)
            {
                direction = directions[i];
                return varId[i];
            }
        }

        return 0;
    }

    private static void DrawSectionLabel(string label, float x, float y)
    {
        Im.Text(label.AsSpan(), x, y + (RowHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextDisabled);
    }

    private static bool TryResolveSlot(AnyComponentHandle component, ushort propertyIndexHint, ulong propertyId, PropertyKind kind, out PropertySlot slot)
    {
        slot = default;

        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        int hint = propertyIndexHint;
        if (hint >= 0 && hint < propertyCount && PropertyDispatcher.TryGetInfo(component, hint, out PropertyInfo infoHint))
        {
            if (infoHint.PropertyId == propertyId && infoHint.Kind == kind)
            {
                slot = PropertyDispatcher.GetSlot(component, hint);
                return true;
            }
        }

        for (int i = 0; i < propertyCount; i++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, i, out PropertyInfo info))
            {
                continue;
            }

            if (info.PropertyId == propertyId && info.Kind == kind)
            {
                slot = PropertyDispatcher.GetSlot(component, i);
                return true;
            }
        }

        return false;
    }

    private bool TryGetPopoverRect(DerpLib.ImGui.Viewport.ImViewport viewport, out ImRect rect)
    {
        rect = ImRect.Zero;

        float height =
            Im.Style.Padding * 2f +
            HeaderHeight +
            RowHeight + // Property label
            RowHeight + // Property name
            6f +
            RowHeight + // Variable label
            RowHeight + // Variable dropdown
            6f +
            RowHeight + // Direction label
            RowHeight;
        float width = PopoverWidth;

        rect = PlacePopover(viewport, _anchorRectViewport, width, height);
        return true;
    }

    private ImRect PlacePopover(DerpLib.ImGui.Viewport.ImViewport viewport, ImRect anchorViewport, float width, float height)
    {
        float x = anchorViewport.Right + InspectorPopoverGap;
        float y = anchorViewport.Y;

        if (_inspectorContentRectViewport.Width > 0f && _inspectorContentRectViewport.Height > 0f)
        {
            x = _inspectorContentRectViewport.X - InspectorPopoverGap - width;
        }
        else if (x + width > viewport.Size.X - InspectorPopoverGap)
        {
            x = anchorViewport.X - width - InspectorPopoverGap;
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

    private byte GetDesiredDirection(ushort boundVariableId, byte boundDirection)
    {
        if (_hasDirectionOverride)
        {
            return _directionOverride;
        }

        if (boundVariableId != 0)
        {
            return boundDirection;
        }

        return 0;
    }

    private void EnsureVariableDropdownOptions(UiWorkspace workspace)
    {
        _variableDropdownOptionCount = 0;

        if (_owningPrefab.IsNull ||
            workspace.World.GetNodeType(_owningPrefab) != UiNodeType.Prefab ||
            !workspace.World.TryGetComponent(_owningPrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) ||
            !varsAny.IsValid)
        {
            EnsureVariableDropdownCapacity(1);
            _variableDropdownOptions[0] = "(no variables)";
            _variableDropdownOptionIds[0] = 0;
            _variableDropdownOptionCount = 1;
            return;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            EnsureVariableDropdownCapacity(1);
            _variableDropdownOptions[0] = "(no variables)";
            _variableDropdownOptionIds[0] = 0;
            _variableDropdownOptionCount = 1;
            return;
        }

        ushort count = vars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = (ushort)PrefabVariablesComponent.MaxVariables;
        }

        EnsureVariableDropdownCapacity(count);

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();

        for (int i = 0; i < count; i++)
        {
            ushort id = ids[i];
            if (id == 0)
            {
                continue;
            }

            PropertyKind kind = (PropertyKind)kinds[i];
            if (kind != _propertyKind)
            {
                continue;
            }

            string name = names[i].IsValid ? names[i].ToString() : "Var";
            _variableDropdownOptions[_variableDropdownOptionCount] = name;
            _variableDropdownOptionIds[_variableDropdownOptionCount] = id;
            _variableDropdownOptionCount++;
        }

        if (_variableDropdownOptionCount == 0)
        {
            EnsureVariableDropdownCapacity(1);
            _variableDropdownOptions[0] = "No matching variables";
            _variableDropdownOptionIds[0] = 0;
            _variableDropdownOptionCount = 1;
        }
    }

    private void EnsureVariableDropdownCapacity(int capacity)
    {
        if (_variableDropdownOptions.Length < capacity)
        {
            _variableDropdownOptions = new string[Math.Max(capacity, 8)];
        }

        if (_variableDropdownOptionIds.Length < capacity)
        {
            _variableDropdownOptionIds = new ushort[Math.Max(capacity, 8)];
        }
    }
}
