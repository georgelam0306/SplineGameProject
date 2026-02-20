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

internal sealed class PaintLayerOptionsPopover
{
    private const float InspectorPopoverGap = 6f;
    private const float PopoverWidth = 320f;
    private const float StrokeEffectsMinHeightRows = 12f;
    private const float KeyIconWidth = 18f;

    private static readonly string[] BlendModeOptions = new string[]
    {
        "Inherit",
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

    private static readonly string[] StrokeCapOptions = new string[]
    {
        "Butt",
        "Round",
        "Square",
        "Soft"
    };

    private static readonly string[] BlurDirectionOptions = new string[]
    {
        "Both",
        "Outside",
        "Inside"
    };

    private static readonly StringHandle StrokeWidthName = "Width";
    private static readonly StringHandle StrokeColorName = "Color";
    private static readonly StringHandle TrimEnabledName = "Enabled";
    private static readonly StringHandle TrimStartName = "Start";
    private static readonly StringHandle TrimLengthName = "Length";
    private static readonly StringHandle TrimOffsetName = "Offset";
    private static readonly StringHandle TrimCapName = "Cap";
    private static readonly StringHandle TrimCapSoftnessName = "Cap Softness";
    private static readonly StringHandle DashEnabledName = "Enabled";
    private static readonly StringHandle DashLengthName = "Dash Length";
    private static readonly StringHandle DashGapLengthName = "Gap Length";
    private static readonly StringHandle DashOffsetName = "Offset";
    private static readonly StringHandle DashCapName = "Cap";
    private static readonly StringHandle DashCapSoftnessName = "Cap Softness";

    private static readonly StringHandle TrimGroup = "Trim";
    private static readonly StringHandle DashGroup = "Dash";

    private readonly ColorPicker _colorPicker;

	private bool _isOpen;
	private int _layerIndex;
	private EntityId _ownerEntity;
	private int _openedFrame;
	private DerpLib.ImGui.Viewport.ImViewport? _viewport;
	private ImRect _anchorRectViewport;
	private bool _mouseDownStartedInside;
	private byte _strokeTab; // 0 = Options, 1 = Effects
    private ImRect _inspectorContentRectViewport;

    public PaintLayerOptionsPopover(ColorPicker colorPicker)
    {
        _colorPicker = colorPicker;
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

	public void Open(EntityId ownerEntity, int layerIndex, ImRect anchorRectViewport)
	{
		_isOpen = true;
		_ownerEntity = ownerEntity;
		_layerIndex = layerIndex;
		_anchorRectViewport = anchorRectViewport;
		_openedFrame = Im.Context.FrameCount;
		_viewport = Im.CurrentViewport;
        _mouseDownStartedInside = false;
        _strokeTab = 0;
    }

	public void Close()
	{
		_isOpen = false;
		_ownerEntity = EntityId.Null;
		_layerIndex = -1;
		_viewport = null;
		_mouseDownStartedInside = false;
	}

    public bool CapturesMouse(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
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
        if (viewport.Input.MouseReleased)
        {
            _mouseDownStartedInside = false;
        }

        return hovered || (_mouseDownStartedInside && viewport.Input.MouseDown);
    }

    public void RenderPopover(UiWorkspace workspace)
    {
        if (!_isOpen)
        {
            return;
        }

        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            Close();
            return;
        }

        if (_viewport != null && _viewport != viewport)
        {
            Close();
            return;
        }

        if (_ownerEntity.IsNull || _layerIndex < 0)
        {
            Close();
            return;
        }

        if (workspace._selectedEntities.Count != 1 || workspace._selectedEntities[0].Value != _ownerEntity.Value)
        {
            Close();
            return;
        }

        if (!workspace.World.TryGetComponent(_ownerEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            Close();
            return;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(workspace.PropertyWorld, paintHandle);
        int layerCount = paintView.IsAlive ? paintView.LayerCount : 0;
        if (_layerIndex < 0 || _layerIndex >= layerCount)
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
                closeOnOutsideButtons: ImPopoverCloseButtons.Left,
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

        Im.DrawRoundedRect(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, Im.Style.CornerRadius, Im.Style.Background);
        Im.DrawRoundedRectStroke(popupRect.X, popupRect.Y, popupRect.Width, popupRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

        // This popover is rendered in viewport space and may extend outside the inspector window.
        // Override the window content clip so nested widgets (TextInput, dropdowns, etc.) can push clips normally.
        Im.PushClipRectOverride(new ImRect(0f, 0f, viewport.Size.X, viewport.Size.Y));
        RenderContents(workspace, paintHandle, _layerIndex, popupRect);
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

        if (!_isOpen || _layerIndex < 0)
        {
            return false;
        }

        float padding = Im.Style.Padding;
        float spacing = Im.Style.Spacing;
        float rowHeight = Im.Style.MinButtonHeight;

        float width = Math.Clamp(PopoverWidth, 200f, Math.Max(200f, viewport.Size.X - InspectorPopoverGap * 2f));
        // Generous height; stroke effects can be larger (trim + dash).
        float height = padding * 2f + rowHeight * StrokeEffectsMinHeightRows + spacing * (StrokeEffectsMinHeightRows - 1f);
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

    private void RenderContents(UiWorkspace workspace, PaintComponentHandle paintHandle, int layerIndex, ImRect popupRect)
    {
        float padding = Im.Style.Padding;
        float spacing = Im.Style.Spacing;
        float rowHeight = Im.Style.MinButtonHeight;

        float x = popupRect.X + padding;
        float y = popupRect.Y + padding;
        float contentWidth = Math.Max(160f, popupRect.Width - padding * 2f);

        AnyComponentHandle component = PaintComponentProperties.ToAnyHandle(paintHandle);
        PaintLayerSettingsSlots settingsSlots = GetPaintLayerSettingsSlots(component, layerIndex);

        var kindSlot = PaintComponentPropertySlot.ArrayElement(component, "LayerKind", layerIndex, PropertyKind.Int);
        int kindValue = Math.Clamp(PropertyDispatcher.ReadInt(workspace.PropertyWorld, kindSlot), 0, 1);
        var layerKind = (PaintLayerKind)kindValue;

        uint ownerStableId = workspace.World.GetStableId(_ownerEntity);
        Im.Context.PushId((int)ownerStableId);
        Im.Context.PushId(layerIndex);

        if (layerKind == PaintLayerKind.Stroke)
        {
            DrawStrokeTabs(ref x, ref y, contentWidth, rowHeight, spacing);
            if (_strokeTab == 0)
            {
                DrawStrokeOptions(workspace, _ownerEntity, settingsSlots, component, layerIndex, x, ref y, contentWidth, rowHeight, spacing);
            }
            else
            {
                DrawStrokeEffects(workspace, component, layerIndex, x, ref y, contentWidth, rowHeight, spacing);
            }
        }
        else if (layerKind == PaintLayerKind.Fill)
        {
            var fillRect = new ImRect(x, y, contentWidth, rowHeight);
            PropertySlot fillColorSlot = PaintComponentPropertySlot.ArrayElement(component, "FillColor", layerIndex, PropertyKind.Color32);
            DrawColorRow(workspace, fillRect, "fill_color", fillColorSlot, showOpacity: false);
            y += rowHeight + spacing;

            var blendRect = new ImRect(x, y, contentWidth, rowHeight);
            DrawBlendModeRow(workspace, blendRect, settingsSlots);
            y += rowHeight + spacing;

            var opacityRect = new ImRect(x, y, contentWidth, rowHeight);
            DrawOpacityRow(workspace, opacityRect, settingsSlots);
            y += rowHeight + spacing;

            var offsetRect = new ImRect(x, y, contentWidth, rowHeight);
            DrawOffsetRow(workspace, offsetRect, settingsSlots);
            y += rowHeight + spacing;

            var blurRect = new ImRect(x, y, contentWidth, rowHeight);
            DrawBlurRow(workspace, blurRect, settingsSlots);
            y += rowHeight + spacing;

            var blurDirectionRect = new ImRect(x, y, contentWidth, rowHeight);
            DrawBlurDirectionRow(workspace, blurDirectionRect, settingsSlots);
        }

        Im.Context.PopId();
        Im.Context.PopId();
    }

    private static PaintLayerSettingsSlots GetPaintLayerSettingsSlots(AnyComponentHandle paintComponent, int layerIndex)
    {
        return new PaintLayerSettingsSlots
        {
            InheritBlendMode = PaintComponentPropertySlot.ArrayElement(paintComponent, "LayerInheritBlendMode", layerIndex, PropertyKind.Bool),
            BlendMode = PaintComponentPropertySlot.ArrayElement(paintComponent, "LayerBlendMode", layerIndex, PropertyKind.Int),
            Opacity = PaintComponentPropertySlot.ArrayElement(paintComponent, "LayerOpacity", layerIndex, PropertyKind.Float),
            Offset = PaintComponentPropertySlot.ArrayElement(paintComponent, "LayerOffset", layerIndex, PropertyKind.Vec2),
            Blur = PaintComponentPropertySlot.ArrayElement(paintComponent, "LayerBlur", layerIndex, PropertyKind.Float),
            BlurDirection = PaintComponentPropertySlot.ArrayElement(paintComponent, "LayerBlurDirection", layerIndex, PropertyKind.Int)
        };
    }

    private void DrawColorRow(UiWorkspace workspace, ImRect rect, string pickerId, PropertySlot slot, bool showOpacity)
    {
        workspace.PropertyInspector.DrawPropertyBindingContextMenu(_ownerEntity, slot, rect, allowUnbind: true);
        _colorPicker.DrawColor32PreviewRow(rect, pickerId, slot, showOpacity);
        if (workspace.Commands.TryGetKeyableState(slot, out bool hasTrack, out bool hasKey))
        {
            var iconRect = new ImRect(rect.Right - KeyIconWidth, rect.Y, KeyIconWidth, rect.Height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                workspace.Commands.ToggleKeyAtPlayhead(slot);
            }
        }
    }

    private void DrawStrokeTabs(ref float x, ref float y, float contentWidth, float rowHeight, float spacing)
    {
        float tabWidth = (contentWidth - spacing) * 0.5f;
        var optionsRect = new ImRect(x, y, tabWidth, rowHeight);
        var effectsRect = new ImRect(x + tabWidth + spacing, y, tabWidth, rowHeight);

        DrawTabButton(optionsRect, isActive: _strokeTab == 0, "Options".AsSpan());
        DrawTabButton(effectsRect, isActive: _strokeTab != 0, "Effects".AsSpan());

        if (optionsRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            _strokeTab = 0;
        }
        else if (effectsRect.Contains(Im.MousePos) && Im.MousePressed)
        {
            _strokeTab = 1;
        }

        y += rowHeight + spacing;
    }

    private static void DrawTabButton(ImRect rect, bool isActive, ReadOnlySpan<char> label)
    {
        uint bg = isActive ? ImStyle.Lerp(Im.Style.Surface, 0xFF000000, 0.22f) : Im.Style.Surface;
        uint border = isActive ? Im.Style.Primary : Im.Style.Border;
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, bg);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

        float textWidth = ApproxTextWidth(label, Im.Style.FontSize);
        float textX = rect.X + (rect.Width - textWidth) * 0.5f;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label, textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);
    }

    private static float ApproxTextWidth(ReadOnlySpan<char> text, float fontSize)
    {
        return text.Length * fontSize * 0.6f;
    }

    private static void DrawStrokeOptions(UiWorkspace workspace, EntityId ownerEntity, PaintLayerSettingsSlots settingsSlots, AnyComponentHandle paintComponent, int layerIndex, float x, ref float y, float contentWidth, float rowHeight, float spacing)
    {
        var widthSlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeWidth", layerIndex, PropertyKind.Float);
        var colorSlot = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeColor", layerIndex, PropertyKind.Color32);

        float labelWidth = 74f;
        float inputWidth = Math.Max(120f, contentWidth - labelWidth);

        float width = Math.Max(0f, PropertyDispatcher.ReadFloat(workspace.PropertyWorld, widthSlot));
        DrawFloatRow(workspace, "Width", "stroke_width", widthSlot, x, y, labelWidth, inputWidth, ref width, 0f, 1000f, "F1");
        y += rowHeight + spacing;

        var colorRect = new ImRect(x, y, contentWidth, rowHeight);
        workspace.PropertyInspector.DrawPropertyBindingContextMenu(ownerEntity, colorSlot, colorRect, allowUnbind: true);
        workspace.PropertyInspector.DrawColor32PreviewRow(colorRect, "stroke_color", colorSlot, showOpacity: false);
        if (workspace.Commands.TryGetKeyableState(colorSlot, out bool hasTrack, out bool hasKey))
        {
            var iconRect = new ImRect(colorRect.Right - KeyIconWidth, colorRect.Y, KeyIconWidth, colorRect.Height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                workspace.Commands.ToggleKeyAtPlayhead(colorSlot);
            }
        }
        y += rowHeight + spacing;

        var blendRect = new ImRect(x, y, contentWidth, rowHeight);
        DrawBlendModeRow(workspace, blendRect, settingsSlots);
        y += rowHeight + spacing;

        var offsetRect = new ImRect(x, y, contentWidth, rowHeight);
        DrawOffsetRow(workspace, offsetRect, settingsSlots);
        y += rowHeight + spacing;

        var blurRect = new ImRect(x, y, contentWidth, rowHeight);
        DrawBlurRow(workspace, blurRect, settingsSlots);
        y += rowHeight + spacing;

        var blurDirectionRect = new ImRect(x, y, contentWidth, rowHeight);
        DrawBlurDirectionRow(workspace, blurDirectionRect, settingsSlots);
    }

    private struct PaintLayerSettingsSlots
    {
        public PropertySlot InheritBlendMode;
        public PropertySlot BlendMode;
        public PropertySlot Opacity;
        public PropertySlot Offset;
        public PropertySlot Blur;
        public PropertySlot BlurDirection;
    }

    private struct StrokeSlots
    {
        public PropertySlot Width;
        public PropertySlot Color;

        public PropertySlot TrimEnabled;
        public PropertySlot TrimStart;
        public PropertySlot TrimLength;
        public PropertySlot TrimOffset;
        public PropertySlot TrimCap;
        public PropertySlot TrimCapSoftness;

        public PropertySlot DashEnabled;
        public PropertySlot DashLength;
        public PropertySlot DashGapLength;
        public PropertySlot DashOffset;
        public PropertySlot DashCap;
        public PropertySlot DashCapSoftness;
    }

    private static void DrawStrokeEffects(UiWorkspace workspace, AnyComponentHandle paintComponent, int layerIndex, float x, ref float y, float contentWidth, float rowHeight, float spacing)
    {
        StrokeSlots slots = new StrokeSlots
        {
            Width = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeWidth", layerIndex, PropertyKind.Float),
            Color = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeColor", layerIndex, PropertyKind.Color32),
            TrimEnabled = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeTrimEnabled", layerIndex, PropertyKind.Bool),
            TrimStart = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeTrimStart", layerIndex, PropertyKind.Float),
            TrimLength = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeTrimLength", layerIndex, PropertyKind.Float),
            TrimOffset = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeTrimOffset", layerIndex, PropertyKind.Float),
            TrimCap = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeTrimCap", layerIndex, PropertyKind.Int),
            TrimCapSoftness = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeTrimCapSoftness", layerIndex, PropertyKind.Float),
            DashEnabled = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeDashEnabled", layerIndex, PropertyKind.Bool),
            DashLength = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeDashLength", layerIndex, PropertyKind.Float),
            DashGapLength = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeDashGapLength", layerIndex, PropertyKind.Float),
            DashOffset = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeDashOffset", layerIndex, PropertyKind.Float),
            DashCap = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeDashCap", layerIndex, PropertyKind.Int),
            DashCapSoftness = PaintComponentPropertySlot.ArrayElement(paintComponent, "StrokeDashCapSoftness", layerIndex, PropertyKind.Float)
        };

        DrawSectionHeader("Trim Effect".AsSpan(), x, ref y, contentWidth, rowHeight, spacing);
        DrawStrokeTrimSection(workspace, slots, x, ref y, contentWidth, rowHeight, spacing);

        DrawSectionHeader("Dash Effect".AsSpan(), x, ref y, contentWidth, rowHeight, spacing);
        DrawStrokeDashSection(workspace, slots, x, ref y, contentWidth, rowHeight, spacing);
    }

    private static void DrawSectionHeader(ReadOnlySpan<char> label, float x, ref float y, float contentWidth, float rowHeight, float spacing)
    {
        float textY = y + (rowHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label, x, textY, Im.Style.FontSize, Im.Style.TextSecondary);
        y += rowHeight + spacing;
    }

    private static void DrawStrokeTrimSection(UiWorkspace workspace, in StrokeSlots slots, float x, ref float y, float contentWidth, float rowHeight, float spacing)
    {
        float labelWidth = 74f;
        float inputWidth = Math.Max(120f, contentWidth - labelWidth);

        DrawBoolRow(workspace, "Enabled", "trim_enabled", slots.TrimEnabled, x, y, labelWidth);
        y += rowHeight + spacing;

        float start = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.TrimStart);
        float length = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.TrimLength);
        float end = start + Math.Max(0f, length);

        bool startChanged = DrawFloatRow(workspace, "Start", "trim_start", slots.TrimStart, x, y, labelWidth, inputWidth, ref start, 0f, 100f, "F0");
        if (startChanged)
        {
            // Preserve end where possible.
            float newLength = Math.Max(0f, end - start);
            SetFloatSlot(workspace, "trim_length", slots.TrimLength, newLength);
        }
        y += rowHeight + spacing;

        DrawTrimEndRow(workspace, slots.TrimLength, start, x, y, labelWidth, inputWidth, ref end);
        y += rowHeight + spacing;

        float offset = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.TrimOffset);
        DrawFloatRow(workspace, "Offset", "trim_offset", slots.TrimOffset, x, y, labelWidth, inputWidth, ref offset, -100f, 100f, "F0");
        y += rowHeight + spacing;

        DrawCapRow(workspace, "Cap", "trim_cap", slots.TrimCap, x, y, labelWidth, inputWidth);
        y += rowHeight + spacing;

        float softness = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.TrimCapSoftness);
        DrawFloatRow(workspace, "Softness", "trim_soft", slots.TrimCapSoftness, x, y, labelWidth, inputWidth, ref softness, 0f, 200f, "F0");
        y += rowHeight + spacing;
    }

    private static void DrawStrokeDashSection(UiWorkspace workspace, in StrokeSlots slots, float x, ref float y, float contentWidth, float rowHeight, float spacing)
    {
        float labelWidth = 74f;
        float inputWidth = Math.Max(120f, contentWidth - labelWidth);

        DrawBoolRow(workspace, "Enabled", "dash_enabled", slots.DashEnabled, x, y, labelWidth);
        y += rowHeight + spacing;

        float dash = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.DashLength);
        DrawFloatRow(workspace, "Dash", "dash_len", slots.DashLength, x, y, labelWidth, inputWidth, ref dash, 0f, 1000f, "F0");
        y += rowHeight + spacing;

        float gap = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.DashGapLength);
        DrawFloatRow(workspace, "Gap", "dash_gap", slots.DashGapLength, x, y, labelWidth, inputWidth, ref gap, 0f, 1000f, "F0");
        y += rowHeight + spacing;

        float offset = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.DashOffset);
        DrawFloatRow(workspace, "Offset", "dash_off", slots.DashOffset, x, y, labelWidth, inputWidth, ref offset, -1000f, 1000f, "F0");
        y += rowHeight + spacing;

        DrawCapRow(workspace, "Cap", "dash_cap", slots.DashCap, x, y, labelWidth, inputWidth);
        y += rowHeight + spacing;

        float softness = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.DashCapSoftness);
        DrawFloatRow(workspace, "Softness", "dash_soft", slots.DashCapSoftness, x, y, labelWidth, inputWidth, ref softness, 0f, 200f, "F0");
        y += rowHeight + spacing;
    }

    private static void DrawTrimEndRow(UiWorkspace workspace, PropertySlot trimLengthSlot, float start, float x, float y, float labelWidth, float inputWidth, ref float end)
    {
        float height = Im.Style.MinButtonHeight;
        float textY = y + (height - Im.Style.FontSize) * 0.5f;
        Im.Text("End".AsSpan(), x, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        bool changed = ImScalarInput.DrawAt("trim_end", x + labelWidth, y, inputWidth, ref end, 0f, 100f, "F0");
        int widgetId = Im.Context.GetId("trim_end");
        bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
        if (changed)
        {
            float newLength = Math.Max(0f, end - start);
            workspace.Commands.SetPropertyValue(widgetId, isEditing, trimLengthSlot, PropertyValue.FromFloat(newLength));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }
    }

    private static void DrawBoolRow(UiWorkspace workspace, string label, string id, PropertySlot slot, float x, float y, float labelWidth)
    {
        float height = Im.Style.MinButtonHeight;
        float textY = y + (height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        bool value = PropertyDispatcher.ReadBool(workspace.PropertyWorld, slot);
        float checkboxY = y + (height - Im.Style.CheckboxSize) * 0.5f;
        int widgetId = Im.Context.GetId(id);
        if (Im.Checkbox(id, ref value, x + labelWidth, checkboxY))
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromBool(value));
        }
        workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
    }

    private static bool DrawFloatRow(UiWorkspace workspace, string label, string id, PropertySlot slot, float x, float y, float labelWidth, float inputWidth, ref float value, float min, float max, string format)
    {
        float height = Im.Style.MinButtonHeight;
        float textY = y + (height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        bool changed = ImScalarInput.DrawAt(id, x + labelWidth, y, inputWidth, ref value, min, max, format);
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

        return changed;
    }

    private static void SetFloatSlot(UiWorkspace workspace, string id, PropertySlot slot, float value)
    {
        int widgetId = Im.Context.GetId(id);
        workspace.Commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromFloat(value));
    }

    private static void DrawCapRow(UiWorkspace workspace, string label, string id, PropertySlot slot, float x, float y, float labelWidth, float inputWidth)
    {
        float height = Im.Style.MinButtonHeight;
        float textY = y + (height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        int selectedIndex = PropertyDispatcher.ReadInt(workspace.PropertyWorld, slot);
        if ((uint)selectedIndex >= (uint)StrokeCapOptions.Length)
        {
            selectedIndex = 0;
        }

        int widgetId = Im.Context.GetId(id);
        if (Im.Dropdown(id, StrokeCapOptions, ref selectedIndex, x + labelWidth, y, inputWidth))
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromInt(selectedIndex));
        }
        workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
    }

    private static bool TryResolveStrokeSlots(AnyComponentHandle component, int propertyCount, out StrokeSlots slots)
    {
        slots = default;

        bool hasWidth = false;
        bool hasColor = false;
        bool hasTrimEnabled = false;
        bool hasTrimStart = false;
        bool hasTrimLength = false;
        bool hasTrimOffset = false;
        bool hasTrimCap = false;
        bool hasTrimSoft = false;
        bool hasDashEnabled = false;
        bool hasDashLen = false;
        bool hasDashGap = false;
        bool hasDashOff = false;
        bool hasDashCap = false;
        bool hasDashSoft = false;

        // Resolve by group+name since some properties share names across groups ("Enabled", "Offset", "Cap").
        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            var slot = new PropertySlot(component, (ushort)propertyIndex, info.PropertyId, info.Kind);

            if (!hasWidth && info.Name == StrokeWidthName)
            {
                slots.Width = slot;
                hasWidth = true;
                continue;
            }

            if (!hasColor && info.Name == StrokeColorName)
            {
                slots.Color = slot;
                hasColor = true;
                continue;
            }

            if (info.Group == TrimGroup)
            {
                if (!hasTrimEnabled && info.Name == TrimEnabledName && info.Kind == PropertyKind.Bool)
                {
                    slots.TrimEnabled = slot;
                    hasTrimEnabled = true;
                }
                else if (!hasTrimStart && info.Name == TrimStartName && info.Kind == PropertyKind.Float)
                {
                    slots.TrimStart = slot;
                    hasTrimStart = true;
                }
                else if (!hasTrimLength && info.Name == TrimLengthName && info.Kind == PropertyKind.Float)
                {
                    slots.TrimLength = slot;
                    hasTrimLength = true;
                }
                else if (!hasTrimOffset && info.Name == TrimOffsetName && info.Kind == PropertyKind.Float)
                {
                    slots.TrimOffset = slot;
                    hasTrimOffset = true;
                }
                else if (!hasTrimCap && info.Name == TrimCapName && info.Kind == PropertyKind.Int)
                {
                    slots.TrimCap = slot;
                    hasTrimCap = true;
                }
                else if (!hasTrimSoft && info.Name == TrimCapSoftnessName && info.Kind == PropertyKind.Float)
                {
                    slots.TrimCapSoftness = slot;
                    hasTrimSoft = true;
                }
            }
            else if (info.Group == DashGroup)
            {
                if (!hasDashEnabled && info.Name == DashEnabledName && info.Kind == PropertyKind.Bool)
                {
                    slots.DashEnabled = slot;
                    hasDashEnabled = true;
                }
                else if (!hasDashLen && info.Name == DashLengthName && info.Kind == PropertyKind.Float)
                {
                    slots.DashLength = slot;
                    hasDashLen = true;
                }
                else if (!hasDashGap && info.Name == DashGapLengthName && info.Kind == PropertyKind.Float)
                {
                    slots.DashGapLength = slot;
                    hasDashGap = true;
                }
                else if (!hasDashOff && info.Name == DashOffsetName && info.Kind == PropertyKind.Float)
                {
                    slots.DashOffset = slot;
                    hasDashOff = true;
                }
                else if (!hasDashCap && info.Name == DashCapName && info.Kind == PropertyKind.Int)
                {
                    slots.DashCap = slot;
                    hasDashCap = true;
                }
                else if (!hasDashSoft && info.Name == DashCapSoftnessName && info.Kind == PropertyKind.Float)
                {
                    slots.DashCapSoftness = slot;
                    hasDashSoft = true;
                }
            }
        }

        return hasWidth && hasColor &&
            hasTrimEnabled && hasTrimStart && hasTrimLength && hasTrimOffset && hasTrimCap && hasTrimSoft &&
            hasDashEnabled && hasDashLen && hasDashGap && hasDashOff && hasDashCap && hasDashSoft;
    }

    private static void DrawBlendModeRow(UiWorkspace workspace, ImRect rect, PaintLayerSettingsSlots slots)
    {
        float labelWidth = 74f;
        float inputWidth = Math.Max(120f, rect.Width - labelWidth);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;

        Im.Text("Blend".AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        bool inheritBlend = PropertyDispatcher.ReadBool(workspace.PropertyWorld, slots.InheritBlendMode);
        int blendMode = PropertyDispatcher.ReadInt(workspace.PropertyWorld, slots.BlendMode);
        int selectedIndex = inheritBlend ? 0 : blendMode + 1;
        if ((uint)selectedIndex >= (uint)BlendModeOptions.Length)
        {
            selectedIndex = 0;
        }

        int widgetId = Im.Context.GetId("blend_mode");
        if (Im.Dropdown("blend_mode", BlendModeOptions, ref selectedIndex, rect.X + labelWidth, rect.Y, inputWidth))
        {
            bool newInheritBlend = selectedIndex <= 0;
            int newBlendMode = (int)PaintBlendMode.Normal;
            if (!newInheritBlend)
            {
                newBlendMode = selectedIndex - 1;
                if (newBlendMode < 0)
                {
                    newBlendMode = 0;
                }
                else if (newBlendMode > 15)
                {
                    newBlendMode = 15;
                }
            }

            workspace.Commands.SetPropertyBatch2(
                widgetId,
                isEditing: false,
                slots.InheritBlendMode,
                PropertyValue.FromBool(newInheritBlend),
                slots.BlendMode,
                PropertyValue.FromInt(newBlendMode));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        }
    }

    private static void DrawOpacityRow(UiWorkspace workspace, ImRect rect, PaintLayerSettingsSlots slots)
    {
        float labelWidth = 74f;
        float inputWidth = Math.Max(120f, rect.Width - labelWidth);

        float opacity = PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.Opacity);
        float opacityPercent = Math.Clamp(opacity, 0f, 1f) * 100f;
        bool changed = ImScalarInput.DrawAt("Opacity", "opacity", rect.X, rect.Y, labelWidth, inputWidth, ref opacityPercent, 0f, 100f, "F0");

        int widgetId = Im.Context.GetId("opacity");
        bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);

        float newOpacity = Math.Clamp(opacityPercent / 100f, 0f, 1f);
        if (changed)
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing, slots.Opacity, PropertyValue.FromFloat(newOpacity));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }

        if (workspace.Commands.TryGetKeyableState(slots.Opacity, out bool hasTrack, out bool hasKey))
        {
            var inputRect = new ImRect(rect.X + labelWidth, rect.Y, inputWidth, rect.Height);
            var iconRect = new ImRect(inputRect.Right - KeyIconWidth, inputRect.Y, KeyIconWidth, inputRect.Height);
            DrawKeyIconDiamond(iconRect, filled: hasKey, highlighted: hasTrack);
            if (iconRect.Contains(Im.MousePos) && Im.MousePressed)
            {
                workspace.Commands.ToggleKeyAtPlayhead(slots.Opacity);
            }
        }
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

    private static void DrawOffsetRow(UiWorkspace workspace, ImRect rect, PaintLayerSettingsSlots slots)
    {
        float labelWidth = 74f;
        float inputWidth = Math.Max(120f, rect.Width - labelWidth);

        Vector2 offset = PropertyDispatcher.ReadVec2(workspace.PropertyWorld, slots.Offset);

        bool changed = ImVectorInput.DrawAt("Offset", "offset", rect.X, rect.Y, labelWidth, inputWidth, ref offset, format: "F1");

        int parentWidgetId = Im.Context.GetId("offset");
        int widgetIdX = parentWidgetId * 31 + 0;
        int widgetIdY = parentWidgetId * 31 + 1;

        bool isEditingX = Im.Context.IsActive(widgetIdX) || Im.Context.IsFocused(widgetIdX);
        bool isEditingY = Im.Context.IsActive(widgetIdY) || Im.Context.IsFocused(widgetIdY);
        bool isEditing = isEditingX || isEditingY;

        if (changed)
        {
            int recordWidgetId;
            if (Im.Context.IsActive(widgetIdX) || Im.Context.IsFocused(widgetIdX))
            {
                recordWidgetId = widgetIdX;
            }
            else if (Im.Context.IsActive(widgetIdY) || Im.Context.IsFocused(widgetIdY))
            {
                recordWidgetId = widgetIdY;
            }
            else
            {
                recordWidgetId = parentWidgetId;
            }

            workspace.Commands.SetPropertyValue(recordWidgetId, isEditing, slots.Offset, PropertyValue.FromVec2(offset));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetIdX, isEditingX);
            workspace.Commands.NotifyPropertyWidgetState(widgetIdY, isEditingY);
        }
    }

    private static void DrawBlurRow(UiWorkspace workspace, ImRect rect, PaintLayerSettingsSlots slots)
    {
        float labelWidth = 74f;
        float inputWidth = Math.Max(120f, rect.Width - labelWidth);

        float blurWorld = Math.Max(0f, PropertyDispatcher.ReadFloat(workspace.PropertyWorld, slots.Blur));
        bool changed = ImScalarInput.DrawAt("Blur", "blur", rect.X, rect.Y, labelWidth, inputWidth, ref blurWorld, 0f, 500f, "F1");

        int widgetId = Im.Context.GetId("blur");
        bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);

        if (changed)
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing, slots.Blur, PropertyValue.FromFloat(Math.Max(0f, blurWorld)));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
        }
    }

    private static void DrawBlurDirectionRow(UiWorkspace workspace, ImRect rect, PaintLayerSettingsSlots slots)
    {
        float labelWidth = 74f;
        float inputWidth = Math.Max(120f, rect.Width - labelWidth);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;

        Im.Text("Direction".AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        int direction = PropertyDispatcher.ReadInt(workspace.PropertyWorld, slots.BlurDirection);
        int selectedIndex = Math.Clamp(direction, 0, 2);

        int widgetId = Im.Context.GetId("blur_dir");
        if (Im.Dropdown("blur_dir", BlurDirectionOptions, ref selectedIndex, rect.X + labelWidth, rect.Y, inputWidth))
        {
            workspace.Commands.SetPropertyValue(widgetId, isEditing: false, slots.BlurDirection, PropertyValue.FromInt(selectedIndex));
        }
        else
        {
            workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing: false);
        }
    }
}
